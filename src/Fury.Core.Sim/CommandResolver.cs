namespace Fury.Core.Sim;

/// <summary>Auditable simulation events (zero-allocation dispatch to the tracer).</summary>
public static class SimEventId
{
    public const int MoveStarted = 1;
    public const int AttackHit = 2;
    public const int AttackBlocked = 3;
    public const int FighterJumped = 4;
    public const int MeterGained = 5;
    public const int RageUnlocked = 6;
    public const int Knockdown = 7;
    public const int RoundOver = 8;
}

/// <summary>A single command resolution outcome for one fighter this frame.</summary>
public enum MoveIntent : byte
{
    None = 0,
    StandLight = 1,
    StandMedium = 2,
    StandHeavy = 3,
    CrouchLight = 4,
    CrouchHeavy = 5,
    Special = 6,
    Super = 7,
}

/// <summary>
/// Translates buffered inputs into move intents (phase 3 of the pipeline). Data-driven:
/// intents map to <see cref="MoveSlot"/> indexes; motion inputs route through direction
/// modifiers (crouches) and meter gates (special/super), never through floats or RNG.
/// </summary>
public static class CommandResolver
{
    public static MoveIntent Resolve(InputBuffer input, in FighterContext ctx)
    {
        bool crouching = input.Held(InputButton.Down);

        if (input.Pressed(InputButton.Macro2) && ctx.ExSegments >= 3)
        {
            return MoveIntent.Super;
        }
        if (input.Pressed(InputButton.Special))
        {
            return MoveIntent.Special;
        }
        if (input.Pressed(InputButton.Light))
        {
            return crouching ? MoveIntent.CrouchLight : MoveIntent.StandLight;
        }
        if (input.Pressed(InputButton.Medium))
        {
            return crouching ? MoveIntent.CrouchLight : MoveIntent.StandMedium;
        }
        if (input.Pressed(InputButton.Heavy))
        {
            return crouching ? MoveIntent.CrouchHeavy : MoveIntent.StandHeavy;
        }
        return MoveIntent.None;
    }

    public static int ToMoveSlot(MoveIntent intent) => intent switch
    {
        MoveIntent.StandLight => MoveSlot.StandLight,
        MoveIntent.StandMedium => MoveSlot.StandMedium,
        MoveIntent.StandHeavy => MoveSlot.StandHeavy,
        MoveIntent.CrouchLight => MoveSlot.CrouchLight,
        MoveIntent.CrouchHeavy => MoveSlot.CrouchHeavy,
        MoveIntent.Special => MoveSlot.Special,
        MoveIntent.Super => MoveSlot.Super,
        _ => -1,
    };
}

/// <summary>Read-only view of one fighter's combat context for command gating.</summary>
public readonly record struct FighterContext(int ExSegments);
