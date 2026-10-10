// Fighter.Core — training dummy brains (presenter training aids, deterministic).
// Pure functions of (frame, sim state) like CpuBrain. Excluded from verified replay
// paths (auto-test uses CpuBrain); training determinism is best-effort by construction.
namespace Fighter.Core;

/// <summary>Dummy modes for training (cycle with C in the demo).</summary>
public enum DummyMode : byte
{
    /// <summary>Stand still (neutral always).</summary>
    Stand = 0,
    /// <summary>Hold away (high block).</summary>
    Block = 1,
    /// <summary>Hop periodically.</summary>
    Jump = 2,
    /// <summary>Full CPU (<see cref="CpuBrain"/>).</summary>
    Cpu = 3,
}

/// <summary>Deterministic training-dummy inputs for fighter index 1.</summary>
public static class DummyBrain
{
    /// <summary>Input for the dummy on <paramref name="frame"/>.</summary>
    public static InputState For(FightSimulation sim, int frame, DummyMode mode)
    {
        var me = sim.Fighters[1];
        InputState N(Buttons b, int dx, int dy) => new(b, dx, dy, frame);
        return mode switch
        {
            DummyMode.Stand => N(Buttons.None, 0, 0),
            DummyMode.Block => N(Buttons.None, -me.Facing, 0),
            DummyMode.Jump => N(Buttons.None, 0, frame % 90 == 0 ? 1 : 0),
            _ => CpuBrain.ForP2(sim, frame),
        };
    }
}
