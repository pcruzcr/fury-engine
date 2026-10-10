// §53 suites: Damage + Priority + Hitstop + AABB (fight-level).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class DamageTests
{
    [Fact]
    public void ScaleTable_ExactValues()
    {
        Assert.Equal(100, DamageScaler.Apply(100, 0, false));
        Assert.Equal(92, DamageScaler.Apply(100, 1, false));
        Assert.Equal(84, DamageScaler.Apply(100, 2, false));
        Assert.Equal(77, DamageScaler.Apply(100, 3, false));
        Assert.Equal(70, DamageScaler.Apply(100, 4, false));
        Assert.Equal(62, DamageScaler.Apply(100, 5, false));
        Assert.Equal(10, DamageScaler.Apply(100, 20, false)); // floor reached
        Assert.Equal(2, DamageScaler.Apply(28, 20, false));   // 10% floor of 28 = 2
        Assert.Equal(154, DamageScaler.Apply(230, 2, true));  // super-in-combo extra −20%
        Assert.Equal(230, DamageScaler.Apply(230, 0, true));  // raw super untouched
    }

    [Fact]
    public void CounterHit_DealsTimes6Div5_WithEvent()
    {
        var s = New();
        WalkIntoRange(s);
        // Victim starts slow HK (startup 13); attacker MP (startup 7) 4f later lands in startup.
        Tap(s, 1, Buttons.HK);
        Neutrals(s, 4);
        Tap(s, 0, Buttons.MP);
        bool counterSeen = false;
        for (int k = 0; k < 30 && !counterSeen; k++)
        {
            Neutrals(s, 1);
            counterSeen = HasSimEvent(s, SimEventType.CounterHit);
        }
        Assert.True(counterSeen);
        Assert.Equal(950 - 55 * 6 / 5, s.Fighters[1].Hp); // 55 → 66
    }

    [Fact]
    public void ChipDamage_Is10PercentOfFinal()
    {
        var s = New();
        WalkIntoRange(s);
        T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);
        Tap(s, 0, Buttons.HP); // 90 dmg heavy vs high block
        for (int k = 0; k < 20; k++)
            T(s, Buttons.None, 0, 0, Buttons.None, 1, 0);
        Assert.Equal(950 - 90 / 10, s.Fighters[1].Hp); // 9 chip
    }
}

public sealed class PriorityTests
{
    private static void BothTap(FightSimulation s, Buttons b0, Buttons b1)
    {
        T(s, b0, 0, 0, b1, 0, 0);
    }

    [Fact]
    public void MediumBeatsLight_CounterApplies()
    {
        var s = New();
        WalkIntoRange(s);
        // Stagger starts so the ACTIVE windows overlap: MP (startup 7) first, LP (startup 4)
        // 3f later → both active at +7 → Medium rank wins; LP still in startup → counter.
        Tap(s, 1, Buttons.MP);
        Neutrals(s, 3);
        Tap(s, 0, Buttons.LP);
        bool counterSeen = false;
        for (int k = 0; k < 30 && !counterSeen; k++)
        {
            Neutrals(s, 1);
            counterSeen = HasSimEvent(s, SimEventType.CounterHit);
        }
        Assert.True(counterSeen); // MP wins; LP was in startup → counter
        Assert.Equal(950, s.Fighters[1].Hp); // MP user untouched
        Assert.Equal(950 - 55 * 6 / 5, s.Fighters[0].Hp); // 66
    }

    [Fact]
    public void SameLevel_ClashNoDamage()
    {
        var s = New();
        WalkIntoRange(s);
        BothTap(s, Buttons.LP, Buttons.LP);
        bool clashSeen = false;
        for (int k = 0; k < 30 && !clashSeen; k++)
        {
            Neutrals(s, 1);
            clashSeen = HasSimEvent(s, SimEventType.Clash);
        }
        Assert.True(clashSeen);
        Assert.Equal(950, s.Fighters[0].Hp);
        Assert.Equal(950, s.Fighters[1].Hp);
        Assert.Equal(FighterState.ClashStun, s.Fighters[0].State);
    }

    [Fact]
    public void SpecialBeatsNormal()
    {
        var s = New();
        WalkIntoRange(s);
        // p1 (facing -1: relative fwd = absolute -1) performs QCF+LP → SP1 (startup 10).
        T(s, Buttons.None, 0, 0, Buttons.None, 0, -1);
        T(s, Buttons.None, 0, 0, Buttons.None, -1, -1);
        T(s, Buttons.None, 0, 0, Buttons.None, -1, 0);
        T(s, Buttons.None, 0, 0, Buttons.LP, -1, 0);   // SP1 starts (frame F)
        Assert.Equal(GlenData.Sp1Short, s.Fighters[1].MoveId);
        Neutrals(s, 3);
        Tap(s, 0, Buttons.MP);                            // MP (startup 7) 3f later
        // SP1 active at F+10..F+13; MP active at F+10..F+12 → overlap → Special wins.
        bool spHit = false;
        for (int k = 0; k < 40 && !spHit; k++)
        {
            Neutrals(s, 1);
            foreach (var e in s.LastSimEvents)
                if ((e.Type is SimEventType.Hit or SimEventType.CounterHit) && e.A == 1) spHit = true;
        }
        Assert.True(spHit);
        Assert.Equal(950, s.Fighters[1].Hp);
        Assert.True(s.Fighters[0].Hp < 950);
    }

    [Fact]
    public void SuperBeatsEverything_SingleHit()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[0].Ex = 2000;
        // double QCF + P for p0
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        Assert.Equal(GlenData.Super, s.Fighters[0].MoveId);
        for (int k = 0; k < 60 && s.Fighters[1].Hp == 950; k++) Neutrals(s, 1);
        Assert.Equal(950 - 230, s.Fighters[1].Hp);
    }
}

public sealed class HitstopTests
{
    [Fact]
    public void LightHit_FreezesBoth4Frames()
    {
        var s = New();
        WalkIntoRange(s);
        Tap(s, 0, Buttons.LP);
        // find the connect step, then assert both fighters' StateFrame is frozen for 4 steps
        bool connected = false;
        int frozenSteps = 0, sfA = -1, sfB = -1;
        for (int k = 0; k < 40 && frozenSteps < 4; k++)
        {
            Neutrals(s, 1);
            if (!connected && HasSimEvent(s, SimEventType.Hit))
            {
                connected = true;
                sfA = s.Fighters[0].StateFrame;
                sfB = s.Fighters[1].StateFrame;
            }
            else if (connected)
            {
                Assert.Equal(sfA, s.Fighters[0].StateFrame);
                Assert.Equal(sfB, s.Fighters[1].StateFrame);
                frozenSteps++;
            }
        }
        Assert.True(connected);
        Assert.Equal(4, frozenSteps);
        Neutrals(s, 2); // resumes afterwards
        Assert.True(s.Fighters[0].StateFrame != sfA || s.Fighters[0].State == FighterState.Neutral);
    }

    [Fact]
    public void CounterHit_Adds2Frames()
    {
        var s = New();
        WalkIntoRange(s);
        Tap(s, 1, Buttons.HK);
        Neutrals(s, 4);
        Tap(s, 0, Buttons.MP);
        int total = 0;
        for (int k = 0; k < 40; k++)
        {
            Neutrals(s, 1);
            foreach (var e in s.LastSimEvents)
                if (e.Type == SimEventType.CounterHit) total = e.Value2;
        }
        Assert.Equal(6 + 2, total); // medium 6 + counter 2
    }

    [Fact]
    public void SuperHit_Stops20()
    {
        var s = New();
        WalkIntoRange(s);
        s.Fighters[0].Ex = 2000;
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        T(s, Buttons.None, 0, -1, Buttons.None, 0, 0);
        T(s, Buttons.None, 1, -1, Buttons.None, 0, 0);
        T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        int total = 0;
        for (int k = 0; k < 80; k++)
        {
            Neutrals(s, 1);
            foreach (var e in s.LastSimEvents)
                if (e.Type == SimEventType.Hit && e.Value == 230) total = e.Value2;
        }
        Assert.Equal(20, total);
    }
}

public sealed class AABBTests
{
    [Fact]
    public void BodiesNeverCross()
    {
        var s = New();
        for (int k = 0; k < 400; k++)
            T(s, Buttons.None, 1, 0, Buttons.None, -1, 0); // walk into each other
        Assert.True(FightKit.Gap(s) >= Calibration.FighterHalfWidth * 2 - 2);
    }

    [Fact]
    public void StageClamp_Holds()
    {
        var s = New();
        for (int k = 0; k < 600; k++)
            T(s, Buttons.None, -1, 0, Buttons.None, 1, 0); // walk apart to corners
        Assert.True(s.Fighters[0].Pos.X >= Calibration.StageMinX);
        Assert.True(s.Fighters[1].Pos.X <= Calibration.StageMaxX);
    }

    [Fact]
    public void FarAttack_WhiffsClean()
    {
        var s = New(); // spawn distance, no walking
        Tap(s, 0, Buttons.HP);
        Neutrals(s, 40);
        Assert.Equal(950, s.Fighters[1].Hp);
        Assert.False(s.Fighters[0].DidHit);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
    }

    [Fact]
    public void OverlapQueries_Agree()
    {
        var a = new Aabb(0, 0, 100, 100);
        var b = new Aabb(50, 0, 100, 100);
        var c = new Aabb(500, 0, 100, 100);
        Assert.True(a.Overlaps(b));
        Assert.False(a.Overlaps(c));
    }
}
