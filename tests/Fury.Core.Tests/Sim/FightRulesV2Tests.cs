using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

/// <summary>Rules added by the data-driven Sim v2: motions, buffering, cancels, hit levels, throws, projectiles, launchers, air moves, dashes.</summary>
public class FightRulesV2Tests
{
    private const InputButton L = InputButton.Light;
    private const InputButton M = InputButton.Medium;
    private const InputButton H = InputButton.Heavy;
    private const InputButton Sp = InputButton.Special;
    private const InputButton Grab = InputButton.Macro1;
    private const InputButton Fwd = InputButton.Right; // P1 faces right
    private const InputButton Back = InputButton.Left;
    private const InputButton Down = InputButton.Down;
    private const InputButton Up = InputButton.Up;

    private static InputState In(params InputButton[] buttons)
    {
        int bits = 0;
        foreach (var b in buttons) bits |= (int)b;
        return new InputState((ushort)bits);
    }

    private static FightSimulation Sim(FightConfig? cfg = null) => new(GlenData.Create(), KiraData.Create(), cfg);

    /// <summary>Places the fighters a given gap apart around the origin (centre to centre, in units).</summary>
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

    private static int Move(FightSimulation sim, int f) => sim.State.GetMoveIndex(f);

    private static void PerformQcf(FightSimulation sim, InputButton button, InputState? p2 = null)
    {
        var other = p2 ?? In();
        Tick(sim, In(Down), other);
        Tick(sim, In(Down, Fwd), other);
        Tick(sim, In(Fwd), other);
        Tick(sim, In(Fwd, button), other);
    }

    // ------------------------------------------------------------------ input system

    [Fact]
    public void Socd_OppositeDirectionsCancel_AndUpBeatsDown()
    {
        var buffer = new InputBuffer();
        buffer.Push(In(InputButton.Left, InputButton.Right));
        Assert.False(buffer.Held(InputButton.Left));
        Assert.False(buffer.Held(InputButton.Right));
        buffer.Push(In(Up, Down));
        Assert.True(buffer.Held(Up));
        Assert.False(buffer.Held(Down));
    }

    [Fact]
    public void PressAge_FindsBufferedEdges_AndIgnoresHeldButtons()
    {
        var buffer = new InputBuffer();
        buffer.Push(In());
        buffer.Push(In(L));
        buffer.Push(In(L));
        buffer.Push(In(L));
        Assert.Equal(2, buffer.PressAge(L, 4));   // the edge was two frames ago
        Assert.Equal(-1, buffer.PressAge(L, 2));  // outside a 2-frame window
        Assert.False(buffer.Pressed(L));          // held, not a fresh press
    }

    [Theory]
    [InlineData(MotionId.QuarterCircleForward, new[] { 2, 3, 6 })]
    [InlineData(MotionId.QuarterCircleBack, new[] { 2, 1, 4 })]
    [InlineData(MotionId.DragonPunch, new[] { 6, 2, 3 })]
    [InlineData(MotionId.HalfCircleForward, new[] { 4, 1, 2, 3, 6 })]
    [InlineData(MotionId.DoubleTapForward, new[] { 6, 5, 6 })]
    public void Motions_AreRecognised_FromNumpadSequences(MotionId motion, int[] numpad)
    {
        var buffer = new InputBuffer();
        foreach (int d in numpad) buffer.Push(FromNumpad(d));
        Assert.True(MotionRecognizer.Matches(motion, buffer, facing: 1));
        Assert.False(MotionRecognizer.Matches(motion, new InputBuffer(), facing: 1));
    }

    [Fact]
    public void Motions_AreFacingRelative()
    {
        var buffer = new InputBuffer();
        // Facing left: forward is the LEFT key. 2,3,6 in "forward" terms = Down, Down+Left, Left.
        buffer.Push(In(Down));
        buffer.Push(In(Down, InputButton.Left));
        buffer.Push(In(InputButton.Left));
        Assert.True(MotionRecognizer.Matches(MotionId.QuarterCircleForward, buffer, facing: -1));
        Assert.False(MotionRecognizer.Matches(MotionId.QuarterCircleForward, buffer, facing: 1));
    }

    [Fact]
    public void ChargeMotion_NeedsEnoughChargeFrames()
    {
        var shortCharge = new InputBuffer();
        for (int i = 0; i < MotionRecognizer.ChargeFrames - 6; i++) shortCharge.Push(In(Back));
        shortCharge.Push(In(Fwd));
        Assert.False(MotionRecognizer.Matches(MotionId.ChargeBackForward, shortCharge, 1));

        var longCharge = new InputBuffer();
        for (int i = 0; i < MotionRecognizer.ChargeFrames + 2; i++) longCharge.Push(In(Back));
        longCharge.Push(In(Fwd));
        Assert.True(MotionRecognizer.Matches(MotionId.ChargeBackForward, longCharge, 1));
    }

    private static InputState FromNumpad(int d) => d switch
    {
        1 => In(Down, Back),
        2 => In(Down),
        3 => In(Down, Fwd),
        4 => In(Back),
        6 => In(Fwd),
        7 => In(Up, Back),
        8 => In(Up),
        9 => In(Up, Fwd),
        _ => In(),
    };

    // ------------------------------------------------------------------ command resolution

    [Fact]
    public void QuarterCircleSpecial_BeatsThePlainSpecial_ByPriority()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(8));
        PerformQcf(sim, Sp);
        Assert.Equal(FighterStateId.Attack, sim.State.GetStateId(0));
        Assert.Equal(7, Move(sim, 0)); // GL_FIRE, not GL_ORD (slot 5)

        var plain = Sim();
        Place(plain, Fp.FromInt(8));
        Tick(plain, In(Sp), In());
        Assert.Equal(MoveSlot.Special, Move(plain, 0));
    }

    [Fact]
    public void CrouchAndStandNormals_AreChosenByStance()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(8));
        Tick(sim, In(Down, L), In());
        Assert.Equal(MoveSlot.CrouchLight, Move(sim, 0));

        var stand = Sim();
        Place(stand, Fp.FromInt(8));
        Tick(stand, In(M), In());
        Assert.Equal(MoveSlot.StandMedium, Move(stand, 0));
    }

    [Fact]
    public void SuperNeedsThreeBarsOfMeter()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(8));
        Tick(sim, In(InputButton.Macro2), In());
        Assert.NotEqual(MoveSlot.Super, Move(sim, 0));
        Assert.NotEqual(FighterStateId.Attack, sim.State.GetStateId(0));

        var charged = Sim();
        Place(charged, Fp.FromInt(8));
        charged.State.SetExMeter(0, 300);
        Tick(charged, In(InputButton.Macro2), In());
        Assert.Equal(MoveSlot.Super, Move(charged, 0));
        Assert.Equal(0, charged.State.GetExMeter(0));
    }

    [Fact]
    public void ABufferedPress_ComesOutWhenTheFighterBecomesFree_ButOnlyOnce()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(8));
        Tick(sim, In(L), In());                                  // light starts (14 frames total)
        Assert.Equal(FighterStateId.Attack, sim.State.GetStateId(0));
        int total = GlenData.Create().Moves.GetMove(MoveSlot.StandLight).TotalFrames;

        // Press medium 2 frames before the light ends: it must still be remembered.
        Idle(sim, total - 3);
        Tick(sim, In(M), In());
        Idle(sim, 1);
        Idle(sim, 4);
        Assert.Equal(MoveSlot.StandMedium, Move(sim, 0));
        Assert.Equal(FighterStateId.Attack, sim.State.GetStateId(0));

        // After the medium finishes, the same (consumed) press must not fire again.
        int mediumTotal = GlenData.Create().Moves.GetMove(MoveSlot.StandMedium).TotalFrames;
        Idle(sim, mediumTotal + 6);
        Assert.NotEqual(FighterStateId.Attack, sim.State.GetStateId(0));
    }

    // ------------------------------------------------------------------ movement

    [Fact]
    public void PlayerTwo_WalksTowardThePlayerItIsFacing_WhenHoldingForward()
    {
        // Regression: walking used world +X for "forward", so P2 (facing left) walked away from the key it held.
        var sim = Sim();
        Fp before = sim.State.GetX(1);
        Tick(sim, In(), In(InputButton.Left), 20); // P2 faces left: Left is forward
        Assert.True(sim.State.GetX(1) < before);
    }

    [Fact]
    public void WalkingBack_MovesAwayFromTheOpponent_ForBothSides()
    {
        var sim = Sim();
        Fp p1 = sim.State.GetX(0);
        Fp p2 = sim.State.GetX(1);
        Tick(sim, In(Back), In(InputButton.Right), 10);
        Assert.True(sim.State.GetX(0) < p1);
        Assert.True(sim.State.GetX(1) > p2);
    }

    [Fact]
    public void DoubleTapForward_StartsADash_ThatCoversMoreGroundThanWalking()
    {
        var dash = Sim();
        Tick(dash, In(Fwd), In());
        Tick(dash, In(), In());
        Tick(dash, In(Fwd), In());
        Assert.Equal(FighterStateId.Dash, dash.State.GetStateId(0));
        Fp start = dash.State.GetX(0);
        Idle(dash, 20);
        Fp dashed = dash.State.GetX(0) - start;

        var walk = Sim();
        Fp walkStart = walk.State.GetX(0);
        Tick(walk, In(Fwd), In(), 21);
        Fp walked = walk.State.GetX(0) - walkStart;
        Assert.True(dashed > walked, $"dash {dashed} should out-run walking {walked}");
    }

    [Fact]
    public void DoubleTapBack_IsABackdash_WithInvulnerabilityAtTheStart()
    {
        var sim = Sim();
        Tick(sim, In(Back), In());
        Tick(sim, In(), In());
        Tick(sim, In(Back), In());
        Assert.Equal(FighterStateId.Backdash, sim.State.GetStateId(0));
        Assert.True(sim.State.GetInvuln(0) > 0);
    }

    [Fact]
    public void Jumping_AndHoldingDown_DoesNotLeaveTheHurtboxCrouched()
    {
        var sim = Sim();
        Tick(sim, In(Up, Down), In()); // Up wins (SOCD); the crouch flag must be clear in the air
        Assert.True(sim.State.IsAirborne(0));
        Assert.False(sim.State.IsCrouching(0));
    }

    [Fact]
    public void Jump_ArcIsReasonable_ForAFourUnitFighter()
    {
        var sim = Sim();
        Tick(sim, In(Up), In());
        Fp apex = Fp.Zero;
        int airFrames = 1;
        while (sim.State.IsAirborne(0) && airFrames < 200)
        {
            Idle(sim);
            apex = Fp.Min(apex, sim.State.GetY(0));
            airFrames++;
        }
        Assert.InRange(-apex.ToFloat(), 3f, 12f);   // 0.75 to 3 fighter heights
        Assert.InRange(airFrames, 20, 70);          // 0.33 to 1.2 seconds
    }

    // ------------------------------------------------------------------ hits, blocking, levels

    [Fact]
    public void HitSlidesTheDefenderAway_AndNeverThroughTheWall()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        Fp before = sim.State.GetX(1);
        Tick(sim, In(H), In());
        Idle(sim, 25);
        Assert.True(sim.State.GetHealth(1) < 1000);
        Assert.True(sim.State.GetX(1) > before, "pushback must slide a grounded defender (it only ever moved airborne ones)");

        var cornered = Sim();
        cornered.State.SetX(0, Fp.FromRatio(20, 1));
        cornered.State.SetX(1, Fp.FromRatio(22, 1) + Fp.FromRatio(1, 2));
        Tick(cornered, In(H), In());
        Idle(cornered, 25);
        Assert.True(cornered.State.GetX(1) <= cornered.Config.StageHalfWidth - cornered.Config.PushboxHalfWidth);
        Assert.True(cornered.State.GetX(0) < Fp.FromRatio(20, 1), "a cornered defender pushes the attacker back instead");
    }

    [Fact]
    public void LowAttack_IsBlockedCrouching_ButHitsAStandingGuard()
    {
        // GL_CL (crouching light) is a Low.
        int Damage(bool defenderCrouches)
        {
            var sim = Sim();
            Place(sim, Fp.FromRatio(5, 2));
            var guard = defenderCrouches ? In(InputButton.Right, Down) : In(InputButton.Right);
            Tick(sim, In(Down, L), guard, 2);
            Tick(sim, In(Down), guard, 12);
            return 1000 - sim.State.GetHealth(1);
        }

        Assert.True(Damage(defenderCrouches: false) > 10, "standing guard does not stop a low");
        Assert.True(Damage(defenderCrouches: true) <= 4, "crouching guard stops a low (chip only)");
    }

    [Fact]
    public void OverheadAttack_IsBlockedStanding_ButHitsACrouchingGuard()
    {
        int Damage(bool defenderCrouches)
        {
            var sim = Sim();
            Place(sim, Fp.FromRatio(5, 2));
            var guard = defenderCrouches ? In(InputButton.Right, Down) : In(InputButton.Right);
            // 6+heavy: GL_OVH (overhead command normal).
            Tick(sim, In(Fwd, H), guard, 1);
            Tick(sim, In(Fwd), guard, 26);
            return 1000 - sim.State.GetHealth(1);
        }

        Assert.Equal(HitLevel.Overhead, GlenData.Create().Moves.GetMove(13).Level);
        Assert.True(Damage(defenderCrouches: true) > 10);
        Assert.True(Damage(defenderCrouches: false) <= 8);
    }

    [Fact]
    public void CommandNormal_ForwardHeavy_IsChosenOverTheStandingHeavy()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(8));
        Tick(sim, In(Fwd, H), In());
        Assert.Equal(13, Move(sim, 0));
    }

    [Fact]
    public void ComboCounter_SurvivesChainedMoves_AndResetsWhenTheDefenderRecovers()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(L), In());
        Idle(sim, 6);
        Assert.Equal(1, sim.State.GetComboCount(0));
        int firstHit = 1000 - sim.State.GetHealth(1);

        // Cancel the connected light into a medium (light normals chain into other normals).
        Tick(sim, In(M), In());
        Idle(sim, 12);
        Assert.Equal(2, sim.State.GetComboCount(0));
        int secondHit = 1000 - sim.State.GetHealth(1) - firstHit;
        Assert.True(secondHit < 70, "second hit is scaled by the combo");

        Idle(sim, 90);
        Assert.Equal(0, sim.State.GetComboCount(0));
    }

    [Fact]
    public void CancelIntoASpecial_RequiresTheNormalToHaveConnected()
    {
        // Connected: light -> fireball cancel.
        var hit = Sim();
        Place(hit, Fp.FromRatio(5, 2));
        Tick(hit, In(L), In());
        Idle(hit, 6); // light hit lands during these frames
        Assert.Equal(1, hit.State.GetConnect(0));
        PerformQcf(hit, Sp);
        Assert.Equal(7, Move(hit, 0));

        // Whiffed: no cancel, the fireball cannot interrupt the light.
        var whiff = Sim();
        Place(whiff, Fp.FromInt(12));
        Tick(whiff, In(L), In());
        Idle(whiff, 6);
        PerformQcf(whiff, Sp);
        Assert.Equal(MoveSlot.StandLight, Move(whiff, 0));
    }

    // ------------------------------------------------------------------ throws

    [Fact]
    public void Throw_IgnoresBlocking_DealsDamage_AndKnocksDown()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        var guard = In(InputButton.Right);
        Tick(sim, In(Grab), guard);
        Idle(sim, 8);
        Assert.True(sim.State.GetHealth(1) < 1000, "a throw cannot be blocked");
        Assert.True(sim.State.IsAirborne(1) || sim.State.GetStateId(1) is FighterStateId.Hitstun or FighterStateId.Knockdown);
    }

    [Fact]
    public void Throw_WhiffsOutOfRange()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(7));
        Tick(sim, In(Grab), In());
        Idle(sim, 40);
        Assert.Equal(1000, sim.State.GetHealth(1));
    }

    [Fact]
    public void SimultaneousThrows_TechEachOther_WithoutDamage()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(Grab), In(Grab));
        Idle(sim, 12);
        Assert.Equal(1000, sim.State.GetHealth(0));
        Assert.Equal(1000, sim.State.GetHealth(1));
    }

    [Fact]
    public void AirborneDefenders_CannotBeThrown()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(Grab), In(Up));
        Idle(sim, 10);
        Assert.Equal(1000, sim.State.GetHealth(1));
    }

    // ------------------------------------------------------------------ projectiles

    [Fact]
    public void Fireball_SpawnsTravelsHitsAndDespawns()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(10));
        PerformQcf(sim, Sp);
        Idle(sim, 20);
        Assert.Equal(1, sim.State.ProjectileCount(0));

        Fp x0 = sim.State.ProjectileX(FirstProjectile(sim));
        Idle(sim, 5);
        Assert.True(sim.State.ProjectileX(FirstProjectile(sim)) > x0, "the fireball travels forward");

        Idle(sim, 60);
        Assert.True(sim.State.GetHealth(1) < 1000, "the fireball connected");
        Assert.Equal(0, sim.State.ProjectileCount(0));
    }

    [Fact]
    public void Fireball_IsBlockedForChipDamage()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(10));
        PerformQcf(sim, Sp, p2: In(InputButton.Right));
        Tick(sim, In(), In(InputButton.Right), 80);
        Assert.InRange(1000 - sim.State.GetHealth(1), 1, 8);
    }

    [Fact]
    public void Fireballs_AreLimitedPerFighter()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(40));
        for (int i = 0; i < 6; i++)
        {
            PerformQcf(sim, Sp);
            Idle(sim, 40);
        }
        Assert.True(sim.State.ProjectileCount(0) <= sim.Config.MaxProjectilesPerFighter);
    }

    private static int FirstProjectile(FightSimulation sim)
    {
        for (int s = 0; s < FightState.ProjectileSlots; s++)
        {
            if (sim.State.ProjectileActive(s)) return s;
        }
        throw new InvalidOperationException("No active projectile.");
    }

    // ------------------------------------------------------------------ launcher / air

    [Fact]
    public void DragonPunch_Launches_TheDefenderLandsInAKnockdown_ThenWakesUpInvulnerable()
    {
        var sim = Sim();
        Place(sim, Fp.FromRatio(5, 2));
        Tick(sim, In(Fwd), In());
        Tick(sim, In(Down), In());
        Tick(sim, In(Down, Fwd), In());
        Tick(sim, In(Fwd, H), In());
        Idle(sim, 8);
        Assert.Equal(8, Move(sim, 0));
        Assert.True(sim.State.IsAirborne(1), "launcher sends the defender up");
        Assert.Equal(FighterStateId.Hitstun, sim.State.GetStateId(1));

        int guard = 0;
        while (sim.State.IsAirborne(1) && guard++ < 200) Idle(sim);
        Assert.Equal(FighterStateId.Knockdown, sim.State.GetStateId(1));

        int stuck = 0;
        while (sim.State.GetStateId(1) == FighterStateId.Knockdown && stuck++ < 200) Idle(sim);
        Assert.Equal(FighterStateId.Idle, sim.State.GetStateId(1));
        Assert.True(sim.State.GetInvuln(1) > 0, "wake-up invulnerability");
    }

    [Fact]
    public void JumpAttack_StartsFromTheAir_AndTheFighterLandsNormally()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(14));
        Tick(sim, In(Up), In());
        Idle(sim, 4);
        Assert.True(sim.State.IsAirborne(0));
        Tick(sim, In(H), In());
        Assert.Equal(FighterStateId.Attack, sim.State.GetStateId(0));
        Assert.Equal(11, Move(sim, 0)); // GL_JH
        Assert.True(sim.State.IsAirborne(0));

        int guard = 0;
        while ((sim.State.IsAirborne(0) || sim.State.GetStateId(0) != FighterStateId.Idle) && guard++ < 300) Idle(sim);
        Assert.Equal(FighterStateId.Idle, sim.State.GetStateId(0));
        Assert.Equal(Fp.Zero, sim.State.GetY(0));
    }

    [Fact]
    public void AirMoves_CannotBeStartedOnTheGround_AndGroundMovesNotInTheAir()
    {
        var ground = Sim();
        Place(ground, Fp.FromInt(14));
        Tick(ground, In(H), In());
        Assert.Equal(MoveSlot.StandHeavy, Move(ground, 0));

        var air = Sim();
        Place(air, Fp.FromInt(14));
        Tick(air, In(Up), In());
        Idle(air, 3);
        Tick(air, In(Sp), In()); // the neutral special is a ground move
        Assert.NotEqual(FighterStateId.Attack, air.State.GetStateId(0));
    }

    // ------------------------------------------------------------------ determinism with the new state

    [Fact]
    public void Restore_MidFireballAndMidCombo_ReproducesTheSameFuture()
    {
        var sim = Sim();
        Place(sim, Fp.FromInt(9));
        PerformQcf(sim, Sp);
        Idle(sim, 14);
        Assert.True(sim.State.ProjectileCount(0) > 0);

        var saved = sim.SnapshotSpan.ToArray();
        ulong savedHash = sim.SnapshotHash;
        Idle(sim, 70);
        ulong later = sim.SnapshotHash;

        sim.Restore(saved);
        Assert.Equal(savedHash, sim.SnapshotHash);
        Idle(sim, 70);
        Assert.Equal(later, sim.SnapshotHash);
    }

    [Fact]
    public void RandomMashing_NeverBreaksInvariants_OrThrows()
    {
        var sim = Sim();
        var rng = new Xoshiro256StarStar(2026);
        Fp limit = sim.Config.StageHalfWidth;
        for (int f = 0; f < 20_000; f++)
        {
            sim.Step(new InputState((ushort)(rng.NextULong() & 0x0FFF)), new InputState((ushort)(rng.NextULong() & 0x0FFF)));
            for (int p = 0; p < 2; p++)
            {
                Assert.InRange(sim.State.GetHealth(p), 0, 1000);
                Assert.InRange(sim.State.GetExMeter(p), 0, 300);
                Assert.True(Fp.Abs(sim.State.GetX(p)) <= limit);
                Assert.True(sim.State.GetY(p) <= Fp.Zero, "never below the floor");
            }
            if (sim.State.RoundState != 0) sim = Sim(); // new round
        }
    }

    [Fact]
    public void NoHeapAllocations_WhileFightingWithMovesAndProjectiles()
    {
        var sim = Sim();
        var rng = new Xoshiro256StarStar(7);
        for (int i = 0; i < 600; i++) sim.Step(new InputState((ushort)(rng.NextULong() & 0x0FFF)), new InputState((ushort)(rng.NextULong() & 0x0FFF)));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3000; i++)
        {
            sim.Step(new InputState((ushort)(rng.NextULong() & 0x0FFF)), new InputState((ushort)(rng.NextULong() & 0x0FFF)));
            sim.State.SetHealth(0, 1000); // keep the round alive so every system keeps running
            sim.State.SetHealth(1, 1000);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(after - before < 3000L * 8, $"allocated {after - before} bytes");
    }
}
