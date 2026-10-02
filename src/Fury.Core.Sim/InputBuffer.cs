namespace Fury.Core.Sim;

/// <summary>
/// Per-fighter ring of the most recent inputs (<see cref="History"/> frames: long enough for half-circle
/// motions and charge moves). Edge detection, motion recognition and command buffering read the ring without
/// allocating. Opposite directions are cleaned on entry (SOCD): Left+Right cancel to neutral and Up wins over
/// Down, so keyboards and fight sticks behave identically and no motion can be faked by mashing both sides.
/// The ring is part of the savestate (see <c>FightState.SetInputRing</c>).
/// </summary>
public sealed class InputBuffer
{
    public const int History = 32;

    private readonly InputState[] _ring = new InputState[History];
    private int _head; // index of most recent

    /// <summary>Pushes the input for the current frame (after SOCD cleaning).</summary>
    public void Push(InputState input)
    {
        ushort bits = input.Bits;
        const ushort horizontal = (ushort)(InputButton.Left | InputButton.Right);
        if ((bits & horizontal) == horizontal) bits = (ushort)(bits & ~horizontal);
        if ((bits & (ushort)InputButton.Up) != 0) bits = (ushort)(bits & ~(ushort)InputButton.Down);

        _head = (_head + 1) % History;
        _ring[_head] = new InputState(bits);
    }

    /// <summary>Input from <paramref name="ago"/> frames ago (0 = current frame).</summary>
    public InputState Get(int ago)
    {
        if ((uint)ago >= History) throw new ArgumentOutOfRangeException(nameof(ago));
        return _ring[(((_head - ago) % History) + History) % History];
    }

    /// <summary>Bits of the input <paramref name="ago"/> frames ago; frames older than the history read as neutral.</summary>
    public ushort Bits(int ago) => (uint)ago >= History ? (ushort)0 : Get(ago).Bits;

    /// <summary>True when any bit of <paramref name="button"/> went down this frame (true edge).</summary>
    public bool Pressed(InputButton button) => EdgeAt(button, 0);

    /// <summary>True when any bit of <paramref name="button"/> is held this frame.</summary>
    public bool Held(InputButton button) => (Get(0).Bits & (ushort)button) != 0;

    /// <summary>True when the press edge of <paramref name="button"/> happened exactly <paramref name="ago"/> frames ago.</summary>
    public bool EdgeAt(InputButton button, int ago) =>
        (Bits(ago) & (ushort)button) != 0 && (Bits(ago + 1) & (ushort)button) == 0;

    /// <summary>
    /// Age (0 = this frame) of the most recent press edge of <paramref name="button"/> inside the last
    /// <paramref name="window"/> frames, or -1. This is the input buffer: a press made slightly before the fighter
    /// could act still counts.
    /// </summary>
    public int PressAge(InputButton button, int window)
    {
        int limit = System.Math.Min(window, History);
        for (int ago = 0; ago < limit; ago++)
        {
            if (EdgeAt(button, ago)) return ago;
        }
        return -1;
    }

    /// <summary>Consecutive frames, counting back from now, during which <paramref name="button"/> was held.</summary>
    public int HeldFrames(InputButton button)
    {
        int n = 0;
        while (n < History && (Bits(n) & (ushort)button) != 0) n++;
        return n;
    }

    /// <summary>Captures the ring (oldest to newest order preserved) for the savestate.</summary>
    public void CopyRing(Span<int> ringOut, out int head)
    {
        head = _head;
        for (int i = 0; i < History; i++)
        {
            ringOut[i] = _ring[(_head + 1 + i) % History].Bits; // oldest first
        }
    }

    /// <summary>Restores a captured ring exactly, including the head position.</summary>
    public void LoadRing(ReadOnlySpan<int> ring, int head)
    {
        _head = ((head % History) + History) % History;
        for (int i = 0; i < History; i++)
        {
            _ring[(_head + 1 + i) % History] = new InputState((ushort)ring[i]); // oldest first
        }
    }
}
