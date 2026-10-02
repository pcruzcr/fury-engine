namespace Fury.Core.Math;

/// <summary>
/// 3D vector over Fp for 2.5D/3D rendering. The simulation remains 2D (X, Y),
/// but Z is used for depth sorting, visual effects, and future 3D gameplay.
/// All operations are deterministic integer math.
/// </summary>
public readonly struct FpVec3 : IEquatable<FpVec3>
{
    public Fp X { get; }
    public Fp Y { get; }
    public Fp Z { get; }

    public static readonly FpVec3 Zero = new(Fp.Zero, Fp.Zero, Fp.Zero);
    public static readonly FpVec3 One = new(Fp.One, Fp.One, Fp.One);
    public static readonly FpVec3 Up = new(Fp.Zero, Fp.One, Fp.Zero);
    public static readonly FpVec3 Right = new(Fp.One, Fp.Zero, Fp.Zero);
    public static readonly FpVec3 Forward = new(Fp.Zero, Fp.Zero, Fp.One);

    public FpVec3(Fp x, Fp y, Fp z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public FpVec3(Fp x, Fp y) : this(x, y, Fp.Zero) { }

    public Fp LengthSquared => X * X + Y * Y + Z * Z;

    public Fp Length => Fp.Sqrt(LengthSquared);

    public FpVec3 Normalized()
    {
        // Fast path for axis-aligned vectors
        if (X == Fp.Zero && Y == Fp.Zero && Z == Fp.Zero) return Zero;
        if (X == Fp.Zero && Y == Fp.Zero) return new FpVec3(Fp.Zero, Fp.Zero, Z > Fp.Zero ? Fp.One : Fp.MinusOne);
        if (X == Fp.Zero && Z == Fp.Zero) return new FpVec3(Fp.Zero, Y > Fp.Zero ? Fp.One : Fp.MinusOne, Fp.Zero);
        if (Y == Fp.Zero && Z == Fp.Zero) return new FpVec3(X > Fp.Zero ? Fp.One : Fp.MinusOne, Fp.Zero, Fp.Zero);
        
        Fp len = Length;
        if (len == Fp.Zero) return Zero;
        return new FpVec3(X / len, Y / len, Z / len);
    }

    public Fp Dot(FpVec3 other) => X * other.X + Y * other.Y + Z * other.Z;

    public FpVec3 Cross(FpVec3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X
    );

    public Fp DistanceSquared(FpVec3 other)
    {
        Fp dx = X - other.X;
        Fp dy = Y - other.Y;
        Fp dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public Fp Distance(FpVec3 other) => Fp.Sqrt(DistanceSquared(other));

    public static FpVec3 operator +(FpVec3 a, FpVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static FpVec3 operator -(FpVec3 a, FpVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static FpVec3 operator *(FpVec3 a, Fp s) => new(a.X * s, a.Y * s, a.Z * s);
    public static FpVec3 operator *(Fp s, FpVec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static FpVec3 operator /(FpVec3 a, Fp s) => new(a.X / s, a.Y / s, a.Z / s);
    public static FpVec3 operator -(FpVec3 a) => new(-a.X, -a.Y, -a.Z);

    public static bool operator ==(FpVec3 a, FpVec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(FpVec3 a, FpVec3 b) => !(a == b);

    public bool Equals(FpVec3 other) => this == other;
    public override bool Equals(object? obj) => obj is FpVec3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() => $"({X}, {Y}, {Z})";

    /// <summary>
    /// Linear interpolation between two vectors.
    /// </summary>
    public static FpVec3 Lerp(FpVec3 a, FpVec3 b, Fp t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    /// Component-wise minimum.
    /// </summary>
    public static FpVec3 Min(FpVec3 a, FpVec3 b) => new(
        Fp.Min(a.X, b.X),
        Fp.Min(a.Y, b.Y),
        Fp.Min(a.Z, b.Z)
    );

    /// <summary>
    /// Component-wise maximum.
    /// </summary>
    public static FpVec3 Max(FpVec3 a, FpVec3 b) => new(
        Fp.Max(a.X, b.X),
        Fp.Max(a.Y, b.Y),
        Fp.Max(a.Z, b.Z)
    );

    /// <summary>
    /// Component-wise clamp.
    /// </summary>
    public static FpVec3 Clamp(FpVec3 value, FpVec3 min, FpVec3 max) => new(
        Fp.Clamp(value.X, min.X, max.X),
        Fp.Clamp(value.Y, min.Y, max.Y),
        Fp.Clamp(value.Z, min.Z, max.Z)
    );
}
