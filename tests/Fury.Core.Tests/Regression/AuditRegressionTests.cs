using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Rollback;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Regression;

/// <summary>
/// Regressions from the 2026-10 code audit. Each test fails on the code before its fix.
/// </summary>
public class AuditRegressionTests
{
    private static FightSimulation CreateSim() => new(GlenData.Create(), KiraData.Create());

    private static void WalkIntoRange(FightSimulation sim)
    {
        var walk = new InputState((ushort)InputButton.Right);
        long closeEnough = Fp.FromRatio(5, 2).Raw;
        for (int f = 0; f < 240 && (sim.State.GetX(1) - sim.State.GetX(0)).Raw > closeEnough; f++) sim.Step(walk, new InputState(0));
        sim.Step(new InputState(0), new InputState(0));
    }

    // ------------------------------------------------------------------ meters

    [Fact]
    public void SingleHit_GrantsExGainPoints_NotSaturatedMeter()
    {
        // ExGainPerHit.Raw (12 << 16) used to be added as points, clamping EX to 300 and
        // Resonance to 100 on the first hit: one jab unlocked the Super.
        var sim = CreateSim();
        WalkIntoRange(sim);
        var cfg = FightConfig.Default();
        int before = sim.State.GetHealth(1);
        for (int f = 0; f < 30 && sim.State.GetHealth(1) == before; f++)
        {
            sim.Step(new InputState(f == 0 ? (ushort)InputButton.Light : (ushort)0), new InputState(0));
        }

        Assert.True(sim.State.GetHealth(1) < before);
        Assert.Equal(cfg.ExGainPerHit.FloorInt, sim.State.GetExMeter(0));
        Assert.Equal(cfg.ResonanceGainOnHurt.FloorInt, sim.State.GetResonance(1));
    }

    // ------------------------------------------------------------------ Fp math

    [Theory]
    [InlineData(-32768, 0)]   // -0.5 -> 0 (was -1)
    [InlineData(-98304, -1)]  // -1.5 -> -1 (was -2)
    [InlineData(98304, 1)]
    public void TruncInt_RoundsTowardZero(int raw, int expected)
    {
        Assert.Equal(expected, Fp.FromRaw(raw).TruncInt);
    }

    [Fact]
    public void ToString_NegativeFraction_IsCorrect()
    {
        Assert.Equal("-0.500", Fp.FromRaw(-32768).ToString());
        Assert.Equal("-1.250", Fp.FromRatio(-5, 4).ToString());
    }

    [Fact]
    public void Sqrt_LargeValues_DoNotOverflow()
    {
        // Newton in Q16.16 overflowed (x + v/x) near MaxValue.
        var r = Fp.Sqrt(Fp.FromInt(32000));
        Assert.True(System.Math.Abs(r.ToFloat() - 178.885f) < 0.01f, r.ToString());
        Assert.Equal(Fp.FromInt(3), Fp.Sqrt(Fp.FromInt(9)));
    }

    [Fact]
    public void Tan_IsAccurateAcrossRange()
    {
        // The 4-term Taylor series gave 1.62 for tan(60°) and 4.3 for tan(1.5 rad).
        Assert.True(System.Math.Abs(Fp.Tan(Fp.DegreesToRadians(Fp.FromInt(60))).ToFloat() - 1.7320508f) < 0.01f);
        Assert.True(System.Math.Abs(Fp.Tan(Fp.FromRatio(3, 2)).ToFloat() - 14.1014f) / 14.1014f < 0.02f);
        Assert.True(System.Math.Abs(Fp.Sin(Fp.FromRatio(1, 2)).ToFloat() - 0.4794255f) < 0.001f);
        Assert.True(System.Math.Abs(Fp.Cos(Fp.FromInt(2)).ToFloat() + 0.4161468f) < 0.001f);
    }

    // ------------------------------------------------------------------ roster

    [Fact]
    public void CharacterRoster_ResolvesIdsAndRejectsUnknown()
    {
        Assert.Equal("glen", CharacterRoster.Create("GLEN").Id);
        Assert.Equal("kira", CharacterRoster.Create("kira").Id);
        Assert.Throws<ArgumentException>(() => CharacterRoster.Create("nobody"));
    }

    // ------------------------------------------------------------------ rollback

    [Fact]
    public void RollbackSession_WrappedSlot_IsNotConfirmed()
    {
        var session = new RollbackSession(inputDelay: 0, capacityFrames: 8);
        session.PushLocal(0, new InputState(0));
        session.PredictFor(0);
        session.DeliverRemote(0, new InputState(0x10), currentFrame: 0);

        // Frame 8 reuses slot 0. Its remote input is unknown: it must be predicted from the
        // last confirmed frame (here none in the 8-frame window -> default), never served
        // as "confirmed 0x10" from frame 0.
        session.PushLocal(8, new InputState(0));
        session.PredictFor(8);
        Assert.Equal(new InputState(0), session.GetRemote(8));
    }

}
