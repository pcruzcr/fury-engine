using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Certification;

/// <summary>
/// F5 gate (ADR-006): Headless certification harness. Generates blind AI-vs-AI combats
/// and verifies determinism by comparing state hashes across runs. Any deviation fails
/// the certification — the sim must be bit-exact across platforms and runs.
/// </summary>
public class HeadlessCertification
{
    [Fact]
    public void BlindMatch_SameSeed_ProducesIdenticalHashes()
    {
        var hashes1 = RunBlindMatch(seed: 42, aggression: 5);
        var hashes2 = RunBlindMatch(seed: 42, aggression: 5);

        Assert.Equal(hashes1, hashes2);
    }

    [Fact]
    public void BlindMatch_DifferentSeeds_ProduceDifferentHashes()
    {
        var hashes1 = RunBlindMatch(seed: 42, aggression: 5);
        var hashes2 = RunBlindMatch(seed: 43, aggression: 5);

        Assert.NotEqual(hashes1, hashes2);
    }

    [Fact]
    public void BlindMatch_ProducesCombat()
    {
        var hashes = RunBlindMatch(seed: 42, aggression: 8);

        Assert.Equal(600, hashes.Length);
        Assert.All(hashes, h => Assert.NotEqual(0UL, h));
    }

    [Fact]
    public void BlindMatch_DifferentAggression_ProducesDifferentHashes()
    {
        var hashes1 = RunBlindMatch(seed: 42, aggression: 2);
        var hashes2 = RunBlindMatch(seed: 42, aggression: 10);

        Assert.NotEqual(hashes1, hashes2);
    }

    private static ulong[] RunBlindMatch(ulong seed, int aggression)
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var brain1 = new CpuBrain(seed, aggression);
        var brain2 = new CpuBrain(seed + 1, aggression);

        var hashes = new ulong[600];
        for (int f = 0; f < 600; f++)
        {
            sim.StepWithProviders(brain1, brain2);
            hashes[f] = sim.SnapshotHash;
        }
        return hashes;
    }
}
