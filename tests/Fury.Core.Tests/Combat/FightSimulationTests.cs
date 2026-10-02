using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Combat;

public class FightSimulationTests
{
    private static FightSimulation CreateSim() => new(GlenData.Create(), KiraData.Create());

    /// <summary>Walks P1 toward P2 until the gap is short enough for a light normal, then lets one neutral frame pass.</summary>
    private static void WalkIntoRange(FightSimulation sim)
    {
        var walk = new InputState((ushort)InputButton.Right);
        long closeEnough = Fp.FromRatio(5, 2).Raw;
        for (int f = 0; f < 240 && (sim.State.GetX(1) - sim.State.GetX(0)).Raw > closeEnough; f++)
        {
            sim.Step(walk, new InputState(0));
        }
        sim.Step(new InputState(0), new InputState(0));
    }

    private static int AttackUntilHit(FightSimulation sim, InputButton button, int maxFrames)
    {
        int before = sim.State.GetHealth(1);
        var attack = new InputState((ushort)button);
        for (int f = 0; f < maxFrames && sim.State.GetHealth(1) == before; f++)
        {
            sim.Step(f == 0 ? attack : new InputState(0), new InputState(0));
        }
        return before - sim.State.GetHealth(1);
    }

    [Fact]
    public void Step_AdvancesFrameDeterministically()
    {
        var sim = CreateSim();
        var neutral = new InputState(0);
        for (int i = 0; i < 120; i++) sim.Step(neutral, neutral);
        Assert.Equal(120, sim.Frame);
    }

    [Fact]
    public void IdenticalInputSequences_ProduceIdenticalHashes()
    {
        static ulong Run(int seed)
        {
            var sim = CreateSim();
            var rng = new Xoshiro256StarStar((ulong)seed);
            for (int f = 0; f < 300; f++)
            {
                sim.Step(new InputState((ushort)rng.NextULong()), new InputState((ushort)rng.NextULong()));
            }
            return sim.SnapshotHash;
        }

        Assert.Equal(Run(7), Run(7));
        Assert.NotEqual(Run(7), Run(8));
    }

    [Fact]
    public void ArenaSize_DoesNotAffectDeterminism()
    {
        static ulong Run(int arenaBytes)
        {
            var sim = new FightSimulation(GlenData.Create(), KiraData.Create(), arenaCapacityBytes: arenaBytes);
            var a = new Xoshiro256StarStar(99);
            for (int f = 0; f < 200; f++)
            {
                sim.Step(new InputState((ushort)a.NextULong()), new InputState((ushort)a.NextULong()));
            }
            return sim.SnapshotHash;
        }

        Assert.Equal(Run(16 * 1024), Run(1024 * 1024));
    }

    [Fact]
    public void ZeroHeapAllocations_DuringSteps()
    {
        var sim = CreateSim();
        var neutral = new InputState(0);
        for (int i = 0; i < 10; i++) sim.Step(neutral, neutral);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) sim.Step(neutral, neutral);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Restore_ReproducesIdenticalFuture()
    {
        var sim = CreateSim();
        var stream = new Xoshiro256StarStar(5);
        var inputs = new InputState[400];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new InputState((ushort)stream.NextULong());
        }

        for (int f = 0; f < 100; f++) sim.Step(inputs[2 * f], inputs[2 * f + 1]);
        var saved = sim.SnapshotSpan.ToArray();
        var hashAt100 = sim.SnapshotHash;

        for (int f = 100; f < 200; f++) sim.Step(inputs[2 * f], inputs[2 * f + 1]);
        var hashAt200 = sim.SnapshotHash;

        sim.Restore(saved);
        Assert.Equal(hashAt100, sim.SnapshotHash);

        for (int f = 100; f < 200; f++) sim.Step(inputs[2 * f], inputs[2 * f + 1]);
        Assert.Equal(hashAt200, sim.SnapshotHash);
    }

    [Fact]
    public void RoundTimer_TimeoutPicksHealthierWinner()
    {
        var cfg = FightConfig.Default() with { RoundSeconds = 1 };
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create(), cfg);
        var neutral = new InputState(0);

        sim.State.SetHealth(0, 500);
        for (int f = 0; f < 61 && sim.State.RoundState == 0; f++) sim.Step(neutral, neutral);

        Assert.Equal(1, sim.State.RoundState);
        Assert.Equal(1, sim.State.Winner);
    }

    [Fact]
    public void AttackInRange_DamagesOpponent()
    {
        var sim = CreateSim();
        WalkIntoRange(sim);
        int dropped = AttackUntilHit(sim, InputButton.Heavy, 30);

        Assert.True(dropped > 0);
    }

    [Fact]
    public void BlockHeldBack_ReducesDamageToChip()
    {
        var sim = CreateSim();
        WalkIntoRange(sim);
        var cfg = FightConfig.Default();
        int chip = 110 * cfg.ChipDamagePercent / 100;

        int openHealth = sim.State.GetHealth(1);
        var attack = new InputState((ushort)InputButton.Heavy);
        var block = new InputState((ushort)InputButton.Right);
        for (int f = 0; f < 30; f++)
        {
            sim.Step(f == 0 ? attack : new InputState(0), block);
        }
        int dropped = openHealth - sim.State.GetHealth(1);

        Assert.True(dropped > 0);
        Assert.True(dropped <= chip);
    }

    [Fact]
    public void LandingHits_GrantsExMeter()
    {
        var sim = CreateSim();
        WalkIntoRange(sim);
        Assert.Equal(0, sim.State.GetExMeter(0));

        int dropped = AttackUntilHit(sim, InputButton.Light, 30);
        Assert.True(dropped > 0);
        Assert.True(sim.State.GetExMeter(0) > 0);
    }

    [Fact]
    public void LowHealth_UnlocksRage()
    {
        var sim = CreateSim();
        WalkIntoRange(sim);
        Assert.False(sim.State.IsRaging(0));
        sim.State.SetHealth(0, 100);

        AttackUntilHit(sim, InputButton.Heavy, 30);

        Assert.True(sim.State.IsRaging(0));
    }

    [Fact]
    public void DepletingHealth_EndsRoundWithWinner()
    {
        var sim = CreateSim();
        WalkIntoRange(sim);
        sim.State.SetHealth(1, 1);

        for (int f = 0; f < 40 && sim.State.RoundState == 0; f++)
        {
            sim.Step(f == 0 ? new InputState((ushort)InputButton.Light) : new InputState(0), new InputState(0));
        }

        Assert.Equal(1, sim.State.RoundState);
        Assert.Equal(0, sim.State.Winner);
        Assert.Equal(FighterStateId.Ko, sim.State.GetStateId(1));
    }

    [Fact]
    public void CpuBrain_IsDeterministicPerSeed()
    {
        static ulong Run(ulong seed)
        {
            var sim = CreateSim();
            var brain = new CpuBrain(seed, aggression: 8);
            for (int f = 0; f < 600; f++) sim.StepWithProviders(brain, new DummyBrain());
            return sim.SnapshotHash;
        }

        Assert.Equal(Run(42), Run(42));
        Assert.NotEqual(Run(42), Run(43));
    }

    [Fact]
    public void CpuBlindMatch_ProducesHitEventsAndCompletes()
    {
        var sim = CreateSim();
        var b1 = new CpuBrain(11, 10);
        var b2 = new CpuBrain(22, 10);

        var final = sim.SnapshotHash;
        for (int f = 0; f < 60 * 30; f++)
        {
            sim.StepWithProviders(b1, b2);
            final = sim.SnapshotHash;
        }

        Assert.Equal(60 * 30, sim.Frame);
        _ = final;
        Assert.True(sim.State.GetHealth(0) < 1000 || sim.State.GetHealth(1) < 1000);
    }
}
