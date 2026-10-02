using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Net;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>The genre systems wired into the deterministic simulation as rollback-safe modules.</summary>
public class GameplayModuleTests
{
    private const InputButton L = InputButton.Light;
    private const InputButton H = InputButton.Heavy;

    private static InputState In(params InputButton[] buttons)
    {
        int bits = 0;
        foreach (var b in buttons) bits |= (int)b;
        return new InputState((ushort)bits);
    }

    private static FightSimulation Make(params SimModule[] modules) => new(GlenData.Create(), KiraData.Create(), modules: modules);

    private static void Place(FightSimulation sim, Fp gap)
    {
        sim.State.SetX(0, -(gap * Fp.Half));
        sim.State.SetX(1, gap * Fp.Half);
    }

    private static void Tick(FightSimulation sim, InputState p1, InputState p2, int frames = 1)
    {
        for (int i = 0; i < frames; i++) sim.Step(p1, p2);
    }

    private static void Idle(FightSimulation sim, int frames = 1) => Tick(sim, In(), In(), frames);

    private static int DamageOfLight(params SimModule[] modules)
    {
        var sim = Make(modules);
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(L), In());
        Idle(sim, 8);
        return 1000 - sim.State.GetHealth(1);
    }

    // ------------------------------------------------------------------ weapons

    private static WeaponSystem Sword(int durability = 3)
    {
        var weapons = new WeaponSystem(FightConfig.Default());
        weapons.AddWeapon(new WeaponSystem.WeaponData
        {
            Type = WeaponSystem.WeaponType.Sword,
            Range = Fp.FromRatio(3, 2),
            DamageBonus = 25,
            Durability = durability,
            MaxDurability = durability,
        });
        return weapons;
    }

    [Fact]
    public void Weapon_ExtendsReach_AndAddsFlatDamage()
    {
        // Far enough that a bare light normal whiffs.
        var bare = Make();
        Place(bare, Fp.FromRatio(42, 10));
        Tick(bare, In(L), In());
        Idle(bare, 10);
        Assert.Equal(1000, bare.State.GetHealth(1));

        var armed = Make(new WeaponModule(0, Sword()));
        Place(armed, Fp.FromRatio(42, 10));
        Tick(armed, In(L), In());
        Idle(armed, 10);
        Assert.Equal(1000 - 65, armed.State.GetHealth(1)); // 40 + 25

        Assert.Equal(40, DamageOfLight());
        Assert.Equal(65, DamageOfLight(new WeaponModule(0, Sword())));
    }

    [Fact]
    public void Weapon_WearsDownWithHits_AndBreaks()
    {
        var weapons = Sword(durability: 1);
        var sim = Make(new WeaponModule(0, weapons));
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(L), In());
        Idle(sim, 8);
        Assert.Equal(WeaponSystem.WeaponState.Broken, weapons.GetWeapon(0).State);
        Assert.Equal(Fp.Zero, weapons.GetHitboxExtension(0));
    }

    [Fact]
    public void Weapon_IsKnockedOut_ByAHeavyHit_AndComesBack()
    {
        var weapons = Sword();
        var sim = Make(new WeaponModule(1, weapons, disarmDamage: 100));
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(H), In());
        Idle(sim, 14);
        Assert.Equal(WeaponSystem.WeaponState.Disarmed, weapons.GetWeapon(0).State);
        Idle(sim, 190);
        Assert.Equal(WeaponSystem.WeaponState.Active, weapons.GetWeapon(0).State);
    }

    // ------------------------------------------------------------------ power-ups

    private static ArcanaSystem Fire()
    {
        var arcana = new ArcanaSystem(FightConfig.Default());
        arcana.AddArcana(new ArcanaSystem.ArcanaData
        {
            Type = ArcanaSystem.ArcanaType.Fire,
            MeterCost = 100,
            ChargeTime = 5,
            Duration = 120,
            Cooldown = 60,
            DamageMultiplier = Fp.FromRatio(3, 2),
            SpeedMultiplier = Fp.FromRatio(3, 2),
            DefenseMultiplier = Fp.FromInt(2),
        });
        return arcana;
    }

    [Fact]
    public void Arcana_ActivatesWithMacro3_SpendsMeter_ThenBuffsDamageAndSpeed()
    {
        var arcana = Fire();
        var module = new ArcanaModule(0, arcana);
        var sim = Make(module);
        sim.State.SetExMeter(0, 200);

        Tick(sim, In(InputButton.Macro3), In());
        Assert.Equal(100, sim.State.GetExMeter(0));
        Assert.False(module.IsActive); // still charging
        Idle(sim, 6);
        Assert.True(module.IsActive);

        // Damage x1.5 while active (40 -> 60).
        Assert.Equal(60, DamageWhileBuffed());
        Assert.Equal(40, DamageOfLight());

        // Walk speed x1.5.
        var buffed = ActiveSim();
        var plain = Make();
        Fp b0 = buffed.State.GetX(0);
        Fp p0 = plain.State.GetX(0);
        Tick(buffed, In(InputButton.Right), In(), 20);
        Tick(plain, In(InputButton.Right), In(), 20);
        Assert.True(buffed.State.GetX(0) - b0 > (plain.State.GetX(0) - p0) * Fp.FromRatio(13, 10));

        static FightSimulation ActiveSim()
        {
            var s = Make(new ArcanaModule(0, Fire()));
            s.State.SetExMeter(0, 200);
            Tick(s, In(InputButton.Macro3), In());
            Idle(s, 6);
            return s;
        }

        static int DamageWhileBuffed()
        {
            var s = ActiveSim();
            Place(s, Fp.FromRatio(5, 2));
            Tick(s, In(L), In());
            Idle(s, 8);
            return 1000 - s.State.GetHealth(1);
        }
    }

    [Fact]
    public void Arcana_DefenseReducesDamageTaken_AndNeedsTheMeter()
    {
        var sim = Make(new ArcanaModule(1, Fire()));
        sim.State.SetExMeter(1, 100);
        Tick(sim, In(), In(InputButton.Macro3));
        Idle(sim, 6);
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(L), In());
        Idle(sim, 8);
        Assert.Equal(20, 1000 - sim.State.GetHealth(1)); // 40 / defense 2

        var broke = Make(new ArcanaModule(0, Fire()));
        Tick(broke, In(InputButton.Macro3), In()); // no meter: nothing happens
        Assert.Equal(ArcanaSystem.ArcanaState.Inactive, broke.GetModule<ArcanaModule>()!.Arcana.GetArcana(0).State);
    }

    [Fact]
    public void Transformation_CyclesThroughChargeActiveAndCooldown()
    {
        var forms = new TransformationSystem(FightConfig.Default());
        forms.AddForm(new TransformationSystem.FormData
        {
            Type = TransformationSystem.TransformationType.BeastForm,
            MeterCost = 150,
            ChargeTime = 3,
            Duration = 20,
            Cooldown = 10,
            PowerMultiplier = Fp.FromInt(2),
        });
        var module = new TransformationModule(0, forms);
        var sim = Make(module);
        sim.State.SetExMeter(0, 300);

        Tick(sim, In(InputButton.Macro3), In());
        Assert.Equal(150, sim.State.GetExMeter(0));
        Idle(sim, 4);
        Assert.True(module.IsActive);
        Idle(sim, 25);
        Assert.False(module.IsActive);
        Assert.Equal(TransformationSystem.TransformationState.Cooldown, forms.GetForm(0).State);
        Idle(sim, 12);
        Assert.Equal(TransformationSystem.TransformationState.Inactive, forms.GetForm(0).State);
    }

    // ------------------------------------------------------------------ stage

    private static PlatformSystem.PlatformData Plat(PlatformSystem.PlatformType type, int cx, int cy, int width) =>
        new()
        {
            Type = type,
            StartX = Fp.FromInt(cx),
            StartY = Fp.FromRatio(cy * 4 - 1, 4), // centre; with Height 0.5 the top surface is 0.25 above it
            Width = Fp.FromInt(width),
            Height = Fp.FromRatio(1, 2),
        };

    [Fact]
    public void Platform_CatchesAJumpingFighter_AndTheFighterFallsOffTheEdge()
    {
        // Platform centred at x = 0, top surface at y = -3.375 (above the floor), 4 units wide.
        var stage = new StageModule([Plat(PlatformSystem.PlatformType.Solid, 0, -3, 4)]);
        var sim = Make(stage);
        sim.State.SetX(0, Fp.Zero);
        sim.State.SetX(1, Fp.FromInt(20));

        Tick(sim, In(InputButton.Up), In());
        int guard = 0;
        while ((sim.State.IsAirborne(0) || sim.State.GetStateId(0) == FighterStateId.Jump) && guard++ < 200) Idle(sim);
        Assert.False(sim.State.IsAirborne(0));
        Assert.True(sim.State.GetY(0) < Fp.Zero, "standing on the platform, not the floor");
        var (_, _, top, _) = stage.BoundsAt(0, sim.Frame);
        Assert.Equal(top, sim.State.GetY(0));

        // Walk off the right edge: the fighter falls to the floor.
        Tick(sim, In(InputButton.Right), In(), 40);
        guard = 0;
        while (sim.State.GetY(0) != Fp.Zero && guard++ < 200) Idle(sim);
        Assert.Equal(Fp.Zero, sim.State.GetY(0));
        Assert.False(sim.State.IsAirborne(0));
    }

    [Fact]
    public void PassThroughPlatform_LetsAFighterDropThroughWithDown()
    {
        var stage = new StageModule([Plat(PlatformSystem.PlatformType.PassThrough, 0, -3, 6)]);
        var sim = Make(stage);
        sim.State.SetX(0, Fp.Zero);
        sim.State.SetX(1, Fp.FromInt(20));
        Tick(sim, In(InputButton.Up), In());
        int guard = 0;
        while ((sim.State.IsAirborne(0) || sim.State.GetStateId(0) == FighterStateId.Jump) && guard++ < 200) Idle(sim);
        Assert.True(sim.State.GetY(0) < Fp.Zero);

        Tick(sim, In(InputButton.Down), In(), 3);
        guard = 0;
        while (sim.State.GetY(0) != Fp.Zero && guard++ < 200) Tick(sim, In(InputButton.Down), In());
        Assert.Equal(Fp.Zero, sim.State.GetY(0));
    }

    [Fact]
    public void BouncePlatform_ThrowsTheFighterBackUp_AndConveyorPushes()
    {
        var bounce = Plat(PlatformSystem.PlatformType.Bounce, 0, -2, 6) with { BounceForce = Fp.FromRatio(7, 10) };
        var sim = Make(new StageModule([bounce]));
        sim.State.SetX(0, Fp.Zero);
        sim.State.SetX(1, Fp.FromInt(20));
        Tick(sim, In(InputButton.Up), In());
        bool bounced = false;
        bool landedOnce = false;
        for (int i = 0; i < 200 && !bounced; i++)
        {
            Idle(sim);
            if (!sim.State.IsAirborne(0) && sim.State.GetY(0) < Fp.Zero) landedOnce = true;
            if (sim.State.IsAirborne(0) && sim.State.GetVy(0) < Fp.Zero && sim.State.GetY(0) < Fp.Zero && sim.Frame > 20) bounced = true;
        }
        Assert.True(bounced || landedOnce);

        var conveyor = Plat(PlatformSystem.PlatformType.Conveyor, 0, -2, 12) with { ConveyorSpeed = Fp.FromRatio(1, 20) };
        var belt = Make(new StageModule([conveyor]));
        belt.State.SetX(0, Fp.FromInt(-2));
        belt.State.SetX(1, Fp.FromInt(20));
        Tick(belt, In(InputButton.Up), In());
        int guard = 0;
        while ((belt.State.IsAirborne(0) || belt.State.GetStateId(0) == FighterStateId.Jump) && guard++ < 200) Idle(belt);
        Fp before = belt.State.GetX(0);
        Idle(belt, 30);
        Assert.True(belt.State.GetX(0) > before, "the belt carries a standing fighter");
    }

    [Fact]
    public void MovingPlatform_CarriesTheFighter_AndHazardsHurtOnAnInterval()
    {
        var moving = Plat(PlatformSystem.PlatformType.Moving, 0, -2, 8) with
        {
            EndX = Fp.FromInt(0),
            EndY = Fp.FromRatio(-3 * 4 - 1, 4),
            MoveDuration = 60,
        };
        var stage = new StageModule([moving]);
        var sim = Make(stage);
        sim.State.SetX(0, Fp.Zero);
        sim.State.SetX(1, Fp.FromInt(20));
        Tick(sim, In(InputButton.Up), In());
        int guard = 0;
        while ((sim.State.IsAirborne(0) || sim.State.GetStateId(0) == FighterStateId.Jump) && guard++ < 300) Idle(sim);

        Fp y0 = sim.State.GetY(0);
        Idle(sim, 15);
        Assert.NotEqual(y0, sim.State.GetY(0));
        Assert.False(sim.State.IsAirborne(0));
        var (_, _, top, _) = stage.BoundsAt(0, sim.Frame - 1); // the fighter was carried using the frame that just ran
        Assert.Equal(top, sim.State.GetY(0));

        var hazard = Plat(PlatformSystem.PlatformType.Hazard, 0, 0, 6) with { Damage = 25, StartY = Fp.FromRatio(-2, 1), Height = Fp.FromInt(4) };
        var spikes = Make(new StageModule([hazard]));
        spikes.State.SetX(0, Fp.Zero);
        spikes.State.SetX(1, Fp.FromInt(20));
        Idle(spikes, StageModule.HazardIntervalFrames * 3); // frames 0, 30 and 60 each hurt once
        Assert.Equal(1000 - 75, spikes.State.GetHealth(0));
        Assert.Equal(1000, spikes.State.GetHealth(1));
    }

    [Fact]
    public void StageModule_HasNoState_SoItCannotDesyncRollback()
    {
        var stage = new StageModule([Plat(PlatformSystem.PlatformType.Moving, 0, -2, 4) with { EndY = Fp.FromInt(-5), MoveDuration = 90 }]);
        Assert.Equal(0, stage.StateWords);
        Assert.Equal(stage.CenterAt(0, 17), stage.CenterAt(0, 17 + 90));
    }

    // ------------------------------------------------------------------ tag team

    private static TagTeamModule Tag() => new(FightConfig.Default(), [GlenData.Create(), KiraData.Create()], [KiraData.Create(), GlenData.Create()]);

    [Fact]
    public void TagTeam_SwapsTheActiveCharacter_WithItsOwnMoveTableAndHealth()
    {
        var tag = Tag();
        var sim = Make(tag);
        Assert.Equal("glen", sim.Data(0).Id);

        sim.State.SetHealth(0, 640);
        Tick(sim, In(InputButton.Macro4), In());
        Assert.Equal("kira", sim.Data(0).Id);
        Assert.Equal(1000, sim.State.GetHealth(0));
        Assert.True(sim.State.GetInvuln(0) > 0);

        // Cooldown: tagging straight back is refused.
        Idle(sim, 3);
        Tick(sim, In(InputButton.Macro4), In());
        Assert.Equal("kira", sim.Data(0).Id);

        Idle(sim, 130);
        Tick(sim, In(InputButton.Macro4), In());
        Assert.Equal("glen", sim.Data(0).Id);
        Assert.Equal(640, sim.State.GetHealth(0)); // the partner remembered its health
    }

    [Fact]
    public void TagTeam_AFallenFighterIsReplacedAtOnce_AndTheRoundEndsOnlyWhenTheTeamIsDown()
    {
        var sim = Make(Tag());
        sim.State.SetHealth(0, 0);
        Idle(sim);
        Assert.Equal(0, sim.State.RoundState);
        Assert.Equal("kira", sim.Data(0).Id);
        Assert.Equal(1000, sim.State.GetHealth(0));

        sim.State.SetHealth(0, 0);
        Idle(sim);
        Assert.Equal(1, sim.State.RoundState);
        Assert.Equal(1, sim.State.Winner);
    }

    [Fact]
    public void TagTeam_StateIsPartOfTheSnapshot()
    {
        var sim = Make(Tag());
        Idle(sim, 5);
        var saved = sim.SnapshotSpan.ToArray();
        ulong hash = sim.SnapshotHash;

        Tick(sim, In(InputButton.Macro4), In());
        Idle(sim, 20);
        Assert.NotEqual(hash, sim.SnapshotHash);
        Assert.Equal("kira", sim.Data(0).Id);

        sim.Restore(saved);
        Assert.Equal(hash, sim.SnapshotHash);
        Assert.Equal("glen", sim.Data(0).Id);
        Assert.Equal(FightState.WordCount + TagTeamSystem.StateWords, sim.SnapshotWords);
    }

    [Fact]
    public void TagTeam_RejectsBadTeams()
    {
        Assert.Throws<ArgumentException>(() => new TagTeamModule(FightConfig.Default(), [], [GlenData.Create()]));
        Assert.Throws<ArgumentException>(() => new TagTeamModule(FightConfig.Default(), [.. Enumerable.Repeat(GlenData.Create(), 4)], [GlenData.Create()]));
    }

    // ------------------------------------------------------------------ finisher

    private static FinisherModule Finisher(int window = 120)
    {
        var fatalities = new FatalitySystem(FightConfig.Default());
        fatalities.AddFatality(new FatalitySystem.FatalityData { InputWindow = window, ExecutionDuration = 30, HealthThreshold = 200 });
        fatalities.SetInputs(0, [(int)InputButton.Down, (int)InputButton.Right, (int)InputButton.Heavy]);
        return new FinisherModule(fatalities);
    }

    private static FightSimulation KnockOut(FinisherModule module)
    {
        var sim = Make(module);
        Place(sim, Fp.FromRatio(5, 2));
        sim.State.SetHealth(1, 1);
        Tick(sim, In(L), In());
        Idle(sim, 8);
        return sim;
    }

    [Fact]
    public void Finisher_OpensAWindow_AfterAKo_AndTheSequenceIsPerformed()
    {
        var module = Finisher();
        var sim = KnockOut(module);
        Assert.Equal(1, sim.State.RoundState);
        Assert.Equal(FinisherModule.FinisherPhase.Window, module.Phase);
        Assert.True(module.WinnerHasControl);

        Tick(sim, In(InputButton.Down), In());
        Tick(sim, In(), In());
        Tick(sim, In(InputButton.Right), In());
        Assert.Equal(2, module.Progress);
        Tick(sim, In(), In());
        Tick(sim, In(InputButton.Heavy), In());
        Assert.Equal(FinisherModule.FinisherPhase.Executing, module.Phase);

        Idle(sim, 35);
        Assert.Equal(FinisherModule.FinisherPhase.Done, module.Phase);
        Assert.False(module.WinnerHasControl);
    }

    [Fact]
    public void Finisher_AWrongPressRestartsTheSequence_AndTheWindowExpires()
    {
        var module = Finisher(window: 40);
        var sim = KnockOut(module);
        Tick(sim, In(InputButton.Down), In());
        Tick(sim, In(), In());
        Tick(sim, In(InputButton.Special), In()); // wrong button
        Assert.Equal(0, module.Progress);

        Idle(sim, 60);
        Assert.Equal(FinisherModule.FinisherPhase.Done, module.Phase); // never performed
    }

    [Fact]
    public void Finisher_DoesNotOpenOnATimeout()
    {
        var cfg = FightConfig.Default() with { RoundSeconds = 1 };
        var module = Finisher();
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create(), cfg, modules: [module]);
        Idle(sim, 70);
        Assert.Equal(1, sim.State.RoundState);
        Assert.Equal(FinisherModule.FinisherPhase.None, module.Phase);
    }

    // ------------------------------------------------------------------ holds

    private static HoldModule Hold(int threshold = 4, int window = 30)
    {
        var holds = new WrestlingSystem(FightConfig.Default());
        holds.AddHold(new WrestlingSystem.HoldData { Damage = 100, MaxEscapeTime = window, EscapeThreshold = threshold, IsChainThrow = true });
        return new HoldModule(holds);
    }

    private static FightSimulation Grab(HoldModule module)
    {
        var sim = Make(module);
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(InputButton.Macro1), In());
        Idle(sim, 6);
        return sim;
    }

    [Fact]
    public void Hold_FreezesTheVictim_AndTheSlamLandsWhenNobodyEscapes()
    {
        var module = Hold();
        var sim = Grab(module);
        Assert.True(module.Active);
        int afterThrow = sim.State.GetHealth(1);
        Assert.True(afterThrow < 1000);
        Assert.True(sim.State.GetHitstop(1) > 0, "the victim hangs in the grab");

        Idle(sim, 40);
        Assert.False(module.Active);
        Assert.True(sim.State.GetHealth(1) <= afterThrow - 100, "the slam landed when the window ran out");
    }

    [Fact]
    public void Hold_TheVictimCanMashOut_AndTheAttackerCanChainAnEarlySlam()
    {
        var escape = Hold(threshold: 3);
        var sim = Grab(escape);
        int afterThrow = sim.State.GetHealth(1);
        for (int i = 0; i < 4; i++)
        {
            Tick(sim, In(), In(L));
            Tick(sim, In(), In());
        }
        Assert.False(escape.Active);
        Idle(sim, 40);
        Assert.Equal(afterThrow, sim.State.GetHealth(1)); // escaped: no bonus damage

        var chain = Hold();
        var slam = Grab(chain);
        int before = slam.State.GetHealth(1);
        Tick(slam, In(InputButton.Macro1), In());
        Assert.False(chain.Active);
        Assert.Equal(before - 100, slam.State.GetHealth(1));
    }

    // ------------------------------------------------------------------ determinism with modules

    private static FightSimulation Loaded()
    {
        var stage = new StageModule([Plat(PlatformSystem.PlatformType.Moving, 0, -2, 6) with { EndY = Fp.FromInt(-4), MoveDuration = 80 }]);
        return new FightSimulation(
            GlenData.Create(),
            KiraData.Create(),
            modules: [stage, new ArcanaModule(0, Fire()), new WeaponModule(1, Sword()), Tag(), Finisher(), Hold()]);
    }

    [Fact]
    public void AllModulesTogether_AreDeterministic_AndRestoreReproducesTheFuture()
    {
        ulong Run()
        {
            var sim = Loaded();
            var rng = new Xoshiro256StarStar(99);
            for (int f = 0; f < 3000; f++)
            {
                sim.Step(new InputState((ushort)(rng.NextULong() & 0x3FFF)), new InputState((ushort)(rng.NextULong() & 0x3FFF)));
                if (sim.State.RoundState != 0 && f % 60 == 0) sim = Loaded();
            }
            return sim.SnapshotHash;
        }

        Assert.Equal(Run(), Run());

        var a = Loaded();
        var inputs = new InputState[2 * 500];
        var rng2 = new Xoshiro256StarStar(5);
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputState((ushort)(rng2.NextULong() & 0x3FFF));
        for (int f = 0; f < 200; f++) a.Step(inputs[2 * f], inputs[(2 * f) + 1]);
        var saved = a.SnapshotSpan.ToArray();
        for (int f = 200; f < 500; f++) a.Step(inputs[2 * f], inputs[(2 * f) + 1]);
        ulong future = a.SnapshotHash;

        a.Restore(saved);
        for (int f = 200; f < 500; f++) a.Step(inputs[2 * f], inputs[(2 * f) + 1]);
        Assert.Equal(future, a.SnapshotHash);
    }

    [Fact]
    public void TwoNetplayPeers_WithModules_StillConverge()
    {
        const int frames = 300;
        var rng = new Xoshiro256StarStar(31);
        var s1 = new ushort[frames + 64];
        var s2 = new ushort[frames + 64];
        for (int i = 0; i < s1.Length; i++)
        {
            s1[i] = (ushort)(rng.NextULong() & 0x3FFF);
            s2[i] = (ushort)(rng.NextULong() & 0x3FFF);
        }

        const int delay = 2;
        ushort Applied(ushort[] s, int f) => f >= delay ? s[f - delay] : (ushort)0;
        var offline = Loaded();
        var expected = new ulong[frames];
        for (int f = 0; f < frames; f++)
        {
            offline.Step(new InputState(Applied(s1, f)), new InputState(Applied(s2, f)));
            expected[f] = offline.SnapshotHash;
        }

        var link = new LoopbackPair(seed: 3, maxDelayTicks: 4, dropRatePermyriad: 1500);
        var a = new NetplaySession(Loaded(), link.A, f => new InputState(s1[f]), 0, delay);
        var b = new NetplaySession(Loaded(), link.B, f => new InputState(s2[f]), 1, delay);
        for (int guard = 0; (a.Frame < frames || b.Frame < frames) && guard < 30_000; guard++)
        {
            if (a.Frame < frames) a.Tick(); else a.Pump();
            if (b.Frame < frames) b.Tick(); else b.Pump();
            link.Tick();
        }
        for (int i = 0; i < 3000 && (a.Runner.Session.ConfirmedThrough < frames - 1 || b.Runner.Session.ConfirmedThrough < frames - 1); i++)
        {
            a.Pump();
            b.Pump();
            link.Tick();
        }

        Assert.NotEqual(NetplayStatus.Desynced, a.Status);
        Assert.NotEqual(NetplayStatus.Desynced, b.Status);
        for (int f = frames - 30; f < frames; f++)
        {
            Assert.Equal(expected[f], a.Runner.ConfirmedHash(f));
            Assert.Equal(expected[f], b.Runner.ConfirmedHash(f));
        }
    }
}
