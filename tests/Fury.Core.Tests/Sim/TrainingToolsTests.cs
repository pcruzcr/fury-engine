using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>Practice tools: the configurable dummy and the live frame-advantage measurement.</summary>
public class TrainingToolsTests
{
    private static InputState In(params InputButton[] buttons)
    {
        int bits = 0;
        foreach (var b in buttons) bits |= (int)b;
        return new InputState((ushort)bits);
    }

    private static FightSimulation CloseSim()
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        sim.State.SetX(0, Fp.FromRatio(-3, 2));
        sim.State.SetX(1, Fp.FromRatio(3, 2));
        return sim;
    }

    private static InputState Poll(IInputProvider dummy, FightSimulation sim) => dummy.Poll(sim.Frame, sim.ViewFor(1));

    [Fact]
    public void Dummy_EachModeProducesItsInput_RelativeToFacing()
    {
        var dummy = new TrainingDummy();
        var facingRight = new FightView(0, 0, 0, 0, 1);
        var facingLeft = new FightView(0, 0, 0, 0, -1);

        Assert.Equal(new InputState(0), dummy.Poll(0, facingRight));

        dummy.Mode = DummyMode.Crouch;
        Assert.Equal(new InputState((ushort)InputButton.Down), dummy.Poll(0, facingRight));

        dummy.Mode = DummyMode.Jump;
        Assert.Equal(new InputState((ushort)InputButton.Up), dummy.Poll(0, facingRight));

        // Guard modes react: quiet while nothing threatens, back (or down-back) while the opponent attacks.
        var attackedFromRight = new FightView(0, FighterStateId.Attack, 0, 0, 1);
        var attackedFromLeft = new FightView(0, FighterStateId.Attack, 0, 0, -1);
        dummy.Mode = DummyMode.BlockHigh;
        Assert.Equal(new InputState(0), dummy.Poll(0, facingRight));
        Assert.Equal(new InputState((ushort)InputButton.Left), dummy.Poll(0, attackedFromRight));
        Assert.Equal(new InputState((ushort)InputButton.Right), dummy.Poll(0, attackedFromLeft));
        Assert.Equal(new InputState((ushort)InputButton.Left), dummy.Poll(0, new FightView(FighterStateId.Blockstun, 0, 0, 0, 1)));

        dummy.Mode = DummyMode.BlockLow;
        Assert.Equal(new InputState((ushort)InputButton.Down), dummy.Poll(0, facingRight));
        Assert.Equal(In(InputButton.Left, InputButton.Down), dummy.Poll(0, attackedFromRight));
        Assert.Equal(In(InputButton.Right, InputButton.Down), dummy.Poll(0, attackedFromLeft));
        Assert.Equal(In(InputButton.Left, InputButton.Down), dummy.Poll(0, new FightView(0, 0, 0, 0, 1) { OpponentFireballLive = true }));
    }

    [Fact]
    public void Dummy_CycleWraps_AndOnlyOffersCpuWhenItHasABrain()
    {
        var plain = new TrainingDummy();
        var seen = new List<DummyMode> { plain.Mode };
        for (int i = 0; i < 5; i++) seen.Add(plain.Cycle());
        Assert.DoesNotContain(DummyMode.Cpu, seen);
        Assert.Equal(DummyMode.Stand, seen[^1]); // Stand, Crouch, Jump, BlockHigh, BlockLow, (skip Cpu) Stand

        var brain = new CpuBrain(3, aggression: 8);
        var withBrain = new TrainingDummy(brain);
        var modes = new List<DummyMode>();
        for (int i = 0; i < 6; i++) modes.Add(withBrain.Cycle());
        Assert.Contains(DummyMode.Cpu, modes);
        Assert.Equal(DummyMode.Stand, modes[^1]);
    }

    [Fact]
    public void Dummy_BlockHigh_BlocksAStandingAttack_AndStandBlocksNothing()
    {
        foreach (var (mode, shouldBlock) in new[] { (DummyMode.BlockHigh, true), (DummyMode.Stand, false) })
        {
            var sim = CloseSim();
            var dummy = new TrainingDummy { Mode = mode };
            dummy.Attach(sim, 1);
            bool blocked = false, hit = false;
            for (int i = 0; i < 40; i++)
            {
                sim.Step(i == 0 ? In(InputButton.Light) : In(), Poll(dummy, sim));
                int state = sim.State.GetStateId(1);
                blocked |= state == FighterStateId.Blockstun;
                hit |= state == FighterStateId.Hitstun;
            }

            Assert.Equal(shouldBlock, blocked);
            Assert.Equal(!shouldBlock, hit);
        }
    }

    [Fact]
    public void FrameAdvantage_OnHit_MatchesAnIndependentMeasurement_AndBlockIsWorse()
    {
        var onHit = Measure(DummyMode.Stand);
        var onBlock = Measure(DummyMode.BlockHigh);

        Assert.NotNull(onHit.Tracked);
        Assert.Equal(onHit.Expected, onHit.Tracked!.Value.Frames);
        Assert.False(onHit.Tracked.Value.Blocked);
        Assert.Equal(0, onHit.Tracked.Value.Attacker);

        Assert.NotNull(onBlock.Tracked);
        Assert.Equal(onBlock.Expected, onBlock.Tracked!.Value.Frames);
        Assert.True(onBlock.Tracked.Value.Blocked);

        Assert.True(onHit.Tracked.Value.Frames > onBlock.Tracked.Value.Frames, "Hit advantage must exceed block advantage.");
        Assert.InRange(onHit.Tracked.Value.Frames, -20, 40);
    }

    /// <summary>Runs a light attack against the dummy and compares the tracker with counting neutral frames by hand.</summary>
    private static (AdvantageResult? Tracked, int Expected) Measure(DummyMode mode)
    {
        var sim = CloseSim();
        var dummy = new TrainingDummy { Mode = mode };
        dummy.Attach(sim, 1);
        var tracker = new FrameAdvantageTracker();

        // Step until the attack connects (the defender enters stun), feeding the tracker every frame.
        int guard = 0;
        while (sim.State.GetHitstun(1) == 0 && sim.State.GetBlockstun(1) == 0 && guard++ < 60)
        {
            sim.Step(guard == 1 ? In(InputButton.Light) : In(), Poll(dummy, sim));
            tracker.Update(sim.State);
        }

        // Independent count from the connect frame: frames until each fighter is neutral again. A held block keeps the
        // dummy in the "block" state machine but it returns to a neutral state when the stun ends.
        int attackerFree = FighterStateId.IsNeutral(sim.State.GetStateId(0)) ? 0 : -1;
        int defenderFree = -1;
        for (int elapsed = 1; elapsed <= 120 && (attackerFree < 0 || defenderFree < 0); elapsed++)
        {
            sim.Step(In(), Poll(dummy, sim));
            tracker.Update(sim.State);
            if (attackerFree < 0 && FighterStateId.IsNeutral(sim.State.GetStateId(0))) attackerFree = elapsed;
            if (defenderFree < 0 && FighterStateId.IsNeutral(sim.State.GetStateId(1))) defenderFree = elapsed;
        }

        var tracked = mode == DummyMode.Stand ? tracker.LastHit : tracker.LastBlock;
        return (tracked, defenderFree - attackerFree);
    }

    [Fact]
    public void FrameAdvantage_ReportsKnockdownInsteadOfANumber()
    {
        var sim = CloseSim();
        var tracker = new FrameAdvantageTracker();
        sim.State.SetHitstun(1, 12);
        tracker.Update(sim.State);                       // connect detected
        sim.State.SetStateId(1, FighterStateId.Knockdown);
        tracker.Update(sim.State);

        Assert.NotNull(tracker.LastHit);
        Assert.True(tracker.LastHit!.Value.Knockdown);
    }

    [Fact]
    public void FrameAdvantage_ResetClearsResults_AndGivesUpOnAStall()
    {
        var sim = CloseSim();
        var tracker = new FrameAdvantageTracker();
        sim.State.SetBlockstun(1, 9);
        tracker.Update(sim.State);
        sim.State.SetStateId(1, FighterStateId.Blockstun); // the defender never becomes neutral
        for (int i = 0; i < 200; i++) tracker.Update(sim.State);
        Assert.Null(tracker.LastBlock);

        sim.State.SetBlockstun(1, 0);
        tracker.Update(sim.State);
        sim.State.SetHitstun(1, 5);
        sim.State.SetStateId(1, FighterStateId.Idle);
        tracker.Update(sim.State);
        for (int i = 0; i < 3; i++) tracker.Update(sim.State);
        Assert.NotNull(tracker.LastHit);
        tracker.Reset();
        Assert.Null(tracker.LastHit);
        Assert.Null(tracker.LastBlock);
    }
}
