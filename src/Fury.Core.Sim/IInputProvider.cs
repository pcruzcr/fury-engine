namespace Fury.Core.Sim;

/// <summary>
/// Bit-packed per-frame input for one fighter: 4 directions, 4 attack buttons, block/parry and 4 macro buttons.
/// Equality is by value; it snapshots into replays and network packets verbatim.
/// </summary>
public readonly record struct InputState(ushort Bits);

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

    /// <summary>Activates the fighter power-up (arcana / transformation) when a module provides one.</summary>
    Macro3 = 1 << 12,

    /// <summary>Tags in the partner when a tag-team module is active.</summary>
    Macro4 = 1 << 13,
}

/// <summary>Rich read-only projection of the fight exposed to input providers (brains, netcode).</summary>
public readonly record struct FightView(
    int MyStateId,
    int OpponentStateId,
    int OpponentMoveIndex,
    int DistanceRaw,
    int MyFacing)
{
    /// <summary>EX meter points of this fighter (lets a brain decide when a super is available).</summary>
    public int MyExMeter { get; init; }

    /// <summary>True while the opponent is airborne (jumping or launched).</summary>
    public bool OpponentAirborne { get; init; }

    /// <summary>True while the opponent has a projectile in flight.</summary>
    public bool OpponentFireballLive { get; init; }
}

/// <summary>
/// Port: anything that can produce inputs for the sim — keyboard, pad, netcode, CPU brain,
/// replay script. The sim never knows which adapter is feeding it.
/// </summary>
public interface IInputProvider
{
    /// <summary>Inputs for the given frame. Must be deterministic given (frame, fight state).</summary>
    InputState Poll(int frame, in FightView view);
}
