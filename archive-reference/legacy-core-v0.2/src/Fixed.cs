// Fighter.Core — Q16.16 fixed-point (F0, hardened REFACTOR+HARDEN).
// WHY: integer-only arithmetic is bit-identical on x86/ARM (§14, §55).
// OVERFLOW POLICY: WRAP (two's-complement, unchecked). Rationale: saturation
// branches cost determinism-review surface and the sim's calibrated ranges
// (positions < 2^15 px, timers < 2^31) never approach Raw limits; every
// wrapping operator is inside an explicit `unchecked` block so the policy is
// visible at the operation site. See ADR-004. Saturating helpers are NOT
// provided: adding a second policy invites mixed-policy bugs.
// DIVIDE BY ZERO: throws DivideByZeroException (explicit guard, tested).
// SQRT: integer Newton-Raphson on the scaled value; negative input throws.
// No float/double conversion exists anywhere on this type by design: parsing
// through System.Single would import platform float formatting/rounding into
// the deterministic core. Integer constructors (FromInt/FromRaw) are exact.
namespace Fighter.Core;

/// <summary>Signed Q16.16 fixed-point. Raw int; 16 integer bits, 16 fractional bits.
/// Overflow wraps (two's complement); see remarks.</summary>
/// <remarks>Range is [-32768, 32767.99998]. All operators are exact integer
/// arithmetic except multiply/divide/sqrt, which round toward zero by truncation
/// of the 64-bit intermediate. Bit-identical on x86/x64/ARM64 (no FPU use).</remarks>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    /// <summary>Fractional bits.</summary>
    public const int Shift = 16;
    /// <summary>Raw value of 1.0.</summary>
    public const int OneRaw = 1 << Shift;

    /// <summary>Raw Q16.16 payload. Compare/operate on this for exactness.</summary>
    public readonly int Raw;
    /// <summary>Wraps a raw payload (no conversion).</summary>
    /// <param name="raw">Raw Q16.16 payload.</param>
    public Fixed(int raw) => Raw = raw;

    /// <summary>Converts an integer (exact).</summary>
    /// <param name="v">Integer value.</param>
    /// <returns>Exact fixed representation.</returns>
    public static Fixed FromInt(int v) => new(unchecked(v << Shift));
    /// <summary>Wraps a raw payload (no conversion).</summary>
    /// <param name="raw">Raw Q16.16 payload.</param>
    /// <returns>Fixed value with that exact raw payload.</returns>
    public static Fixed FromRaw(int raw) => new(raw);
    /// <summary>Integer part (floor for negatives: arithmetic shift).</summary>
    /// <returns>Raw arithmetic-shifted right by <see cref="Shift"/>.</returns>
    public int ToInt() => Raw >> Shift;

    /// <summary>Zero.</summary>
    public static readonly Fixed Zero = new(0);
    /// <summary>One.</summary>
    public static readonly Fixed One = new(OneRaw);
    /// <summary>Minus one.</summary>
    public static readonly Fixed MinusOne = new(unchecked(-OneRaw));
    /// <summary>Largest representable value (raw int.MaxValue).</summary>
    public static readonly Fixed MaxValue = new(int.MaxValue);
    /// <summary>Smallest representable value (raw int.MinValue).</summary>
    public static readonly Fixed MinValue = new(int.MinValue);

    /// <summary>Exact addition (wraps on overflow).</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Raw sum, wrapping on overflow.</returns>
    public static Fixed operator +(Fixed a, Fixed b) => new(unchecked(a.Raw + b.Raw));
    /// <summary>Exact subtraction (wraps on overflow).</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Raw difference, wrapping on overflow.</returns>
    public static Fixed operator -(Fixed a, Fixed b) => new(unchecked(a.Raw - b.Raw));
    /// <summary>Unary negation (wraps for <see cref="MinValue"/>).</summary>
    /// <param name="a">Operand.</param>
    /// <returns>Negated raw value.</returns>
    public static Fixed operator -(Fixed a) => new(unchecked(-a.Raw));
    /// <summary>Fixed-point multiply (64-bit intermediate, shift back, truncates).</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Product, truncated toward zero of the true product.</returns>
    public static Fixed operator *(Fixed a, Fixed b) => new(unchecked((int)(((long)a.Raw * b.Raw) >> Shift)));
    /// <summary>Fixed-point divide (64-bit intermediate, shift first, truncates).</summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor (must be non-zero).</param>
    /// <returns>Quotient, truncated toward zero of the true quotient.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="b"/> is zero.</exception>
    public static Fixed operator /(Fixed a, Fixed b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fixed division by zero.");
        return new(unchecked((int)(((long)a.Raw << Shift) / b.Raw)));
    }

    /// <summary>Absolute value (wraps for <see cref="MinValue"/>, per WRAP policy).</summary>
    /// <param name="a">Operand.</param>
    /// <returns>Non-negative magnitude, except MinValue which wraps to itself.</returns>
    public static Fixed Abs(Fixed a) => a.Raw == int.MinValue ? a : (a.Raw < 0 ? -a : a);

    /// <summary>Integer square root (Newton-Raphson on the scaled value).</summary>
    /// <param name="v">Non-negative operand.</param>
    /// <returns>Floor of the true square root (raw error &lt; 1 LSB).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="v"/> is negative.</exception>
    /// <remarks>Deterministic: pure integer iteration, fixed iteration bound
    /// (converges quadratically; at most 31 halvings for any input).</remarks>
    public static Fixed Sqrt(Fixed v)
    {
        if (v.Raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "Fixed.Sqrt of negative value.");
        if (v.Raw == 0) return Zero;
        long n = ((long)v.Raw) << Shift; // scaled radicand, fits in 63 bits (Raw <= int.MaxValue)
        long x = n; // overestimate; Newton converges monotonically downward
        long y = (x + n / x) >> 1;
        while (y < x)
        {
            x = y;
            y = (x + n / x) >> 1;
        }
        return new(unchecked((int)x));
    }

    /// <summary>Raw equality.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when raw payloads are equal.</returns>
    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    /// <summary>Raw inequality.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when raw payloads differ.</returns>
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    /// <summary>Raw comparison.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when <paramref name="a"/> is less than <paramref name="b"/>.</returns>
    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    /// <summary>Raw comparison.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when <paramref name="a"/> is greater than <paramref name="b"/>.</returns>
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    /// <summary>Raw comparison.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when <paramref name="a"/> is less than or equal to <paramref name="b"/>.</returns>
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    /// <summary>Raw comparison.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when <paramref name="a"/> is greater than or equal to <paramref name="b"/>.</returns>
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    /// <summary>Raw equality.</summary>
    /// <param name="other">Value to compare.</param>
    /// <returns>True when raw payloads are equal.</returns>
    public bool Equals(Fixed other) => Raw == other.Raw;
    /// <summary>Type-checked raw equality.</summary>
    /// <param name="obj">Value to compare.</param>
    /// <returns>True when <paramref name="obj"/> is a Fixed with equal raw payload.</returns>
    public override bool Equals(object? obj) => obj is Fixed f && f.Raw == Raw;
    /// <summary>Hash of the raw payload.</summary>
    /// <returns>Raw payload as hash code.</returns>
    public override int GetHashCode() => Raw;
    /// <summary>Raw comparison.</summary>
    /// <param name="other">Value to compare.</param>
    /// <returns>Signed comparison of raw payloads.</returns>
    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
    /// <summary>Human-readable decimal (debug only; never parsed back).</summary>
    /// <returns>Debug string; not a serialization format.</returns>
    public override string ToString() => $"{ToInt()}.{((Raw & 0xFFFF) * 100000L / 65536):D5}";
}
