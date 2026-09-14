namespace Fury.Core.Sim;

/// <summary>
/// Per-fighter ring of recent inputs (history 8). Edge detection and command resolution
/// read the ring without allocations. The ring is reconstructible: resimulation from a
/// restored snapshot replays the same input history by contract.
/// </summary>
public sealed class InputBuffer
{
    public const int History = 8;

    private readonly InputState[] _ring = new InputState[History];
    private int _head; // index of most recent

    /// <summary>Pushes the input for the current frame.</summary>
    public void Push(InputState input)
    {
        _head = (_head + 1) % History;
        _ring[_head] = input;
    }

    /// <summary>Input from <paramref name="ago"/> frames ago (0 = current frame).</summary>
    public InputState Get(int ago)
    {
        if ((uint)ago >= History) throw new ArgumentOutOfRangeException(nameof(ago));
        int index = ((_head - ago) % History + History) % History;
        return _ring[index];
    }

    /// <summary>Pressed this frame and not the previous one (true edge).</summary>
    public bool Pressed(InputButton button) => Held(button) && (Get(1).Bits & (ushort)button) == 0;

    /// <summary>Held this frame.</summary>
    public bool Held(InputButton button) => (Get(0).Bits & (ushort)button) != 0;
}
