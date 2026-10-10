// Fighter.Core — energy: EX + Resonance + Rage (GDD §5, OPEN-001..005 resolved).
// Integer EX units: 1% = 30 (SimulationUnits.ExMax = 3000).
namespace Fighter.Core;

/// <summary>EX + Resonance + Rage economy (GDD §5; OPEN-001..005 resolved).
/// Integer EX units: 1% = 30 (<see cref="SimulationUnits.ExMax"/> = 3000).</summary>
public static class EnergySystem
{
    /// <summary>EX units per percent (100% = 3000).</summary>
    public const int Pct = 30;
    /// <summary>Auto-Rage threshold: full bar consumed (GDD §5).</summary>
    public const int RageAutoThreshold = 3000;
    /// <summary>Post-KO grace for the round loser: 30% EX (GDD §5.3).</summary>
    public const int LoserGraceEx = 900;

    // Rage timings, frames (GDD §5.2).
    /// <summary>Danimal rage duration 5s.</summary>
    public const int DanimalRageDur = 300;
    /// <summary>Danimal post-rage cooldown 8s.</summary>
    public const int DanimalRageCd = 480;
    /// <summary>Dant rage duration 6s.</summary>
    public const int DantRageDur = 360;
    /// <summary>Dant post-rage cooldown 10s.</summary>
    public const int DantRageCd = 600;
    /// <summary>Gray rage duration 7s.</summary>
    public const int GrayRageDur = 420;
    /// <summary>Gray post-rage cooldown 15s.</summary>
    public const int GrayRageCd = 900;
    /// <summary>Resonance duration 3s (GDD §5.3).</summary>
    public const int ResDur = 180;
    /// <summary>Resonance cooldown 8s from activation (OPEN-003).</summary>
    public const int ResCd = 480;

    /// <summary>Positive generation gate: rage (active OR cooldown, OPEN-002/GDD §5.1) → ×0;
    /// resonance active → ×1/2 (integer floor). Penalties bypass this gate (documented F1 reading).</summary>
    public static int GateGain(Fighter f, int amount)
    {
        if (f.RageActive || f.RageCd > 0) return 0;
        if (f.ResActive) return amount / 2;
        return amount;
    }

    /// <summary>Adds a positive gain through the gate and the stage zone (clamped to max).</summary>
    public static void AddGain(Fighter f, int amount) =>
        f.Ex = System.Math.Min(SimulationUnits.ExMax,
            f.Ex + GateGain(f, amount) * f.ExZonePerMille / 1000);

    /// <summary>Called with ComboTaken still holding the PRE-hit total; updates it here.</summary>
    public static void OnDamageTaken(Fighter victim, int finalDamage)
    {
        int cap = CombatMath.SnowballCap(victim.MaxHp); // OPEN-005
        int before = victim.ComboTaken;
        int fullPart = System.Math.Min(finalDamage, System.Math.Max(0, cap - before));
        int halfPart = finalDamage - fullPart;
        victim.ComboTaken = before + finalDamage;
        AddGain(victim, fullPart / 3 * Pct + (halfPart / 3 * Pct) / 2);
        // Dealer side handled by caller via OnDamageDealt (needs attacker ref).
    }

    /// <summary>Dealer-side economy: +1% per 5 HP, +10% once on the 5th hit,
    /// Kira Acceleration stacking for connected specials.</summary>
    public static void OnDamageDealt(Fighter attacker, int finalDamage, MoveKind kind = MoveKind.Normal)
    {
        AddGain(attacker, finalDamage / 5 * Pct); // +1% per 5 HP
        if (attacker.ComboGiven == 5) AddGain(attacker, 10 * Pct); // combo 5+ bonus, once
        // Kira Acceleration (GDD §3.3): each CONNECTED special +1 stack (max 2 → ×3).
        // Chip/throws never stack (kind passed by the call site; default Normal is inert).
        if (attacker.Gimmick == GimmickId.KiraAcceleration && kind == MoveKind.Special)
            attacker.AccelStacks = System.Math.Min(2, attacker.AccelStacks + 1);
    }

    /// <summary>Block-string penalty: from the 3rd consecutive blocked attack on, −5% (F1 rule,
    /// documented: GDD "bloquear 3+ seguidos −5%"). Penalty is never halved.</summary>
    public static void OnBlocked(Fighter victim)
    {
        victim.ConsecBlocked++;
        if (victim.ConsecBlocked >= 3)
            victim.Ex = System.Math.Max(0, victim.Ex - 5 * Pct);
    }

    /// <summary>Clears a fighter's consecutive-block counter (any hit/attack/parry breaks the string).</summary>
    public static void ResetBlockString(Fighter f) => f.ConsecBlocked = 0;

    /// <summary>Throw-tech reward: +5% EX to the tech-er (GDD §5.1).</summary>
    public static void OnTech(Fighter f) => AddGain(f, 5 * Pct);
    /// <summary>Parry reward: +15% EX and +10% Resonance (GDD §1.2: the dual-feed node).</summary>
    public static void OnParrySuccess(Fighter f) { AddGain(f, 15 * Pct); f.Res = System.Math.Min(1000, f.Res + 100); }

    /// <summary>Spends meter (throws on insufficient funds; supers pre-gate).</summary>
    public static void Spend(Fighter f, int amount)
    {
        if (amount < 0 || f.Ex < amount) throw new InvalidOperationException("Insufficient EX.");
        f.Ex -= amount;
    }

    // ---- Rage ----
    /// <summary>Auto-fires a full bar into affinity Rage (consumes 3000; manual Burst excluded).</summary>
    public static void MaybeAutoRage(Fighter f, int frame,
        System.Collections.Generic.List<SimEvent> sim, System.Collections.Generic.List<PresEvent> pres, int idx)
    {
        if (f.RageActive || f.RageCd > 0 || f.Ex < RageAutoThreshold) return;
        f.Ex -= RageAutoThreshold;
        f.RageActive = true;
        f.RageStage = 1;
        f.FirstParryAvailable = f.Affinity == RageAffinity.Dant; // OPEN-004 arming
        f.RageTimer = f.Affinity switch
        {
            RageAffinity.Danimal => DanimalRageDur,
            RageAffinity.Dant => DantRageDur,
            _ => GrayRageDur,
        };
        if (f.Affinity == RageAffinity.Danimal && f.Hp > CombatMath.DanimalMaxHp(f.MaxHp))
            f.Hp = CombatMath.DanimalMaxHp(f.MaxHp); // −10% max life while active
        sim.Add(new SimEvent(frame, SimEventType.RageOn, idx, 1 - idx));
        pres.Add(new PresEvent(frame, PresKind.RageAura, f.Pos.X, f.Pos.Y, AttackLevel.Super));
    }

    /// <summary>Ticks Rage expiry into cooldown (per-affinity timings).</summary>
    public static void TickRage(Fighter f, int frame,
        System.Collections.Generic.List<SimEvent> sim, int idx)
    {
        if (f.RageActive && --f.RageTimer <= 0)
        {
            f.RageActive = false;
            f.RageStage = 0;
            f.RageCd = f.Affinity switch
            {
                RageAffinity.Danimal => DanimalRageCd,
                RageAffinity.Dant => DantRageCd,
                _ => GrayRageCd,
            };
            sim.Add(new SimEvent(frame, SimEventType.RageOff, idx, 1 - idx));
        }
        else if (!f.RageActive && f.RageCd > 0) f.RageCd--;
    }

    /// <summary>Physical damage numerator by affinity/rage (pairs with <see cref="PhysicalDen"/>).</summary>
    public static int PhysicalMult(Fighter f) =>
        f.RageActive && (f.Affinity is RageAffinity.Danimal or RageAffinity.Gray)
            ? (f.Affinity == RageAffinity.Danimal ? 5 : 11) : 4;
    /// <summary>Physical damage denominator (×5/4 Danimal, ×11/10 Gray, ×1 otherwise).</summary>
    public static int PhysicalDen(Fighter f) =>
        f.RageActive && (f.Affinity is RageAffinity.Danimal or RageAffinity.Gray)
            ? (f.Affinity == RageAffinity.Danimal ? 4 : 10) : 4;

    /// <summary>Energy damage numerator by affinity/rage (pairs with <see cref="EnergyMultDen"/>).</summary>
    public static int EnergyMultNum(Fighter f) =>
        f.RageActive && (f.Affinity is RageAffinity.Dant or RageAffinity.Gray)
            ? (f.Affinity == RageAffinity.Dant ? 5 : 11) : 4;
    /// <summary>Energy damage denominator (×5/4 Dant, ×11/10 Gray, ×1 otherwise).</summary>
    public static int EnergyMultDen(Fighter f) =>
        f.RageActive && (f.Affinity is RageAffinity.Dant or RageAffinity.Gray)
            ? (f.Affinity == RageAffinity.Dant ? 4 : 10) : 4;

    // ---- Resonance ----
    /// <summary>Activates Resonance if ready (cooldown + round use). Returns false when refused.</summary>
    public static bool TryActivateResonance(Fighter f, bool manual, int frame,
        System.Collections.Generic.List<SimEvent> sim, System.Collections.Generic.List<PresEvent> pres, int idx)
    {
        if (f.ResActive || f.ResCd > 0 || f.ResUsedRound) return false;
        f.ResActive = true;
        f.ResTimer = ResDur;
        f.ResCd = ResCd; // OPEN-003: cooldown runs from activation
        f.ResUsedRound = true;
        sim.Add(new SimEvent(frame, manual ? SimEventType.ResOn : SimEventType.ResAuto, idx, 1 - idx));
        pres.Add(new PresEvent(frame, PresKind.ResShimmer, f.Pos.X, f.Pos.Y, AttackLevel.Medium));
        return true;
    }

    /// <summary>Ticks Resonance expiry and cooldown.</summary>
    public static void TickResonance(Fighter f, int frame,
        System.Collections.Generic.List<SimEvent> sim, int idx)
    {
        if (f.ResActive && --f.ResTimer <= 0)
        {
            f.ResActive = false;
            sim.Add(new SimEvent(frame, SimEventType.ResOff, idx, 1 - idx));
        }
        if (f.ResCd > 0) f.ResCd--;
    }

    /// <summary>Round reset: HP restored, loser EX grace, all timers/cooldowns/uses cleared.</summary>
    public static void ResetForRound(Fighter f, bool isLoser)
    {
        f.Hp = f.MaxHp;
        if (isLoser && f.Ex < LoserGraceEx) f.Ex = LoserGraceEx; // gracia post-KO
        f.RageActive = false; f.RageTimer = 0; f.RageCd = 0; f.RageStage = 0;
        f.ResActive = false; f.ResTimer = 0; f.ResCd = 0; f.ResUsedRound = false;
        f.ComboGiven = 0; f.ComboTaken = 0; f.ConsecBlocked = 0;
        f.Hitstop = 0; f.Airborne = false; f.JuggleCount = 0;
        f.ParryStanceT = 0; f.ParryWhiffT = 0; f.ParryGraceUsed = false;
        f.ParryGraceArmed = false;
        f.CounterStanceT = 0; f.InvulnT = 0; f.MoveId = 0; f.DidHit = false;
        f.SuppressJump = false;
        f.AccelStacks = 0; f.AirAttackUsed = false; // gimmick state resets (Gimmick id persists)
        f.ExZonePerMille = 1000; f.SpeedZonePerMille = 1000; // recomputed per step from position
        f.Enter(FighterState.Neutral);
    }
}
