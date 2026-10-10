// Fighter.Core — fixed-point 2D vector (REFACTOR+HARDEN, A1).
// The authority numeric vector for deterministic physics (PhysicsStep).
// Vector2I (millipixels) remains the positional substrate used by the live
// sim; Vector2Fixed is the Fixed-based counterpart for sub-pixel integration
// paths. Migration of live physics is a tracked PLANNED item (ADR-004), not
// part of this change: no behavior was moved, so no hash can drift.
namespace Fighter.Core;

/// <summary>2D vector over <see cref="Fixed"/> (Q16.16 per component).
/// The deterministic physics vector: no floats, no Godot types.</summary>
public readonly struct Vector2Fixed : IEquatable<Vector2Fixed>
{
    /// <summary>X component.</summary>
    public readonly Fixed X;
    /// <summary>Y component.</summary>
    public readonly Fixed Y;

    /// <summary>Constructs from fixed components.</summary>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    public Vector2Fixed(Fixed x, Fixed y) { X = x; Y = y; }

    /// <summary>Constructs from integer components (exact).</summary>
    /// <param name="x">X integer.</param>
    /// <param name="y">Y integer.</param>
    /// <returns>Exact fixed vector.</returns>
    public static Vector2Fixed FromInts(int x, int y) => new(Fixed.FromInt(x), Fixed.FromInt(y));

    /// <summary>Origin.</summary>
    public static readonly Vector2Fixed Zero = new(Fixed.Zero, Fixed.Zero);

    /// <summary>Component-wise add.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Component-wise sum.</returns>
    public static Vector2Fixed operator +(Vector2Fixed a, Vector2Fixed b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Component-wise subtract.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>Component-wise difference.</returns>
    public static Vector2Fixed operator -(Vector2Fixed a, Vector2Fixed b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Scalar multiply.</summary>
    /// <param name="v">Vector.</param>
    /// <param name="s">Scalar.</param>
    /// <returns>Scaled vector.</returns>
    public static Vector2Fixed operator *(Vector2Fixed v, Fixed s) => new(v.X * s, v.Y * s);

    /// <summary>Squared magnitude (avoids sqrt; exact fixed arithmetic).</summary>
    /// <returns>X*X + Y*Y.</returns>
    public Fixed LengthSquared() => X * X + Y * Y;

    /// <summary>Component equality.</summary>
    /// <param name="other">Value to compare.</param>
    /// <returns>True when both components are raw-equal.</returns>
    public bool Equals(Vector2Fixed other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <summary>Type-checked equality.</summary>
    /// <param name="obj">Value to compare.</param>
    /// <returns>True when <paramref name="obj"/> is an equal vector.</returns>
    public override bool Equals(object? obj) => obj is Vector2Fixed v && Equals(v);

    /// <summary>Combined hash.</summary>
    /// <returns>Combined raw hash.</returns>
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);

    /// <summary>Component equality.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when components are raw-equal.</returns>
    public static bool operator ==(Vector2Fixed a, Vector2Fixed b) => a.Equals(b);

    /// <summary>Component inequality.</summary>
    /// <param name="a">First operand.</param>
    /// <param name="b">Second operand.</param>
    /// <returns>True when any component differs.</returns>
    public static bool operator !=(Vector2Fixed a, Vector2Fixed b) => !a.Equals(b);

    /// <summary>Debug format (never parsed).</summary>
    /// <returns>Debug string.</returns>
    public override string ToString() => $"({X},{Y})";
}
