using Fury.Core.Data;
using Fury.Core.Observability;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Combat;

public class CpuBrainBehaviourTests
{
    private sealed class EventCounter : IFightTracer
    {
        public readonly Dictionary<int, int> Events = [];
        public readonly HashSet<int> Moves = [];

        public void OnFrame(in FrameTrace trace)
        {
        }

        public void OnSimEvent(int frame, int eventId, ulong payload)
        {
            Events[eventId] = Events.GetValueOrDefault(eventId) + 1;
            if (eventId is SimEventId.MoveStarted or SimEventId.Cancel)
            {
                SimEventPayload.Unpack(payload, out _, out int move, out _);
                Moves.Add(move);
            }
        }
    }

    [Fact]
    public void BlindMatches_ExerciseTheWholeMoveSet_AndKeepFighting()
    {
        var counter = new EventCounter();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var sim = new FightSimulation(GlenData.Create(), KiraData.Create(), tracer: counter);
            var a = new CpuBrain(seed * 11, aggression: 8);
            var b = new CpuBrain((seed * 11) + 1, aggression: 8);
            for (int f = 0; f < 60 * 25 && sim.State.RoundState == 0; f++) sim.StepWithProviders(a, b);
        }

        Assert.True(counter.Events.GetValueOrDefault(SimEventId.AttackHit) > 20, "the brains must actually land hits");
        Assert.True(counter.Events.GetValueOrDefault(SimEventId.ProjectileSpawned) > 0, "fireballs are thrown");
        Assert.True(counter.Events.GetValueOrDefault(SimEventId.Throw) > 0, "throws are attempted");
        Assert.True(counter.Events.GetValueOrDefault(SimEventId.Dash) > 0, "dashes are used");
        Assert.True(counter.Events.GetValueOrDefault(SimEventId.AttackBlocked) > 0, "blocking happens");
        Assert.True(counter.Moves.Count >= 8, $"only {counter.Moves.Count} distinct moves were used");
    }

    [Fact]
    public void BothSidesFight_NeitherPlayerWalksAwayForever()
    {
        // Regression: P2 used to walk away from P1 (inverted walk), so matches were one-sided.
        int closeFrames = 0;
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var a = new CpuBrain(3, 8);
        var b = new CpuBrain(4, 8);
        for (int f = 0; f < 60 * 20 && sim.State.RoundState == 0; f++)
        {
            sim.StepWithProviders(a, b);
            if (System.Math.Abs((sim.State.GetX(1) - sim.State.GetX(0)).ToFloat()) < 4f) closeFrames++;
        }
        Assert.True(closeFrames > 200, $"fighters were close for only {closeFrames} frames");
    }
}
