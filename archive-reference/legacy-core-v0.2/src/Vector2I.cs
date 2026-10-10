// Fighter.Core — integer 2D vector (F0). Millipixels, see SimulationUnits.
namespace Fighter.Core;

/// <summary>Integer 2D vector. Only integer ops. No floats.</summary>
public readonly struct Vector2I : IEquatable<Vector2I>
{
    /// <summary>Millipixels, X right (stage 0..960000).</summary>
    public readonly int X;
    /// <summary>Millipixels, Y up-negative (ground 0).</summary>
    public readonly int Y;
    /// <summary>Constructs from millipixel components.</summary>
    public Vector2I(int x, int y) { X = x; Y = y; }

    /// <summary>Origin.</summary>
    public static readonly Vector2I Zero = new(0, 0);

    /// <summary>Component-wise add.</summary>
    public static Vector2I operator +(Vector2I a, Vector2I b) => new(a.X + b.X, a.Y + b.Y);
    /// <summary>Component-wise subtract.</summary>
    public static Vector2I operator -(Vector2I a, Vector2I b) => new(a.X - b.X, a.Y - b.Y);
    /// <summary>Scalar multiply.</summary>
    public static Vector2I operator *(Vector2I v, int s) => new(v.X * s, v.Y * s);

    /// <summary>Component equality.</summary>
    public bool Equals(Vector2I other) => X == other.X && Y == other.Y;
    /// <summary>Type-checked equality.</summary>
    public override bool Equals(object? obj) => obj is Vector2I v && Equals(v);
    /// <summary>Combined hash.</summary>
    public override int GetHashCode() => HashCode.Combine(X, Y);
    /// <summary>Component equality.</summary>
    public static bool operator ==(Vector2I a, Vector2I b) => a.Equals(b);
    /// <summary>Component inequality.</summary>
    public static bool operator !=(Vector2I a, Vector2I b) => !a.Equals(b);
    /// <summary>Debug format (never parsed).</summary>
    public override string ToString() => $"({X},{Y})";
}
