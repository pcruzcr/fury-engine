using System.Diagnostics;
using Fury.Core.Data;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Performance;

/// <summary>
/// F6 gate: Performance budget tests. Verifies that the sim meets its 16.67 ms frame budget
/// and that GC allocations stay at zero after warmup.
/// </summary>
public class BudgetTests
{
    [Fact]
    public void Sim_FrameTime_UnderBudget()
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var neutral = new InputState(0);

        // Warmup
        for (int i = 0; i < 100; i++) sim.Step(neutral, neutral);

        // Measure
        var stopwatch = Stopwatch.StartNew();
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++) sim.Step(neutral, neutral);
        stopwatch.Stop();

        double avgMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 16.67, $"Average frame time {avgMs:F3} ms exceeds 16.67 ms budget");
    }

    [Fact]
    public void Sim_ZeroGcAllocations_AfterWarmup()
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var neutral = new InputState(0);

        // Warmup
        for (int i = 0; i < 100; i++) sim.Step(neutral, neutral);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) sim.Step(neutral, neutral);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Sim_Deterministic_ProducesSameHashes()
    {
        var sim1 = new FightSimulation(GlenData.Create(), KiraData.Create());
        var sim2 = new FightSimulation(GlenData.Create(), KiraData.Create());

        var rng = new Fury.Core.Math.Xoshiro256StarStar(42);

        for (int f = 0; f < 100; f++)
        {
            var input1 = new InputState((ushort)rng.NextULong());
            var input2 = new InputState((ushort)rng.NextULong());

            sim1.Step(input1, input2);
            sim2.Step(input1, input2);

            Assert.Equal(sim1.SnapshotHash, sim2.SnapshotHash);
        }
    }
}
