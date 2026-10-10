// §54 Test 1/4: 10,000-frame determinism + repeated execution.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class DeterminismTests
{
    private const ulong Seed = 0x1234_5678_9ABC_DEF0UL;
    private const int Frames = 10_000;

    private static ulong Run(ulong seed, int frames)
    {
        var sim = new DeterministicSim(seed);
        for (int f = 0; f < frames; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));
        return sim.Capture().Hash;
    }

    [Fact]
    public void SameSeedSameInputs_SameFinalHash_10kFrames()
    {
        ulong a = Run(Seed, Frames);
        ulong b = Run(Seed, Frames);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RepeatedExecution_ThreeRuns_IdenticalHash()
    {
        ulong a = Run(Seed, Frames);
        ulong b = Run(Seed, Frames);
        ulong c = Run(Seed, Frames);
        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }

    [Fact]
    public void DifferentSeed_DifferentHash()
    {
        Assert.NotEqual(Run(Seed, Frames), Run(Seed ^ 1UL, Frames));
    }

    [Fact]
    public void PerFrameHashStream_IsReproducible()
    {
        var s1 = new DeterministicSim(Seed);
        var s2 = new DeterministicSim(Seed);
        for (int f = 0; f < 500; f++)
        {
            s1.Step(InputGen.P1(f), InputGen.P2(f));
            s2.Step(InputGen.P1(f), InputGen.P2(f));
            Assert.Equal(s1.Capture().Hash, s2.Capture().Hash);
        }
    }
}
