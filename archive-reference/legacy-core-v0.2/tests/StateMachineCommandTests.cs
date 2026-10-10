// §53 suites: StateMachine + CommandNormal (fight-level).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void LpStartsAttack_ReturnsToNeutral()
    {
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.LP);
        Assert.Equal(FighterState.Attack, s.Fighters[0].State);
        Assert.Equal(GlenData.LP, s.Fighters[0].MoveId);
        Neutrals(s, 30);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
    }

    [Fact]
    public void HpCausesHitstunThenPendingKnockdown()
    {
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.HP);
        // wait for connect
        for (int k = 0; k < 30 && s.Fighters[1].State != FighterState.Hitstun; k++)
            Neutrals(s, 1);
        Assert.Equal(FighterState.Hitstun, s.Fighters[1].State);
        Assert.Equal(Calibration.HitstunHeavy, s.Fighters[1].StateDuration);
        Assert.True(s.Fighters[1].PendingKnockdown);
        // Poll for transitions (immune to hitstop offsets): stun → knockdown → wakeup → neutral.
        for (int k = 0; k < 80 && s.Fighters[1].State != FighterState.Knockdown; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Knockdown, s.Fighters[1].State);
        for (int k = 0; k < 80 && s.Fighters[1].State != FighterState.Wakeup; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Wakeup, s.Fighters[1].State);
        for (int k = 0; k < 40 && s.Fighters[1].State != FighterState.Neutral; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Neutral, s.Fighters[1].State);
    }

    [Fact]
    public void BlockProducesBlockstun_WithFormula()
    {
        var s = New();
        WalkIntoRange(s);
        // p1 holds away (DirX == -facing == +1).
        T(s, Buttons.None, 0, 0, Buttons.None, 1, 0); // p0 neutral-ish, p1 block stance
        Tap(s, 0, Buttons.LP); // will be blocked (p1 still holding? need hold through impact)
        for (int k = 0; k < 12; k++)
            T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);
        Assert.Equal(950 - 28 / 10, s.Fighters[1].Hp); // chip OPEN-001: 28/10 = 2
        // blockstun = attackerRemaining(14-4=10) + onBlock(+2) = 12, plus 4 frozen hitstop
        // steps: still in Blockstun 12 loop steps after the tap.
        Assert.Equal(FighterState.Blockstun, s.Fighters[1].State);
    }

    [Fact]
    public void DoubleTapBack_BackdashWithInvuln()
    {
        var s = New();
        WalkIntoRange(s);
        // From neutral: back-tap, release, back-tap again (first tap originates from neutral).
        T(s, Buttons.None, 0, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, -1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, -1, 0, Buttons.None, 0, 0);
        Assert.Equal(FighterState.Backdash, s.Fighters[0].State);
        Assert.True(s.Fighters[0].InvulnT > 0);
        Neutrals(s, 20);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
    }

    [Fact]
    public void LethalHit_KoEndsRound()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[1].Hp = 10;
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 30 && !s.Over; k++) Neutrals(s, 1);
        Assert.True(s.Over);
        Assert.Equal(0, s.Winner);
        Assert.Equal(FighterState.KO, s.Fighters[1].State);
    }

    [Fact]
    public void JumpRisesAndLands()
    {
        var s = New();
        T(s, Buttons.None, 0, 1, Buttons.None, 0, 0); // p0 jump edge
        Assert.True(s.Fighters[0].Airborne);
        for (int k = 0; k < 120 && s.Fighters[0].Airborne; k++) Neutrals(s, 1);
        Assert.False(s.Fighters[0].Airborne);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
    }

    [Fact]
    public void Timeout_HigherHpWins()
    {
        var s = New(roundTimer: 10);
        s.Fighters[1].Hp = 500;
        Neutrals(s, 15);
        Assert.True(s.Over);
        Assert.Equal(0, s.Winner);
    }
}

public sealed class CommandNormalTests
{
    [Fact]
    public void FwdHp_ResolvesTo11() { var s = New(); WalkIntoRange(s); Tap(s, 0, Buttons.HP, 1, 0); Assert.Equal(GlenData.F_HP, s.Fighters[0].MoveId); }

    [Fact]
    public void BackLk_ResolvesTo19() { var s = New(); WalkIntoRange(s); Tap(s, 0, Buttons.LK, -1, 0); Assert.Equal(GlenData.B_LK, s.Fighters[0].MoveId); }

    [Fact]
    public void Sweep_ResolvesTo20() { var s = New(); WalkIntoRange(s); Tap(s, 0, Buttons.LK, -1, -1); Assert.Equal(GlenData.Sweep, s.Fighters[0].MoveId); }

    [Fact]
    public void NeutralHp_ResolvesTo3() { var s = New(); WalkIntoRange(s); Tap(s, 0, Buttons.HP); Assert.Equal(GlenData.HP, s.Fighters[0].MoveId); }

    [Fact]
    public void BackHp_ResolvesTo12() { var s = New(); WalkIntoRange(s); Tap(s, 0, Buttons.HP, -1, 0); Assert.Equal(GlenData.B_HP, s.Fighters[0].MoveId); }

    [Fact]
    public void QcfPunch_ResolvesSp1Short()
    {
        var s = New();
        // p0 facing +1: down, down-fwd, fwd, fwd+LP edge.
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.LP, 1, 0, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Sp1Short, s.Fighters[0].MoveId);
    }

    [Fact]
    public void QcfHp_ResolvesSp1Far()
    {
        var s = New();
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Sp1Far, s.Fighters[0].MoveId);
    }

    [Fact]
    public void DpKick_ResolvesSp3()
    {
        var s = New();
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.LK, 1, -1, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Sp3, s.Fighters[0].MoveId);
    }

    [Fact]
    public void SpinPunch_ResolvesSp2()
    {
        // 360 ends in U+HP on ONE frame: the motion eats the jump input (input priority),
        // so no Jump state intervenes. A lone U frame beforehand would jump (tested elsewhere).
        var s = New();
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, -1, 0, Buttons.None, 0, 0);
        T(s, Buttons.HP, 0, 1, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Sp2, s.Fighters[0].MoveId);
        Assert.False(s.Fighters[0].Airborne);
    }

    [Fact]
    public void DoubleQcfPunch_WithMeter_ResolvesSuper()
    {
        var s = New();
        s.Fighters[0].Ex = 2000;
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Super, s.Fighters[0].MoveId);
        Assert.Equal(0, s.Fighters[0].Ex);
    }

    [Fact]
    public void DoubleQcfPunch_WithoutMeter_NoSilentFallback()
    {
        var s = New();
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        // Double motion matched but meter insufficient: super gated, no silent fallback.
        Assert.Equal(0, s.Fighters[0].MoveId);
    }

    [Fact]
    public void WhiffedNormal_CannotCancelIntoNormal()
    {
        var s = New(); // spawn distance: attacks whiff (no DidHit)
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 5; k++) Neutrals(s, 1); // mid-recovery
        Assert.Equal(FighterState.Attack, s.Fighters[0].State);
        Tap(s, 0, Buttons.MP);
        Assert.Equal(GlenData.LP, s.Fighters[0].MoveId); // still LP: no whiff cancel
    }

    [Fact]
    public void ChainLpLp_OnHit_Scaled92()
    {
        // Hitstop eats inputs (both frozen, standard behavior): the chain tap must land
        // AFTER the 4 frozen steps, inside the remaining recovery (LP total 14f).
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 20 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[0].DidHit);
        Assert.Equal(950 - 28, s.Fighters[1].Hp);
        Neutrals(s, 5); // clear the 4 hitstop steps + 1 live frame
        Tap(s, 0, Buttons.LP);
        Assert.False(s.Fighters[0].DidHit); // fresh activation (chain restarted the move)
        for (int k = 0; k < 40 && s.Fighters[1].Hp == 950 - 28; k++) Neutrals(s, 1);
        Assert.Equal(950 - 28 - 28 * 23 / 25, s.Fighters[1].Hp); // 2nd hit at 92% = 25
    }
}
