// Fighter.Core — fighter states (GDD §4.4). Airborne is a FLAG, not a state:
// Jump/Hitstun/Blockstun can all occur airborne (juggle system, ADR-002 §4).
namespace Fighter.Core;

/// <summary>Fighter states (GDD §4.4). Airborne is a FLAG, not a state.</summary>
public enum FighterState : byte
{
    /// <summary>Idle, actionable.</summary>
    Neutral = 0,
    /// <summary>Walking, actionable.</summary>
    Walk = 1,
    /// <summary>Airborne transit (implies <see cref="Fighter.Airborne"/>).</summary>
    Jump = 2,
    /// <summary>Attack in progress (grounded or air).</summary>
    Attack = 3,
    /// <summary>Stunned by a hit (grounded or air — juggle).</summary>
    Hitstun = 4,
    /// <summary>Stunned by a block.</summary>
    Blockstun = 5,
    /// <summary>Down; unhittable until wakeup (no OTG in F1).</summary>
    Knockdown = 6,
    /// <summary>Getting up; vulnerable, 8f.</summary>
    Wakeup = 7,
    /// <summary>Backdash (invulnerable 1–6f).</summary>
    Backdash = 8,
    /// <summary>Parry attempt window (6f, 10f Dant rage).</summary>
    ParryStance = 9,
    /// <summary>Parry whiffed: punishable (hit during it deals ×1.2).</summary>
    ParryWhiff = 10,
    /// <summary>Clash rebound, 12f, no damage.</summary>
    ClashStun = 11,
    /// <summary>Parried attacker, 15f punish window for the defender.</summary>
    ParryStun = 12,
    /// <summary>Round-ending state.</summary>
    KO = 13,
}

/// <summary>State predicates shared by the pipeline.</summary>
public static class FighterStateMeta
{
    /// <summary>States in which fresh inputs (moves, parry, dash) are accepted.</summary>
    public static bool IsActionable(FighterState s) => s is FighterState.Neutral or FighterState.Walk;

    /// <summary>States in which blocking (hold away) is legal.</summary>
    public static bool CanBlock(FighterState s) =>
        s is FighterState.Neutral or FighterState.Walk or FighterState.Blockstun or FighterState.Jump;
}
