// Fighter.Core — AABB body collision (ADR-002, §20). Integer only. No Godot physics.
namespace Fighter.Core;

/// <summary>Axis-aligned box in millipixels. X centered on fighter, Y = feet.</summary>
public readonly struct Aabb
{
    /// <summary>Center X (millipixels).</summary>
    public readonly int X;
    /// <summary>Feet Y (millipixels, up-negative).</summary>
    public readonly int Y;
    /// <summary>Width (millipixels).</summary>
    public readonly int W;
    /// <summary>Height (millipixels).</summary>
    public readonly int H;
    /// <summary>Constructs from center-X, feet-Y, size.</summary>
    public Aabb(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

    /// <summary>Left edge.</summary>
    public int Left => X - W / 2;
    /// <summary>Right edge.</summary>
    public int Right => X + W / 2;
    /// <summary>Top edge.</summary>
    public int Top => Y - H;
    /// <summary>Bottom edge (feet).</summary>
    public int Bottom => Y;

    /// <summary>Strict overlap test (touching edges do not overlap).</summary>
    public bool Overlaps(Aabb o) => Left < o.Right && Right > o.Left && Top < o.Bottom && Bottom > o.Top;
}

/// <summary>Body collision helpers: boxes, separation, stage clamp. Integer only.</summary>
public static class BodyCollision
{
    /// <summary>Fighter body box from feet position.</summary>
    public static Aabb BodyOf(Vector2I pos) =>
        new(pos.X, pos.Y, Calibration.FighterHalfWidth * 2, Calibration.FighterHeight);

    /// <summary>Attack hitbox: body expanded forward by level reach [CAL].</summary>
    public static Aabb HitboxOf(Vector2I pos, int facing, AttackLevel level)
    {
        int reach = level switch
        {
            AttackLevel.Light => 40000,
            AttackLevel.Medium => 50000,
            AttackLevel.Heavy => 60000,
            AttackLevel.Special => 70000,
            AttackLevel.Super => 80000,
            _ => 0,
        };
        int cx = pos.X + facing * reach / 2;
        return new Aabb(cx, pos.Y, Calibration.FighterHalfWidth * 2 + reach, Calibration.FighterHeight);
    }

    /// <summary>Prevents body crossing: pushes both apart on X, equally. Returns corrected positions.</summary>
    public static void Separate(ref Vector2I a, ref Vector2I b)
    {
        var ba = BodyOf(a);
        var bb = BodyOf(b);
        if (!ba.Overlaps(bb)) return;
        int overlap = (ba.W / 2 + bb.W / 2) - System.Math.Abs(a.X - b.X);
        if (overlap <= 0) return;
        int push = overlap / 2 + 1;
        if (a.X <= b.X) { a = new Vector2I(a.X - push, a.Y); b = new Vector2I(b.X + push, b.Y); }
        else { a = new Vector2I(a.X + push, a.Y); b = new Vector2I(b.X - push, b.Y); }
    }

    /// <summary>Clamps feet inside stage bounds and above ground (Y never below ground).</summary>
    public static Vector2I ClampStage(Vector2I p)
    {
        int x = p.X < Calibration.StageMinX ? Calibration.StageMinX
            : p.X > Calibration.StageMaxX ? Calibration.StageMaxX : p.X;
        int y = p.Y > Calibration.GroundY ? Calibration.GroundY : p.Y; // Y up-negative; never below ground
        return new Vector2I(x, y);
    }
}
