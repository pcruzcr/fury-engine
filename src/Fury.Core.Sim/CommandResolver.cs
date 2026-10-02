using Fury.Core.Data;

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
    public const int ProjectileSpawned = 9;
    public const int Throw = 10;
    public const int ThrowTeched = 11;
    public const int Dash = 12;
    public const int Cancel = 13;
    public const int FinisherStarted = 14;
    public const int FinisherPerformed = 15;
    public const int PowerActivated = 16;
    public const int Tagged = 17;
    public const int HoldStarted = 18;
    public const int HoldEnded = 19;
}

/// <summary>Read-only view of one fighter's situation for command gating.</summary>
/// <param name="Frame">Simulation frame being resolved (to tell fresh button presses from already-used ones).</param>
/// <param name="Facing">+1 facing right, -1 facing left.</param>
/// <param name="Stance">Stand / Crouch / Air.</param>
/// <param name="ExMeter">EX points available.</param>
/// <param name="InputConsumed">Presses made before this frame number were already used by a move.</param>
public readonly record struct FighterContext(int Frame, int Facing, MoveStance Stance, int ExMeter, int InputConsumed);

/// <summary>
/// Phase 3 of the pipeline: turns buffered inputs into a move index by scanning the character's frame-data
/// table. Fully data-driven: each move declares its trigger (buttons, motion, stance), meter cost and priority;
/// the resolver picks the matching move with the highest priority (ties go to the higher index). A press stays
/// valid for <see cref="BufferFrames"/> frames (input buffering) until a move consumes it. No floats, no RNG.
/// </summary>
public static class CommandResolver
{
    public const int NoMove = -1;

    /// <summary>How long a button press stays valid while the fighter is busy (about 67 ms).</summary>
    public const int BufferFrames = 4;

    /// <summary>Frames in which a motion input must have been completed before the button.</summary>
    public const int MotionWindow = 18;

    /// <summary>Returns the index of the move to start, or <see cref="NoMove"/>.</summary>
    public static int Resolve(InputBuffer input, CharacterConfig data, in FighterContext ctx)
    {
        int best = NoMove;
        int bestPriority = int.MinValue;
        var table = data.Moves;

        for (int i = 0; i < table.MoveCount; i++)
        {
            var move = table.GetMove(i);
            if (move.Command == 0 || move.MeterCost > ctx.ExMeter) continue;

            var stance = MoveCommand.Stance(move.Command);
            bool stanceOk = stance switch
            {
                MoveStance.Stand => ctx.Stance == MoveStance.Stand,
                MoveStance.Crouch => ctx.Stance == MoveStance.Crouch,
                MoveStance.Air => ctx.Stance == MoveStance.Air,
                _ => ctx.Stance != MoveStance.Air || move.Has(MoveTraits.Air),
            };
            if (!stanceOk) continue;

            var buttons = MoveCommand.Buttons(move.Command);
            var motion = MoveCommand.Motion(move.Command);
            if (buttons == InputButton.None) continue; // motion-only commands (dashes) live in the movement system

            int age = input.PressAge(buttons, BufferFrames);
            if (age < 0 || ctx.Frame - age < ctx.InputConsumed) continue;
            if (motion != MotionId.None && !MotionRecognizer.Matches(motion, input, ctx.Facing, MotionWindow)) continue;

            if (move.Priority >= bestPriority)
            {
                best = i;
                bestPriority = move.Priority;
            }
        }
        return best;
    }

    /// <summary>Stance of a fighter given the current input (Down held) and whether it is airborne.</summary>
    public static MoveStance StanceOf(bool airborne, bool downHeld) =>
        airborne ? MoveStance.Air : downHeld ? MoveStance.Crouch : MoveStance.Stand;
}
