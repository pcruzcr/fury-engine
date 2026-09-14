namespace Fury.Core.Sim;

/// <summary>
/// Orchestrates one deterministic simulation tick through the fixed phase pipeline.
/// Phases run in strict order; each phase is a pure function of the previous state.
/// Pipeline (per ADR-101): Input → Buffer → Command → State → Movement → Collision → Resolution → Energy → Snapshot.
/// </summary>
public sealed class FightClock
{
    public const int TicksPerSecond = 60;

    private int _frame;

    public int Frame => _frame;

    /// <summary>Advance exactly one tick. One call == one _PhysicsProcess in the presentation layer.</summary>
    public void Tick(InputState p1, InputState p2)
    {
        // F0: pipeline scaffolding. Systems land in F1 (combat) with per-phase tests.
        _frame++;
    }
}
