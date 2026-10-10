// Fighter.Core — normalized input (§16). Hardware-agnostic: adapters produce InputState.
namespace Fighter.Core;

/// <summary>Attack buttons. Bitmask so combined presses (e.g. LP+LK tech) are representable.</summary>
[Flags]
public enum Buttons : byte
{
    /// <summary>No buttons.</summary>
    None = 0,
    /// <summary>Light punch.</summary>
    LP = 1 << 0,
    /// <summary>Medium punch.</summary>
    MP = 1 << 1,
    /// <summary>Heavy punch.</summary>
    HP = 1 << 2,
    /// <summary>Light kick.</summary>
    LK = 1 << 3,
    /// <summary>Medium kick.</summary>
    MK = 1 << 4,
    /// <summary>Heavy kick.</summary>
    HK = 1 << 5,
}

/// <summary>Normalized input for one fighter on one frame. Integers only; no device concepts.</summary>
public readonly struct InputState : IEquatable<InputState>
{
    /// <summary>Pressed button bitmask.</summary>
    public readonly Buttons Buttons;
    /// <summary>Horizontal axis: -1 back/downstream-resolved later, 0, +1. Raw sign only.</summary>
    public readonly int DirX;
    /// <summary>Vertical axis: -1 down, 0, +1 up.</summary>
    public readonly int DirY;
    /// <summary>Simulation frame this input belongs to (motion windows use deltas).</summary>
    public readonly int Frame;

    /// <summary>Constructs a normalized input (axes restricted to −1..1).</summary>
    public InputState(Buttons buttons, int dirX, int dirY, int frame)
    {
        if (dirX is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(dirX));
        if (dirY is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(dirY));
        Buttons = buttons;
        DirX = dirX;
        DirY = dirY;
        Frame = frame;
    }

    /// <summary>Pure normalizer used by hardware adapters and tests.</summary>
    public static InputState FromBooleans(bool lp, bool mp, bool hp, bool lk, bool mk, bool hk,
        int dirX, int dirY, int frame)
    {
        Buttons b = Buttons.None;
        if (lp) b |= Buttons.LP;
        if (mp) b |= Buttons.MP;
        if (hp) b |= Buttons.HP;
        if (lk) b |= Buttons.LK;
        if (mk) b |= Buttons.MK;
        if (hk) b |= Buttons.HK;
        return new InputState(b, dirX, dirY, frame);
    }

    /// <summary>Full structural equality (buttons, axes, frame).</summary>
    public bool Equals(InputState other) =>
        Buttons == other.Buttons && DirX == other.DirX && DirY == other.DirY && Frame == other.Frame;
    /// <summary>Type-checked equality.</summary>
    public override bool Equals(object? obj) => obj is InputState s && Equals(s);
    /// <summary>Combined hash.</summary>
    public override int GetHashCode() => HashCode.Combine((byte)Buttons, DirX, DirY, Frame);
    /// <summary>Full structural equality.</summary>
    public static bool operator ==(InputState a, InputState b) => a.Equals(b);
    /// <summary>Full structural inequality.</summary>
    public static bool operator !=(InputState a, InputState b) => !a.Equals(b);
}
