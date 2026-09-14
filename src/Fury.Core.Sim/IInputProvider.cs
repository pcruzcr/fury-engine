namespace Fury.Core.Sim;

/// <summary>
/// Bit-packed per-frame input for one fighter. 16 bits: 6 attack buttons + directions.
/// Equality is by value; it snapshots into replays and network packets verbatim.
/// </summary>
public readonly record struct InputState(ushort Bits)
{
    public bool IsSet(InputButton button) => (Bits & (ushort)button) != 0;
}

[Flags]
public enum InputButton : ushort
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,
    Down = 1 << 3,
    Light = 1 << 4,
    Medium = 1 << 5,
    Heavy = 1 << 6,
    Special = 1 << 7,
    Block = 1 << 8,
    Parry = 1 << 9,
    Macro1 = 1 << 10,
    Macro2 = 1 << 11,
}

/// <summary>Rich read-only projection of the fight exposed to input providers (brains, netcode).</summary>
public readonly record struct FightView(
    int Frame,
    int MyHealth,
    int OpponentHealth,
    int MyStateId,
    int OpponentStateId,
    int OpponentMoveIndex,
    int DistanceRaw,
    int MyFacing);

/// <summary>
/// Port: anything that can produce inputs for the sim — keyboard, pad, netcode, CPU brain,
/// replay script. The sim never knows which adapter is feeding it.
/// </summary>
public interface IInputProvider
{
    /// <summary>Inputs for the given frame. Must be deterministic given (frame, fight state).</summary>
    InputState Poll(int frame, in FightView view);
}
