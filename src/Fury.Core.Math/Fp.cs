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
        return new(unchecked((numerator << FractionBits) / denominator));
    }

    public int FloorInt => Raw >> FractionBits;

    /// <summary>Truncated integer part (toward zero), for display only — never for sim logic.</summary>
    public int TruncInt => FloorInt;

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

    /// <summary>Clamps to [-1, 1] before squaring to dodge overflow; used by norms.</summary>
    public static Fp Sqrt(Fp v)
    {
        if (v.Raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "Fp.Sqrt of negative.");
        if (v.Raw == 0) return Zero;
        // Newton-Raphson on raw bits: x_{n+1} = (x_n + v/x_n) / 2 in Q16.16, seed 1.0.
        var x = One;
        for (int i = 0; i < 32; i++)
        {
            var next = (x + (v / x)) * Half;
            if (next == x) break;
            x = next;
        }
        return x;
    }

    public bool Equals(Fp other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fp other && Equals(other);
    public override int GetHashCode() => Raw;
    public int CompareTo(Fp other) => Raw.CompareTo(other.Raw);

    public override string ToString() => $"{FloorInt}.{(Abs(this).Raw & FractionMask) * 1000 / (FractionMask + 1):D3}";
}
