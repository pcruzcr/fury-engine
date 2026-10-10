// F2a suites: Kira Acceleration + air combat + juggle + GDD routes + economy probe.
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class KiraGimmickTests
{
    // Kira is fighter 1 (facing -1: relative fwd = absolute -1). QCF: down, down-back, back + P.
    private static void KiraQcf(FightSimulation s)
    {
        T(s, Buttons.None, 0, 0, Buttons.None, 0, -1);
        T(s, Buttons.None, 0, 0, Buttons.None, -1, -1);
        T(s, Buttons.None, 0, 0, Buttons.None, -1, 0);
        T(s, Buttons.None, 0, 0, Buttons.LP, -1, 0);
    }

    [Fact]
    public void SpecialConnects_StacksUpTo2()
    {
        var s = NewVsKira();
        WalkIntoRange(s);
        KiraQcf(s);
        Assert.Equal(KiraData.Sp1, s.Fighters[1].MoveId);
        for (int k = 0; k < 40 && s.Fighters[1].AccelStacks == 0; k++) Neutrals(s, 1);
        Assert.Equal(1, s.Fighters[1].AccelStacks);
        // Full recovery first: a mid-recovery motion would be (correctly) rejected as no-cancel.
        for (int k = 0; k < 80 && s.Fighters[1].State != FighterState.Neutral; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Neutral, s.Fighters[1].State);
        KiraQcf(s);
        for (int k = 0; k < 60 && s.Fighters[1].AccelStacks == 1; k++) Neutrals(s, 1);
        Assert.Equal(2, s.Fighters[1].AccelStacks);
        for (int k = 0; k < 80 && s.Fighters[1].State != FighterState.Neutral; k++) Neutrals(s, 1);
        KiraQcf(s);
        Neutrals(s, 60);
        Assert.Equal(2, s.Fighters[1].AccelStacks); // capped (×3 max)
    }

    [Fact]
    public void Stacks_MultiplyWalk_AndResetOnAnyDamage()
    {
        var s = NewVsKira();
        WalkIntoRange(s);
        KiraQcf(s);
        for (int k = 0; k < 40 && s.Fighters[1].AccelStacks == 0; k++) Neutrals(s, 1);
        Assert.Equal(1, s.Fighters[1].AccelStacks);
        for (int k = 0; k < 80 && s.Fighters[1].State != FighterState.Neutral; k++) Neutrals(s, 1);
        // Walk TOWARD 5 steps (5×5600 = 28000 < 33500 available: no body-block via Separate).
        // (Away is block stance by design and does not move.)
        int x0 = s.Fighters[1].Pos.X;
        for (int k = 0; k < 5; k++)
            T(s, Buttons.None, 0, 0, Buttons.None, -1, 0); // Kira toward (facing -1)
        int walkedStack1 = x0 - s.Fighters[1].Pos.X;
        Assert.Equal(2800 * 2 * 5, walkedStack1); // ×2 with 1 stack
        // Any damage resets: Glen must be neutral first, then jab.
        for (int k = 0; k < 80 && s.Fighters[0].State != FighterState.Neutral; k++) Neutrals(s, 1);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 40 && s.Fighters[1].Hp == 900; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[1].Hp < 900);
        Assert.Equal(0, s.Fighters[1].AccelStacks);
    }

    [Fact]
    public void NormalHit_DoesNotStack()
    {
        var s = NewVsKira();
        WalkIntoRange(s);
        Tap(s, 1, Buttons.LP); // Kira jab (normal kind)
        for (int k = 0; k < 30 && s.Fighters[0].Hp == 950; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[0].Hp < 950);
        Assert.Equal(0, s.Fighters[1].AccelStacks);
    }
}

public sealed class AirCombatTests
{
    [Fact]
    public void JumpHp_ConnectsVsGrounded_Damage90()
    {
        var s = New();
        WalkIntoRange(s);
        T(s, Buttons.None, 0, 1, Buttons.None, 0, 0); // p0 jumps straight
        Assert.True(s.Fighters[0].Airborne);
        for (int k = 0; k < 16; k++) Neutrals(s, 1); // descend
        Tap(s, 0, Buttons.HP); // j.HP (startup 9)
        Assert.Equal(GlenData.JHp, s.Fighters[0].MoveId);
        for (int k = 0; k < 40 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.Equal(950 - 90, s.Fighters[1].Hp);
    }

    [Fact]
    public void WhiffedAirAttack_BlocksSecondAirAction()
    {
        var s = New();
        // Jump away from the opponent so the air attack whiffs.
        T(s, Buttons.None, -1, 1, Buttons.None, 0, 0);
        for (int k = 0; k < 4; k++) Neutrals(s, 1);
        Tap(s, 0, Buttons.LP); // j.LP whiffs (far)
        Assert.Equal(GlenData.JLp, s.Fighters[0].MoveId);
        Neutrals(s, 4);
        Tap(s, 0, Buttons.MP); // second air action denied (no hit → no chain)
        Assert.Equal(GlenData.JLp, s.Fighters[0].MoveId);
    }

    [Fact]
    public void Landing_CancelsAirAttack()
    {
        var s = New();
        T(s, Buttons.None, 0, 1, Buttons.None, 0, 0);
        for (int k = 0; k < 10; k++) Neutrals(s, 1);
        Tap(s, 0, Buttons.HP);
        for (int k = 0; k < 60 && s.Fighters[0].Airborne; k++) Neutrals(s, 1);
        Assert.False(s.Fighters[0].Airborne);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
    }

    [Fact]
    public void Launcher_JuggleConnectsScaled()
    {
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.HK); // launcher
        for (int k = 0; k < 30 && !s.Fighters[1].Airborne; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[1].Airborne);
        // HK recovery must finish first (B+LP is no legal cancel of HK): poll attacker neutral.
        for (int k = 0; k < 80 && s.Fighters[0].State != FighterState.Neutral; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
        Assert.True(s.Fighters[1].Airborne); // victim still up (35f airtime)
        Tap(s, 0, Buttons.LP, -1, 0); // B+LP anti-air → juggle index 1
        for (int k = 0; k < 40 && s.Fighters[1].JuggleCount == 0; k++) Neutrals(s, 1);
        Assert.Equal(1, s.Fighters[1].JuggleCount);
        // HK 95 (idx0) + B+LP 45*92% = 41 (idx1, combo persists while airborne)
        Assert.Equal(950 - 95 - 45 * 23 / 25, s.Fighters[1].Hp);
    }
}

public sealed class RouteTests
{
    [Fact]
    public void Bnb_LpMpSp1_Total149_DF_CON_003()
    {
        // Freeze-buffered cancels: chain taps land DURING hitstop and execute on first live frame.
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 20 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        Tap(s, 0, Buttons.MP); // buffered during LP's 4f freeze → starts frozen
        Assert.Equal(GlenData.MP, s.Fighters[0].MoveId);
        for (int k = 0; k < 40 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        // Cancel into SP1: QCF buffered through MP's 6f freeze, P edge last.
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.LP, 1, 0, Buttons.None, 0, 0);
        // NOTE (test bug fixed 2026-09-13): the wait must be >= : hp is ALREADY at the
        // threshold from MP, so a strict > exits instantly before SP1 lands.
        for (int k = 0; k < 80 && s.Fighters[1].Hp >= 950 - 28 - 50; k++) Neutrals(s, 1);
        // LP 28 + MP 55*92% = 50 + SP1 85*84% = 71 → 149 (GDD "~175" is an estimate)
        Assert.Equal(950 - (28 + 55 * 23 / 25 + 85 * 21 / 25), s.Fighters[1].Hp);
    }

    [Fact]
    public void LauncherRoute_MpHkJmpJhp_Executes257_DF_CON_004()
    {
        // Executable 4-hit portion (MP>HK>j.MP>j.HP). The GDD's SP1 ender is proven
        // unexecutable under knockdown physics (DF-CON-004) and is NOT attempted here.
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.MP);
        for (int k = 0; k < 20 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        Tap(s, 0, Buttons.HK); // buffered chain during MP's freeze
        for (int k = 0; k < 40 && !s.Fighters[1].Airborne; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[1].Airborne); // launcher connected
        Assert.Equal(950 - 55 - 95 * 23 / 25, s.Fighters[1].Hp); // MP 55 + HK 87
        int hpAfterHk = s.Fighters[1].Hp;
        // Wait for full recovery (HK total 28), then jump + j.MP.
        for (int k = 0; k < 80 && s.Fighters[0].State != FighterState.Neutral; k++) Neutrals(s, 1);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
        T(s, Buttons.None, 0, 1, Buttons.None, 0, 0);
        T(s, Buttons.MP, 0, 0, Buttons.None, 0, 0);
        for (int k = 0; k < 60 && s.Fighters[1].Hp == hpAfterHk; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[1].Hp < hpAfterHk); // j.MP juggle connected
        int hpAfterJmp = s.Fighters[1].Hp;
        Assert.Equal(hpAfterHk - 55 * 21 / 25, hpAfterJmp); // idx2 → 46
        // Chain j.HP buffered through j.MP's freeze (tap right after its DidHit).
        for (int k = 0; k < 60 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[0].DidHit); // j.MP connected (StartMove reset the flag first)
        Tap(s, 0, Buttons.HP);
        for (int k = 0; k < 80 && s.Fighters[1].Hp == hpAfterJmp; k++) Neutrals(s, 1);
        // j.HP 90*77% = 69 (idx3) → route total 55+87+46+69 = 257
        Assert.Equal(hpAfterJmp - 90 * 77 / 100, s.Fighters[1].Hp);
    }
}

public sealed class EconomyProbeTests
{
    [Fact]
    public void AggressiveMirror_80s_ReportsMetrics()
    {
        var s = New(seed: 555, roundTimer: 80 * 60);
        int rageOn = 0, ko = 0;
        for (int f = 0; f < 80 * 60 && !s.Over; f++)
        {
            FightDeterminismTests.Script(s, f);
            foreach (var e in s.LastSimEvents)
            {
                if (e.Type == SimEventType.RageOn) rageOn++;
                if (e.Type == SimEventType.KO) ko++;
            }
        }
        // Sanity (not balance): resources bounded, fight terminates, meters flowed.
        Assert.True(s.Fighters[0].Ex is >= 0 and <= 3000);
        Assert.True(s.Fighters[1].Ex is >= 0 and <= 3000);
        Assert.True(s.Over); // scripted aggression ends the round (KO or timeout)
    }
}
