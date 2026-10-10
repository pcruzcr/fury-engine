// Fighter.Core — combat resolution (§22). Pure outcome computation on (attacker, defender).
// Damage order (documented, tested): scale(combo) → rage mods → resonance ×4/5 → counter ×6/5.
// Chip (OPEN-001): 10% of that final damage; resonance reduces chip like any damage;
// chip feeds dealer meter only ((chip/5)*30, usually 0), never victim comeback (F1 rule, flagged).
// Blockstun (ADR-002): attackerFramesRemaining + onBlock, min 1.
namespace Fighter.Core;

/// <summary>Combat resolution (§22): pure outcome computation on (attacker, defender).
/// Priority, counter, block, parry, armor, reflect, throws, clash — all integer, all tested.</summary>
public static class CombatResolution
{
    /// <summary>ANY hp reduction resets Kira stacks (GDD §3.3: "al recibir cualquier daño").</summary>
    private static void TouchDamage(Fighter f) => f.AccelStacks = 0;

    /// <summary>Resolves a strike: invuln → counter-stance → parry → block → hit.
    /// Throws bypass to <see cref="ResolveThrow"/>.</summary>
    public static void ResolveStrike(Fighter att, Fighter def, MoveData move,
        int attIdx, InputState defInput,
        int frame, System.Collections.Generic.List<SimEvent> sim,
        System.Collections.Generic.List<PresEvent> pres)
    {
        // 1. Invulnerability wins over everything (§4.5 L1).
        if (def.InvulnT > 0)
        {
            sim.Add(new SimEvent(frame, SimEventType.Miss, attIdx, 1 - attIdx));
            return;
        }

        // 2. Counter stance (Glen SP.3): reflect ×1.5 of incoming BASE, end stance. Throws bypass.
        if (def.State == FighterState.Attack && def.CounterStanceT > 0 && move.Kind != MoveKind.Throw)
        {
            int reflected = CombatMath.Reflect(move.Damage);
            att.Hp -= reflected;
            TouchDamage(att);
            att.Enter(FighterState.Hitstun, Calibration.HitstunMedium);
            att.ComboGiven = 0; // countered attack builds nothing
            def.CounterStanceT = 0;
            def.Enter(FighterState.Neutral);
            EnergySystem.ResetBlockString(att);
            EnergySystem.ResetBlockString(def);
            int hs = HitstopTable.For(move);
            att.Hitstop = hs; def.Hitstop = hs;
            sim.Add(new SimEvent(frame, SimEventType.Reflected, 1 - attIdx, attIdx, reflected, hs));
            pres.Add(new PresEvent(frame, PresKind.ParryFlash, def.Pos.X, def.Pos.Y, move.Level));
            CheckKO(att, def, attIdx, frame, sim, pres);
            return;
        }

        // 3. Parry (throws are not parryable; supers are — documented F1 rule).
        if (move.Kind != MoveKind.Throw && (def.ParryStanceT > 0 || def.ParryGraceT > 0))
        {
            att.Enter(FighterState.ParryStun, Calibration.ParryStunAttacker);
            att.DidHit = true;
            att.NoCancel = true; // parried: no cancel (documented F1 rule)
            def.ParryStanceT = 0;
            def.ParryGraceT = 0;
            EnergySystem.OnParrySuccess(def);
            EnergySystem.ResetBlockString(att);
            EnergySystem.ResetBlockString(def);
            sim.Add(new SimEvent(frame, SimEventType.Parry, 1 - attIdx, attIdx));
            pres.Add(new PresEvent(frame, PresKind.ParryFlash, def.Pos.X, def.Pos.Y, move.Level));
            return; // GDD: parry = flash, no standard hitstop
        }

        bool counter = def.State == FighterState.Attack && def.MoveFrame < def.StartupEff;

        // 4. Block?
        if (FighterStateMeta.CanBlock(def.State) && IsCorrectBlock(defInput, def.Facing, move))
        {
            int scaled = FinalDamage(att, def, move, applyCounter: false);
            int chip = CombatMath.Chip(scaled); // OPEN-001: 10% of that final damage
            def.Hp -= chip;
            TouchDamage(def);
            int remaining = (att.StartupEff + att.ActiveEff + att.RecoveryEff) - att.MoveFrame;
            int blockstun = System.Math.Max(1, remaining + move.OnBlock);
            def.Enter(FighterState.Blockstun, blockstun);
            att.DidHit = true;
            Push(att, def, move.Level, def.ResActive);
            EnergySystem.OnBlocked(def);
            EnergySystem.ResetBlockString(att);
            EnergySystem.OnDamageDealt(att, chip); // dealer trickle only
            int hs = HitstopTable.For(move);
            att.Hitstop = hs; def.Hitstop = hs;
            sim.Add(new SimEvent(frame, SimEventType.Block, attIdx, 1 - attIdx, chip, hs));
            pres.Add(new PresEvent(frame, PresKind.BlockSpark, def.Pos.X, def.Pos.Y, move.Level));
            CheckKO(att, def, attIdx, frame, sim, pres);
            return;
        }

        // 5. Clean hit (or parry-whiff punish ×1.2).
        int final = FinalDamage(att, def, move, applyCounter: counter);
        bool whiffPunish = def.State == FighterState.ParryWhiff;
        if (whiffPunish) final = CombatMath.ParryFail(final); // GDD §4.6 parry-fail case
        bool armored = def.RageActive && def.Affinity == RageAffinity.Danimal
            && def.State == FighterState.Attack && !def.ArmorUsed;

        def.Hp -= final;
        TouchDamage(def);
        att.DidHit = true;
        att.ComboGiven++;
        EnergySystem.OnDamageTaken(def, final);
        EnergySystem.OnDamageDealt(att, final, move.Kind);
        EnergySystem.ResetBlockString(att);
        EnergySystem.ResetBlockString(def);

        int hitstop = HitstopTable.For(move) + (counter ? 2 : 0);
        att.Hitstop = hitstop; def.Hitstop = hitstop;

        if (!armored)
            ApplyVictimState(att, def, move);
        else
            def.ArmorUsed = true; // damage sticks, no interruption

        Push(att, def, move.Level, def.ResActive);
        sim.Add(new SimEvent(frame,
            counter ? SimEventType.CounterHit : SimEventType.Hit,
            attIdx, 1 - attIdx, final, hitstop, counter));
        pres.Add(new PresEvent(frame, PresKind.HitSpark, def.Pos.X, def.Pos.Y, move.Level));
        CheckKO(att, def, attIdx, frame, sim, pres);
    }

    private static int FinalDamage(Fighter att, Fighter def, MoveData move, bool applyCounter)
    {
        bool isSuper = move.Kind == MoveKind.Super;
        int v = DamageScaler.Apply(move.Damage, att.ComboGiven, isSuper);
        if ((move.Flags & MoveFlags.Physical) != 0)
            v = v * EnergySystem.PhysicalMult(att) / EnergySystem.PhysicalDen(att);
        else if ((move.Flags & MoveFlags.Energy) != 0)
            v = v * EnergySystem.EnergyMultNum(att) / EnergySystem.EnergyMultDen(att);
        if (def.ResActive) v = CombatMath.ResonanceTaken(v); // −20% (OPEN-003: applies to chip too)
        if (applyCounter) v = CombatMath.CounterHit(v); // +20% counter
        return v;
    }

    private static bool IsCorrectBlock(InputState input, int defFacing, MoveData move)
    {
        // Holding away = pressing opposite of facing.
        if (input.DirX != -defFacing) return false;
        bool lowHeld = input.DirY == -1;
        bool highHeld = !lowHeld;
        bool isLow = (move.Flags & MoveFlags.Low) != 0;
        bool isOverhead = (move.Flags & MoveFlags.Overhead) != 0;
        if (isLow) return lowHeld;
        if (isOverhead) return highHeld;
        return true; // mid: either
    }

    private static void ApplyVictimState(Fighter att, Fighter def, MoveData move)
    {
        // Juggle continuity (F1 refinement, documented): EVERY airborne hit counts toward the
        // juggle cap, stunned or not; the combo itself never resets while the victim is airborne
        // (see ResetCombosWhereStanding). Launchers reset the count as juggle starters.
        if (def.Airborne)
        {
            def.JuggleCount++;
            if (def.JuggleCount > def.JuggleMax)
            {
                def.PendingKnockdown = true;
                def.Vel = new Vector2I(def.Vel.X, Calibration.JuggleSlamVelY);
            }
        }
        if ((move.Flags & MoveFlags.Launcher) != 0)
        {
            def.Airborne = true;
            def.JuggleCount = 0;
            int dir = def.Pos.X >= att.Pos.X ? 1 : -1;
            def.Vel = new Vector2I(dir * Calibration.LauncherVelX, Calibration.LauncherVelY);
            def.Enter(FighterState.Hitstun, Calibration.HitstunHeavy);
        }
        else if ((move.Flags & MoveFlags.Knockdown) != 0)
        {
            def.Airborne = false;
            def.Vel = Vector2I.Zero;
            def.Enter(FighterState.Knockdown, Calibration.KnockdownDuration);
        }
        else if (move.Level == AttackLevel.Heavy)
        {
            def.PendingKnockdown = true; // 19f stun, then down (GDD §4.4)
            def.Enter(FighterState.Hitstun, Calibration.HitstunHeavy);
        }
        else if (move.Level == AttackLevel.Medium || move.Level == AttackLevel.Special)
        {
            def.Enter(FighterState.Hitstun, Calibration.HitstunMedium);
        }
        else
        {
            def.Enter(FighterState.Hitstun, Calibration.HitstunLight);
        }
    }

    private static void Push(Fighter att, Fighter def, AttackLevel level, bool resActive)
    {
        int dir = def.Pos.X >= att.Pos.X ? 1 : -1;
        int pa = level switch
        {
            AttackLevel.Light => Calibration.PushAttackerL,
            AttackLevel.Medium or AttackLevel.Special => Calibration.PushAttackerM,
            _ => Calibration.PushAttackerH,
        };
        int pd = level switch
        {
            AttackLevel.Light => Calibration.PushDefenderL,
            AttackLevel.Medium or AttackLevel.Special => Calibration.PushDefenderM,
            _ => Calibration.PushDefenderH,
        };
        if (resActive) pd = CombatMath.ResonancePushback(pd); // +20% pushback on block/hit (OPEN-003)
        att.Pos = new Vector2I(att.Pos.X - dir * pa, att.Pos.Y);
        def.Pos = new Vector2I(def.Pos.X + dir * pd, def.Pos.Y);
        att.Pos = BodyCollision.ClampStage(att.Pos);
        def.Pos = BodyCollision.ClampStage(def.Pos);
    }

    /// <summary>Resolves a throw: invuln/whiff/tech checks, then damage + knockdown.</summary>
    public static void ResolveThrow(Fighter att, Fighter def, MoveData move, int attIdx,
        RingBuffer<InputState> defHist, bool commandGrab,
        int frame, System.Collections.Generic.List<SimEvent> sim,
        System.Collections.Generic.List<PresEvent> pres)
    {
        if (def.InvulnT > 0)
        {
            sim.Add(new SimEvent(frame, SimEventType.Miss, attIdx, 1 - attIdx));
            return;
        }
        if (def.Airborne || att.Airborne)
        {
            // F1 rule (documented): no air throws. Grounded fighters only.
            sim.Add(new SimEvent(frame, SimEventType.Miss, attIdx, 1 - attIdx));
            return;
        }
        int edgeDist = System.Math.Abs(att.Pos.X - def.Pos.X)
            - Calibration.FighterHalfWidth * 2;
        if (edgeDist > Calibration.ThrowRange)
        {
            sim.Add(new SimEvent(frame, SimEventType.Miss, attIdx, 1 - attIdx)); // whiff: full recovery
            return;
        }
        // Tech: LP+LK simultaneously pressed within the last TechWindow frames (normal throws only).
        if (!commandGrab && HasTech(defHist))
        {
            int dir = def.Pos.X >= att.Pos.X ? 1 : -1;
            att.Pos = BodyCollision.ClampStage(new Vector2I(att.Pos.X - dir * Calibration.TechSeparation / 2, att.Pos.Y));
            def.Pos = BodyCollision.ClampStage(new Vector2I(def.Pos.X + dir * Calibration.TechSeparation / 2, def.Pos.Y));
            att.Enter(FighterState.Neutral);
            def.Enter(FighterState.Neutral);
            EnergySystem.OnTech(def);
            sim.Add(new SimEvent(frame, SimEventType.Tech, 1 - attIdx, attIdx));
            pres.Add(new PresEvent(frame, PresKind.ThrowSpark, def.Pos.X, def.Pos.Y, AttackLevel.Medium));
            return;
        }
        int final = DamageScaler.Apply(move.Damage, att.ComboGiven, false);
        if (def.ResActive) final = CombatMath.ResonanceTaken(final);
        def.Hp -= final;
        TouchDamage(def);
        att.DidHit = true;
        att.ComboGiven++;
        EnergySystem.OnDamageTaken(def, final);
        EnergySystem.OnDamageDealt(att, final, move.Kind);
        EnergySystem.ResetBlockString(att);
        EnergySystem.ResetBlockString(def);
        def.Airborne = false;
        def.Vel = Vector2I.Zero;
        def.Enter(FighterState.Knockdown, Calibration.KnockdownDuration); // F1 rule, documented
        int hs = 6;
        att.Hitstop = hs; def.Hitstop = hs;
        sim.Add(new SimEvent(frame, SimEventType.Throw, attIdx, 1 - attIdx, final, hs));
        pres.Add(new PresEvent(frame, PresKind.ThrowSpark, def.Pos.X, def.Pos.Y, AttackLevel.Heavy));
        CheckKO(att, def, attIdx, frame, sim, pres);
    }

    private static bool HasTech(RingBuffer<InputState> hist)
    {
        int n = System.Math.Min(hist.Count, Calibration.TechWindow);
        for (int o = 0; o < n; o++)
        {
            var s = hist.GetLatest(o);
            if ((s.Buttons & (Buttons.LP | Buttons.LK)) == (Buttons.LP | Buttons.LK)) return true;
        }
        return false;
    }

    /// <summary>Applies a mutual clash rebound (no damage).</summary>
    public static void ApplyClash(Fighter a, Fighter b, int duration,
        int frame, System.Collections.Generic.List<SimEvent> sim,
        System.Collections.Generic.List<PresEvent> pres)
    {
        a.DidHit = true; b.DidHit = true;
        a.Enter(FighterState.ClashStun, duration);
        b.Enter(FighterState.ClashStun, duration);
        sim.Add(new SimEvent(frame, SimEventType.Clash, 0, 1, 0, duration));
        int midX = (a.Pos.X + b.Pos.X) / 2;
        pres.Add(new PresEvent(frame, PresKind.ClashSpark, midX, a.Pos.Y, AttackLevel.Medium));
    }

    private static void CheckKO(Fighter att, Fighter def, int attIdx, int frame,
        System.Collections.Generic.List<SimEvent> sim, System.Collections.Generic.List<PresEvent> pres)
    {
        if (def.Hp > 0) return;
        def.Hp = 0;
        def.Enter(FighterState.KO);
        sim.Add(new SimEvent(frame, SimEventType.KO, attIdx, 1 - attIdx));
        pres.Add(new PresEvent(frame, PresKind.KoSlowmo, def.Pos.X, def.Pos.Y, AttackLevel.Heavy));
    }
}
