// §54 Test 2: snapshot → restore → resimulate == uninterrupted run.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class SnapshotTests
{
    private const ulong Seed = 0x0BAD_F00D_DEAD_BEEFUL;

    [Fact]
    public void RestoreAndResimulate_MatchesUninterruptedRun()
    {
        const int at = 300, extra = 700;

        var uninterrupted = new DeterministicSim(Seed);
        for (int f = 0; f < at + extra; f++)
            uninterrupted.Step(InputGen.P1(f), InputGen.P2(f));
        ulong expected = uninterrupted.Capture().Hash;

        var branched = new DeterministicSim(Seed);
        for (int f = 0; f < at; f++)
            branched.Step(InputGen.P1(f), InputGen.P2(f));
        StateSnapshot snap = branched.Capture();

        var restored = new DeterministicSim(0); // different seed proves restore overwrites
        restored.Restore(snap);
        Assert.Equal(snap.Hash, restored.Capture().Hash);
        for (int f = at; f < at + extra; f++)
            restored.Step(InputGen.P1(f), InputGen.P2(f));

        Assert.Equal(expected, restored.Capture().Hash);
    }

    [Fact]
    public void CaptureTwice_SameBytes()
    {
        var sim = new DeterministicSim(Seed);
        for (int f = 0; f < 50; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));
        Assert.Equal(sim.Capture().Payload, sim.Capture().Payload);
    }

    [Fact]
    public void Restore_PreservesPositionsAndTimers()
    {
        var sim = new DeterministicSim(Seed);
        for (int f = 0; f < 40; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));
        sim.SetTimer(1, 77);
        var snap = sim.Capture();

        var other = new DeterministicSim(1);
        other.Restore(snap);
        Assert.Equal(sim.P1Pos, other.P1Pos);
        Assert.Equal(sim.P2Pos, other.P2Pos);
        Assert.Equal(77, other.P1Timer);
        Assert.Equal(sim.RngState, other.RngState);
        Assert.Equal(sim.Frame, other.Frame);
    }
}
