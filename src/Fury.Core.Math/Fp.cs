namespace Fury.Core.Math;

/// <summary>
/// Q16.16 fixed-point number: the single deterministic numeric primitive of the engine.
/// All combat math runs on this type so results are bit-identical on every CPU/arch.
/// Arithmetic overflow wraps (unchecked) by policy: wrap-around is part of the contract.
/// </summary>
public readonly struct Fp : IEquatable<Fp>, IComparable<Fp>
{
    public const int FractionBits = 16;
    public const int FractionMask = (1 << FractionBits) - 1;

    /// <summary>Raw Q16.16 bits. 1.0 == 0x10000.</summary>
    public int Raw { get; }

    public static readonly Fp Zero = new(0);
    public static readonly Fp One = new(1 << FractionBits);
    public static readonly Fp MinusOne = new(-(1 << FractionBits));
    public static readonly Fp Half = new(1 << (FractionBits - 1));
    public static readonly Fp MaxValue = new(int.MaxValue);
    public static readonly Fp MinValue = new(int.MinValue);

    private Fp(int raw) => Raw = raw;

    /// <summary>Build from raw Q16.16 bits. Preferred constructor in sim code.</summary>
    public static Fp FromRaw(int raw) => new(raw);

    /// <summary>Build from whole units. Exact.</summary>
    public static Fp FromInt(int units) => new(unchecked(units << FractionBits));

    /// <summary>
    /// Build from a numerator/denominator pair. Exact when the division terminates,
    /// otherwise truncates toward zero. Avoids any float path.
    /// </summary>
    public static Fp FromRatio(int numerator, int denominator)
    {
        if (denominator == 0) throw new DivideByZeroException("Fp.FromRatio denominator is zero.");
        return new(unchecked((int)(((long)numerator << FractionBits) / denominator)));
    }

    public int FloorInt => Raw >> FractionBits;

    /// <summary>Truncated integer part (toward zero), for display only — never for sim logic.</summary>
    /// <remarks>Antes devolvía <see cref="FloorInt"/>: para negativos redondeaba hacia -∞
    /// (-0.5 → -1) en vez de hacia cero (-0.5 → 0), contradiciendo su propio contrato.</remarks>
    public int TruncInt => Raw >= 0 ? Raw >> FractionBits : -(int)((-(long)Raw) >> FractionBits);

    /// <summary>
    /// Converts to float. Presentation boundary only (rendering, debug UI) — never in sim logic,
    /// rollback snapshots, or golden-hash paths (ADR-005).
    /// </summary>
    public float ToFloat() => Raw / (float)(1 << FractionBits);

    public static Fp operator +(Fp a, Fp b) => new(unchecked(a.Raw + b.Raw));
    public static Fp operator -(Fp a, Fp b) => new(unchecked(a.Raw - b.Raw));
    public static Fp operator -(Fp a) => new(unchecked(-a.Raw));
    public static Fp operator *(Fp a, Fp b) => new(unchecked((int)(((long)a.Raw * b.Raw) >> FractionBits)));
    public static Fp operator /(Fp a, Fp b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fp division by zero.");
        return new(unchecked((int)(((long)a.Raw << FractionBits) / b.Raw)));
    }

    public static Fp operator <<(Fp a, int shift) => new(unchecked(a.Raw << shift));
    public static Fp operator >>(Fp a, int shift) => new(unchecked(a.Raw >> shift));

    public static bool operator ==(Fp a, Fp b) => a.Raw == b.Raw;
    public static bool operator !=(Fp a, Fp b) => a.Raw != b.Raw;
    public static bool operator <(Fp a, Fp b) => a.Raw < b.Raw;
    public static bool operator >(Fp a, Fp b) => a.Raw > b.Raw;
    public static bool operator <=(Fp a, Fp b) => a.Raw <= b.Raw;
    public static bool operator >=(Fp a, Fp b) => a.Raw >= b.Raw;

    public static Fp Abs(Fp v) => v.Raw < 0 ? -v : v;
    public static Fp Min(Fp a, Fp b) => a < b ? a : b;
    public static Fp Max(Fp a, Fp b) => a > b ? a : b;
    public static Fp Clamp(Fp v, Fp lo, Fp hi) => Min(Max(v, lo), hi);

    /// <summary>
    /// Square root, exact floor in Q16.16: sqrt(raw / 2^16) * 2^16 == isqrt(raw * 2^16).
    /// Pure 64-bit integer arithmetic (bit-by-bit), so it is deterministic and cannot overflow.
    /// The previous Newton-Raphson iterated in Q16.16 and overflowed for v near
    /// <see cref="MaxValue"/> (x + v/x wrapped), returning garbage.
    /// </summary>
    public static Fp Sqrt(Fp v)
    {
        if (v.Raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "Fp.Sqrt of negative.");
        if (v.Raw == 0) return Zero;
        ulong n = (ulong)v.Raw << FractionBits;
        ulong result = 0;
        ulong bit = 1UL << 62;
        while (bit > n) bit >>= 2;
        while (bit != 0)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return new((int)result);
    }

    /// <summary>π in Q16.16 (205887 / 65536 ≈ 3.14159).</summary>
    public static readonly Fp Pi = new(205887);

    /// <summary>Converts degrees to radians (deterministic).</summary>
    public static Fp DegreesToRadians(Fp degrees) => degrees * Pi / FromInt(180);

    /// <summary>Sine with range reduction to [-π/2, π/2] and a 9th-order odd Taylor polynomial
    /// (error &lt; 4e-6 rad on the reduced interval, below Q16.16 resolution).</summary>
    public static Fp Sin(Fp angle)
    {
        Fp twoPi = Pi + Pi;
        Fp halfPi = Pi >> 1;
        Fp x = FromRaw((int)(angle.Raw % twoPi.Raw)); // (-2π, 2π)
        if (x > Pi) x -= twoPi;
        if (x < -Pi) x += twoPi;                         // [-π, π]
        if (x > halfPi) x = Pi - x;                      // sin(π - x) = sin(x)
        if (x < -halfPi) x = -Pi - x;
        Fp x2 = x * x;
        // Horner: x(1 - x²/6(1 - x²/20(1 - x²/42(1 - x²/72))))
        Fp t = One - (x2 / FromInt(72));
        t = One - (x2 / FromInt(42) * t);
        t = One - (x2 / FromInt(20) * t);
        t = One - (x2 / FromInt(6) * t);
        return x * t;
    }

    /// <summary>Cosine via the phase identity cos(x) = sin(x + π/2).</summary>
    public static Fp Cos(Fp angle) => Sin(angle + (Pi >> 1));

    /// <summary>
    /// Tangent as sin/cos (deterministic). The previous 4-term Taylor series was only valid
    /// near 0: tan(π/3) gave 1.62 instead of 1.73 and tan(1.5) gave 4.3 instead of 14.1.
    /// Throws <see cref="DivideByZeroException"/> exactly at ±π/2 (cos == 0).
    /// </summary>
    public static Fp Tan(Fp angle) => Sin(angle) / Cos(angle);

    public bool Equals(Fp other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fp other && Equals(other);
    public override int GetHashCode() => Raw;
    public int CompareTo(Fp other) => Raw.CompareTo(other.Raw);

    /// <remarks>Antes usaba <see cref="FloorInt"/> con la fracción del valor absoluto, así que
    /// -0.5 se mostraba "-1.500". Ahora signo + parte entera + fracción del valor absoluto.</remarks>
    public override string ToString()
    {
        long abs = System.Math.Abs((long)Raw);
        string sign = Raw < 0 ? "-" : "";
        return $"{sign}{abs >> FractionBits}.{(abs & FractionMask) * 1000 / (FractionMask + 1):D3}";
    }
}
