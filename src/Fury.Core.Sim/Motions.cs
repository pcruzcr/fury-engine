namespace Fury.Core.Sim;

/// <summary>Directional motion inputs, in numpad notation relative to the fighter's facing (6 = forward).</summary>
public enum MotionId : byte
{
    None = 0,

    /// <summary>Quarter circle forward: 236.</summary>
    QuarterCircleForward = 1,

    /// <summary>Quarter circle back: 214.</summary>
    QuarterCircleBack = 2,

    /// <summary>Dragon punch: 623.</summary>
    DragonPunch = 3,

    /// <summary>Reverse dragon punch: 421.</summary>
    ReverseDragonPunch = 4,

    /// <summary>Half circle forward: 41236.</summary>
    HalfCircleForward = 5,

    /// <summary>Half circle back: 63214.</summary>
    HalfCircleBack = 6,

    /// <summary>Charge back, then forward: [4]6.</summary>
    ChargeBackForward = 7,

    /// <summary>Charge down, then up: [2]8.</summary>
    ChargeDownUp = 8,

    /// <summary>Double tap forward: 656 (dash).</summary>
    DoubleTapForward = 9,

    /// <summary>Double tap back: 454 (backdash).</summary>
    DoubleTapBack = 10,

    /// <summary>Full circle: 63214789.</summary>
    FullCircle = 11,

    /// <summary>Forward held when the button is pressed (command normals such as 6+heavy).</summary>
    HoldForward = 12,

    /// <summary>Back held when the button is pressed (back + attack command normals).</summary>
    HoldBack = 13,
}

/// <summary>Stance a move needs to be triggered.</summary>
public enum MoveStance : byte
{
    /// <summary>Any grounded stance (standing or crouching); airborne only when the move is flagged Air.</summary>
    Any = 0,

    /// <summary>Standing: Down not held.</summary>
    Stand = 1,

    /// <summary>Crouching: Down held.</summary>
    Crouch = 2,

    /// <summary>Airborne.</summary>
    Air = 3,
}

/// <summary>
/// Packing of <c>MoveEntry.Command</c>: button mask (bits 0-15, same bits as <see cref="InputButton"/>, ANY of them
/// triggers), motion (bits 16-23) and stance (bits 24-25). 0 means "not directly triggerable".
/// </summary>
public static class MoveCommand
{
    public static int Pack(InputButton buttons, MotionId motion = MotionId.None, MoveStance stance = MoveStance.Any) =>
        (int)(ushort)buttons | ((int)motion << 16) | ((int)stance << 24);

    public static InputButton Buttons(int command) => (InputButton)(ushort)(command & 0xFFFF);

    public static MotionId Motion(int command) => (MotionId)((command >> 16) & 0xFF);

    public static MoveStance Stance(int command) => (MoveStance)((command >> 24) & 0x3);
}

/// <summary>
/// Recognizes motion inputs from an <see cref="InputBuffer"/>. Directions are matched in numpad notation
/// relative to facing; unrelated frames between the required directions are tolerated inside the time window
/// (the lenient behaviour players expect from modern fighters). Pure, allocation-free and deterministic.
/// </summary>
public static class MotionRecognizer
{
    /// <summary>Frames a charge direction must be held before the release counts.</summary>
    public const int ChargeFrames = 24;

    private static readonly byte[] Qcf = [2, 3, 6];
    private static readonly byte[] Qcb = [2, 1, 4];
    private static readonly byte[] Dp = [6, 2, 3];
    private static readonly byte[] Rdp = [4, 2, 1];
    private static readonly byte[] Hcf = [4, 1, 2, 3, 6];
    private static readonly byte[] Hcb = [6, 3, 2, 1, 4];
    private static readonly byte[] Circle = [6, 3, 2, 1, 4, 7, 8, 9];
    private static readonly byte[] DoubleForward = [6, 5, 6];
    private static readonly byte[] DoubleBack = [4, 5, 4];

    /// <summary>Numpad direction of one frame relative to <paramref name="facing"/> (+1 = facing right).</summary>
    public static int Numpad(ushort bits, int facing)
    {
        bool right = (bits & (ushort)InputButton.Right) != 0;
        bool left = (bits & (ushort)InputButton.Left) != 0;
        bool forward = facing >= 0 ? right : left;
        bool back = facing >= 0 ? left : right;
        int x = forward ? 1 : back ? -1 : 0;
        int y = (bits & (ushort)InputButton.Up) != 0 ? 1 : (bits & (ushort)InputButton.Down) != 0 ? -1 : 0;
        return 5 + x + (3 * y);
    }

    /// <summary>True when the motion was completed inside the last <paramref name="window"/> frames.</summary>
    public static bool Matches(MotionId motion, InputBuffer input, int facing, int window = 18) => motion switch
    {
        MotionId.None => true,
        MotionId.QuarterCircleForward => Sequence(Qcf, input, facing, window),
        MotionId.QuarterCircleBack => Sequence(Qcb, input, facing, window),
        MotionId.DragonPunch => Sequence(Dp, input, facing, window),
        MotionId.ReverseDragonPunch => Sequence(Rdp, input, facing, window),
        MotionId.HalfCircleForward => Sequence(Hcf, input, facing, System.Math.Max(window, 24)),
        MotionId.HalfCircleBack => Sequence(Hcb, input, facing, System.Math.Max(window, 24)),
        MotionId.FullCircle => Sequence(Circle, input, facing, InputBuffer.History),
        MotionId.DoubleTapForward => Sequence(DoubleForward, input, facing, 12),
        MotionId.DoubleTapBack => Sequence(DoubleBack, input, facing, 12),
        MotionId.ChargeBackForward => Charge(input, facing, back: true, release: true),
        MotionId.ChargeDownUp => Charge(input, facing, back: false, release: false),
        MotionId.HoldForward => Numpad(input.Bits(0), facing) is 6 or 3 or 9,
        MotionId.HoldBack => Numpad(input.Bits(0), facing) is 4 or 1 or 7,
        _ => false,
    };

    /// <summary>
    /// Does a frame direction satisfy a required direction? A required cardinal accepts the diagonals that contain it
    /// (2 accepts 1, 2, 3); a required diagonal and neutral must match exactly.
    /// </summary>
    private static bool Satisfies(int required, int actual)
    {
        if (required == actual) return true;
        return required switch
        {
            2 => actual is 1 or 3,
            4 => actual is 1 or 7,
            6 => actual is 3 or 9,
            8 => actual is 7 or 9,
            _ => false,
        };
    }

    private static bool Sequence(ReadOnlySpan<byte> sequence, InputBuffer input, int facing, int window)
    {
        int index = sequence.Length - 1;
        int limit = System.Math.Min(window, InputBuffer.History);
        for (int ago = 0; ago < limit; ago++)
        {
            int actual = Numpad(input.Bits(ago), facing);
            if (!Satisfies(sequence[index], actual)) continue;
            if (--index < 0) return true;
        }
        return false;
    }

    private static bool Charge(InputBuffer input, int facing, bool back, bool release)
    {
        // Release direction pressed within the last few frames...
        int releaseAge = -1;
        for (int ago = 0; ago < 6; ago++)
        {
            int d = Numpad(input.Bits(ago), facing);
            bool released = release ? d is 6 or 3 or 9 : d is 8 or 7 or 9;
            if (released)
            {
                releaseAge = ago;
                break;
            }
        }
        if (releaseAge < 0) return false;

        // ...preceded by ChargeFrames of the opposite direction held (diagonals count).
        int held = 0;
        for (int ago = releaseAge + 1; ago < InputBuffer.History; ago++)
        {
            int d = Numpad(input.Bits(ago), facing);
            bool charging = back ? d is 4 or 1 or 7 : d is 2 or 1 or 3;
            if (charging) held++;
            else if (held > 0) break;
        }
        return held >= ChargeFrames;
    }
}
