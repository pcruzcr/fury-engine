using Fury.Core.Math;
using Fury.Core.Sim;

namespace Fury.Game.Tools;

/// <summary>Forces a fighter into a given state of the simulation, for the art tools (the baker and the viewer). Never used during a real fight.</summary>
public static class PoseSetup
{
    public static void Apply(FightSimulation sim, int fighter, int state, int stateFrame, bool crouch = false, bool air = false, int move = -1, Fp? vy = null)
    {
        var s = sim.State;
        s.SetStateId(fighter, state);
        s.SetStateFrame(fighter, stateFrame);
        s.SetMoveIndex(fighter, move);
        s.SetCrouching(fighter, crouch);
        s.SetAirborne(fighter, air);
        s.SetVy(fighter, vy ?? Fp.Zero);
    }
}
