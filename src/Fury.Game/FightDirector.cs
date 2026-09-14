using Fury.Core.Sim;
using Godot;

namespace Fury.Game;

/// <summary>
/// Presentation-side orchestrator: owns the FightClock, feeds it from platform input,
/// and dispatches state to the render/audio/vfx/hud subsystems. Draws nothing itself.
/// Replaces the old monolithic SimRunner: one Node, one responsibility — direction.
/// </summary>
public sealed partial class FightDirector : Node2D
{
    private FightClock _clock = new();
    private InputState _p1;
    private InputState _p2;

    public override void _PhysicsProcess(double delta)
    {
        _p1 = PollLocal(0);
        _p2 = PollLocal(1);
        _clock.Tick(_p1, _p2);
    }

    private static InputState PollLocal(int slot)
    {
        // F0: platform input adapter lands in F3 (Platform/). Placeholder returns neutral.
        return new InputState(0);
    }
}
