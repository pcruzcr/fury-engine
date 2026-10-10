// §53 suites: Energy + Gimmick (fight-level, resolved OPEN-001..005/007).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class EnergyTests
{
    private static void LandLp(FightSimulation s)
    {
        WalkIntoRange(s);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 30 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[1].Hp < 950);
    }

    [Fact]
    public void DealAndReceive_GenerateEx()
    {
        var s = New();
        LandLp(s); // 28 dmg
        Assert.Equal(28 / 5 * 30, s.Fighters[0].Ex); // 150 (5%)
        Assert.Equal(28 / 3 * 30, s.Fighters[1].Ex); // 270 (9%)
    }

    [Fact]
    public void ParrySuccess_GrantsExAndRes()
    {
        var s = New();
        WalkIntoRange(s);
        // Defender LP+MP chord same frame attacker taps LP (startup 4 → lands inside 6f stance).
        T(s, Buttons.LP, 0, 0, Buttons.LP | Buttons.MP, 0, 0);
        bool parry = false;
        for (int k = 0; k < 20 && !parry; k++)
        {
            Neutrals(s, 1);
            parry = HasSimEvent(s, SimEventType.Parry);
        }
        Assert.True(parry);
        Assert.Equal(15 * 30, s.Fighters[1].Ex);
        Assert.Equal(100, s.Fighters[1].Res);
        Assert.Equal(950, s.Fighters[1].Hp);
        Assert.Equal(FighterState.ParryStun, s.Fighters[0].State);
    }

    [Fact]
    public void ThreeConsecutiveBlocks_Cost5Percent()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[1].Ex = 1000;
        for (int b = 0; b < 3; b++)
        {
            // Victim NEVER releases the away-hold (TapKeep): no edges, no self-attacks, no backdash.
            TapKeep(s, 0, Buttons.LP, 0, 0, Buttons.None, 1, 0);
            for (int k = 0; k < 25; k++)
                T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);
        }
        Assert.Equal(1000 - 150, s.Fighters[1].Ex);
        Assert.Equal(950 - 3 * (28 / 10), s.Fighters[1].Hp);
    }

    [Fact]
    public void Tech_Grants5Percent()
    {
        var s = New();
        WalkIntoRange(s);
        // Victim holds LP+LK from INSIDE blockstun (edge eaten while non-actionable),
        // then never releases (no new edges → no self-attack). Attacker recovers, throws → tech.
        T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);          // victim away-hold (dir edge only)
        TapKeep(s, 0, Buttons.LP, 0, 0, Buttons.None, 1, 0);   // blocked jab, hold kept
        for (int k = 0; k < 5; k++)
            T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);      // impact + freeze
        for (int k = 0; k < 16; k++)
            T(s, Buttons.None, 0, 0, Buttons.LP | Buttons.LK, 1, 0); // chord held from blockstun
        Assert.Equal(FighterState.Neutral, s.Fighters[1].State); // recovered, still holding, no edge
        TapKeep(s, 0, Buttons.HP | Buttons.HK, 0, 0, Buttons.LP | Buttons.LK, 1, 0); // throw
        bool tech = false;
        for (int k = 0; k < 25 && !tech; k++)
        {
            T(s, Buttons.None, 0, 0, Buttons.LP | Buttons.LK, 1, 0);
            tech = HasSimEvent(s, SimEventType.Tech);
        }
        Assert.True(tech);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
        Assert.Equal(FighterState.Neutral, s.Fighters[1].State);
        Assert.Equal(5 * 30, s.Fighters[1].Ex); // +5% to the tech-er
    }

    [Fact]
    public void FifthHit_Bonus10Percent_Unit()
    {
        // Combo counting is proved at integration level (ChainLpMp scaled-92 test);
        // the once-per-combo +300 bonus is specified exactly here (ComboGiven==5 post-increment).
        var att = new Fighter { ComboGiven = 5 };
        EnergySystem.OnDamageDealt(att, 28);
        Assert.Equal(28 / 5 * 30 + 300, att.Ex);
        var att4 = new Fighter { ComboGiven = 4 };
        EnergySystem.OnDamageDealt(att4, 28);
        Assert.Equal(28 / 5 * 30, att4.Ex); // no bonus before the 5th
    }

    [Fact]
    public void RageActive_ZeroesGeneration()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[0].Ex = 3000;
        Neutrals(s, 1); // auto-rage fires
        Assert.True(s.Fighters[0].RageActive);
        Assert.Equal(0, s.Fighters[0].Ex);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 30 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.Equal(0, s.Fighters[0].Ex); // ×0 during rage
    }

    [Fact]
    public void ResonanceActive_HalvesGeneration()
    {
        var s = New();
        WalkIntoRange(s);
        Assert.True(s.TryActivateResonance(1, manual: true));
        Tap(s, 0, Buttons.HP); // 90 dmg → reduced to 72
        for (int k = 0; k < 40 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.Equal(950 - (90 * 4 / 5), s.Fighters[1].Hp);
        Assert.Equal((90 * 4 / 5) / 5 * 30, s.Fighters[0].Ex); // attacker unaffected: 72→420
        Assert.Equal(((90 * 4 / 5) / 3 * 30) / 2, s.Fighters[1].Ex); // victim halved: 720→360
    }

    [Fact]
    public void AutoRage_Consumes3Bars_Danimal5s()
    {
        var s = New();
        s.Fighters[0].Ex = 3000;
        Neutrals(s, 1);
        Assert.True(s.Fighters[0].RageActive);
        Assert.Equal(0, s.Fighters[0].Ex);
        Assert.Equal(EnergySystem.DanimalRageDur, s.Fighters[0].RageTimer);
        Assert.True(HasSimEvent(s, SimEventType.RageOn));
        Neutrals(s, EnergySystem.DanimalRageDur); // exactly the duration: expires this tick
        Assert.False(s.Fighters[0].RageActive);
        Assert.Equal(EnergySystem.DanimalRageCd, s.Fighters[0].RageCd);
    }

    [Fact]
    public void LethalHit_AutoTriggersResonanceOnce()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[1].Hp = 100; // <15% threshold zone: HP super (230) would leave -130
        s.Fighters[0].Ex = 2000;
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        bool auto = false;
        for (int k = 0; k < 80 && !auto; k++)
        {
            Neutrals(s, 1);
            auto = HasSimEvent(s, SimEventType.ResAuto);
        }
        Assert.True(auto);
        Assert.True(s.Fighters[1].ResUsedRound);
        // Second lethal setup this round must NOT auto-trigger again.
        s.Fighters[1].Hp = 100;
        s.Fighters[1].ResActive = false;
        s.Fighters[1].ResTimer = 0;
        // (round use consumed → TryActivateRefuses)
        Assert.False(s.TryActivateResonance(1, manual: true));
    }

    [Fact]
    public void SnowballCap_HalvesExcessGeneration_Unit()
    {
        var v = new Fighter { MaxHp = 950, ComboTaken = 380 }; // 40% already taken
        EnergySystem.OnDamageTaken(v, 60);
        Assert.Equal((60 / 3 * 30) / 2, v.Ex); // all excess → 300, not 600
        Assert.Equal(440, v.ComboTaken);
    }

    [Fact]
    public void LoserGrace_NextRound30Percent()
    {
        var f = new Fighter { MaxHp = 950, Hp = 0, Ex = 0 };
        EnergySystem.ResetForRound(f, isLoser: true);
        Assert.Equal(900, f.Ex);
        Assert.Equal(950, f.Hp);
    }
}

public sealed class GimmickTests
{
    [Fact]
    public void Rage_ClampsMaxHp10Percent()
    {
        var s = New();
        s.Fighters[0].Ex = 3000;
        Neutrals(s, 1);
        Assert.Equal(950 * 9 / 10, s.Fighters[0].Hp); // 855
    }

    [Fact]
    public void Rage_BoostsPhysical25Percent()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[0].Ex = 3000;
        Neutrals(s, 1);
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 30 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.Equal(950 - 28 * 5 / 4, s.Fighters[1].Hp); // 35
    }

    [Fact]
    public void Rage_ArmorAbsorbsInterruptionOnce()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[0].Ex = 3000;
        Neutrals(s, 1); // p0 raging
        // p0 starts slow HP (startup 12→10 raged); p1 jabs so LP lands mid-startup,
        // strictly before HP goes active (no clash possible).
        Tap(s, 0, Buttons.HP);
        Neutrals(s, 4);
        Tap(s, 1, Buttons.LP); // active at +4/+5 global; HP active at +10
        for (int k = 0; k < 30; k++) Neutrals(s, 1);
        Assert.True(s.Fighters[0].ArmorUsed); // absorbed instead of interrupting
        Assert.True(s.Fighters[0].Hp < 855);  // damage still sticks
        Assert.True(s.Fighters[1].Hp < 950);  // HP later landed: no interruption happened
    }

    [Fact]
    public void CounterStance_ReflectsTimes1Point5()
    {
        var s = New();
        WalkIntoRange(s);
        // p0 SP3 (DP+K): 8f startup then 30f counter stance.
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.LK, 1, -1, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Sp3, s.Fighters[0].MoveId);
        Neutrals(s, 9); // stance live
        Tap(s, 1, Buttons.LP);
        bool reflected = false;
        for (int k = 0; k < 30 && !reflected; k++)
        {
            Neutrals(s, 1);
            reflected = HasSimEvent(s, SimEventType.Reflected);
        }
        Assert.True(reflected);
        Assert.Equal(950 - 28 * 3 / 2, s.Fighters[1].Hp); // 42 reflected
        Assert.Equal(950, s.Fighters[0].Hp); // defender takes nothing
    }
}
