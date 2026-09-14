namespace Fury.Core.Math;

/// <summary>
/// 2D vector over <see cref="Fp"/>. The only spatial primitive of the simulation;
/// millipixels, pixels-as-int and float vectors are all forbidden in sim code.
/// </summary>
public readonly struct FpVec2 : IEquatable<FpVec2>
{
    public static readonly FpVec2 Zero = new(Fp.Zero, Fp.Zero);

    public Fp X { get; }
    public Fp Y { get; }

    public FpVec2(Fp x, Fp y)
    {
        X = x;
        Y = y;
    }

    public static FpVec2 operator +(FpVec2 a, FpVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FpVec2 operator -(FpVec2 a, FpVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FpVec2 operator -(FpVec2 a) => new(-a.X, -a.Y);
    public static FpVec2 operator *(FpVec2 v, Fp s) => new(v.X * s, v.Y * s);
    public static FpVec2 operator *(Fp s, FpVec2 v) => v * s;
    public static FpVec2 operator /(FpVec2 v, Fp s) => new(v.X / s, v.Y / s);

    public static bool operator ==(FpVec2 a, FpVec2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(FpVec2 a, FpVec2 b) => !(a == b);

    public Fp Dot(FpVec2 o) => X * o.X + Y * o.Y;
    public FpVec2 Perp() => new(-Y, X);

    /// <summary>Manhattan length: cheap, exact, overflow-free — the default for gameplay distances.</summary>
    public Fp ManhattanLen() => Fp.Abs(X) + Fp.Abs(Y);

    /// <summary>Euclidean length via Fp.Sqrt. Use for display/camera only unless required by design.</summary>
    public Fp Length() => Fp.Sqrt(Dot(this));

    public static FpVec2 Min(FpVec2 a, FpVec2 b) => new(Fp.Min(a.X, b.X), Fp.Min(a.Y, b.Y));
    public static FpVec2 Max(FpVec2 a, FpVec2 b) => new(Fp.Max(a.X, b.X), Fp.Max(a.Y, b.Y));

    public bool Equals(FpVec2 other) => this == other;
    public override bool Equals(object? obj) => obj is FpVec2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);
    public override string ToString() => $"({X}, {Y})";
}
