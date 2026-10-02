using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 7: combat resolution (hit / block / throw / tech / whiff), damage scaling, stuns from the move data,
/// launchers and juggles, projectile impacts, hitstop, the dual energy economy (EX offensive / Resonance
/// defensive / Rage comeback) and round end. All arithmetic is integer Fp: no floats, no RNG.
/// </summary>
public sealed class CombatResolutionSystem
{
    private const int TechPushbackRaw = 0x4CCD; // 0.3 units per frame, sliding with friction
    private const int TechStun = 12;
    private const int TechHitstop = 6;

    private readonly FightConfig _cfg;
    private readonly FighterStateMachine _fsm;
    private readonly InputBuffer[] _buffers;
    private readonly ModuleHooks _hooks;

    internal CombatResolutionSystem(FightConfig cfg, FighterStateMachine fsm, InputBuffer[] buffers, ModuleHooks hooks)
    {
        _cfg = cfg;
        _fsm = fsm;
        _buffers = buffers;
        _hooks = hooks;
    }

    /// <summary>Resolves detected connects into hits, blocks or throws and drives meter/KO/timeout.</summary>
    public void Resolve(FightState s, ReadOnlySpan<bool> connects, ReadOnlySpan<bool> projectileHits, SimEventSink events)
    {
        for (int attacker = 0; attacker < FightState.FighterCount; attacker++)
        {
            if (!connects[attacker] || s.HasHitThisActive(attacker)) continue;

            int defender = 1 - attacker;
            var move = _fsm.Data(attacker).Moves.GetMove(s.GetMoveIndex(attacker));

            if (move.Has(MoveTraits.Throw))
            {
                ResolveThrow(s, events, attacker, defender, move);
                continue;
            }

            if (IsBlocked(s, defender, attacker, move)) ApplyBlock(s, events, attacker, defender, move, attackerHitstop: true);
            else ApplyHit(s, events, attacker, defender, move, attackerHitstop: true);
            s.SetHasHit(attacker, true);
        }

        for (int slot = 0; slot < FightState.ProjectileSlots; slot++)
        {
            if (!projectileHits[slot] || !s.ProjectileActive(slot)) continue;

            int owner = s.ProjectileOwner(slot);
            int defender = 1 - owner;
            var move = _fsm.Data(owner).Moves.GetMove(s.ProjectileMove(slot));

            if (IsBlocked(s, defender, owner, move)) ApplyBlock(s, events, owner, defender, move, attackerHitstop: false);
            else ApplyHit(s, events, owner, defender, move, attackerHitstop: false);
            s.DespawnProjectile(slot);
        }

        CheckRoundEnd(s, events);
    }

    // ------------------------------------------------------------------ blocking

    private bool IsBlocked(FightState s, int defender, int attacker, in MoveEntry move)
    {
        if (move.Has(MoveTraits.Unblockable) || !FighterStateId.CanBlock(s.GetStateId(defender))) return false;
        if (!IsHoldingBack(s, defender, attacker)) return false;

        // Standing guard stops Mid / High / Overhead, crouching guard stops Mid / High / Low.
        bool crouching = s.IsCrouching(defender);
        return move.Level switch
        {
            HitLevel.Low => crouching,
            HitLevel.Overhead => !crouching,
            _ => true,
        };
    }

    private bool IsHoldingBack(FightState s, int defender, int attacker)
    {
        // Back relative to the attacker's side: defender holds away from the attacker.
        InputButton away = s.GetX(defender).Raw >= s.GetX(attacker).Raw ? InputButton.Right : InputButton.Left;
        return _buffers[defender].Held(away);
    }

    // ------------------------------------------------------------------ throws

    private void ResolveThrow(FightState s, SimEventSink events, int attacker, int defender, in MoveEntry move)
    {
        int state = s.GetStateId(defender);
        bool grabbable = state is FighterStateId.Idle or FighterStateId.WalkForward or FighterStateId.WalkBack
            or FighterStateId.Crouch or FighterStateId.Blockstun or FighterStateId.Dash or FighterStateId.Backdash
            or FighterStateId.Attack;
        if (s.IsAirborne(defender) || !grabbable) return; // nothing to grab: the throw keeps trying until its active frames end

        // Both fighters grabbing on the same frames tech each other.
        if (state == FighterStateId.Attack && _fsm.Data(defender).Moves.GetMove(s.GetMoveIndex(defender)).Has(MoveTraits.Throw))
        {
            Tech(s, events, attacker, defender);
            return;
        }

        int damage = ScaledDamage(s, attacker, defender, move);
        s.SetHealth(defender, s.GetHealth(defender) - damage);
        s.SetComboCount(attacker, s.GetComboCount(attacker) + 1);
        s.SetComboDamage(attacker, s.GetComboDamage(attacker) + damage);

        s.SetStateId(defender, FighterStateId.Hitstun);
        s.SetCrouching(defender, false);
        Launch(s, defender, move.Traits | MoveTraits.Knockdown, move);
        s.SetHitstun(defender, move.EffectiveHitstun);
        s.SetHitstop(defender, move.HitstopFrames);
        s.SetHitstop(attacker, move.HitstopFrames);
        s.SetHasHit(attacker, true);
        s.SetConnect(attacker, 1);
        GainOnHit(s, attacker, defender);

        events.Emit(s.Frame, SimEventId.Throw, SimEventPayload.Pack(attacker, defender, damage));
        _hooks.OnHit(attacker, defender, damage, blocked: false);
        _hooks.OnThrow(attacker, defender);
        UnlockRage(s, events, defender);
        UnlockRage(s, events, attacker);
    }

    private static void Tech(FightState s, SimEventSink events, int a, int b)
    {
        for (int f = 0; f < FightState.FighterCount; f++)
        {
            int away = s.GetX(f).Raw >= s.GetX(1 - f).Raw ? 1 : -1;
            s.SetStateId(f, FighterStateId.Hitstun);
            s.SetHitstun(f, TechStun);
            s.SetHitstop(f, TechHitstop);
            s.SetHasHit(f, true);
            s.SetVx(f, Fp.FromRaw(TechPushbackRaw) * Fp.FromInt(away));
        }
        events.Emit(s.Frame, SimEventId.ThrowTeched, SimEventPayload.Pack(a, b, 0));
    }

    // ------------------------------------------------------------------ hit / block

    private void ApplyBlock(FightState s, SimEventSink events, int attacker, int defender, in MoveEntry move, bool attackerHitstop)
    {
        int chip = move.Damage * _cfg.ChipDamagePercent / 100 * _hooks.DamagePercent(attacker, defender) / 100;
        s.SetHealth(defender, s.GetHealth(defender) - chip);

        s.SetStateId(defender, FighterStateId.Blockstun);
        s.SetBlockstun(defender, move.EffectiveBlockstun);
        s.SetHitstop(defender, move.HitstopFrames);
        if (attackerHitstop) s.SetHitstop(attacker, move.HitstopFrames);
        s.SetConnect(attacker, 2);

        Pushback(s, attacker, defender, move);

        s.SetResonance(defender, s.GetResonance(defender) + _cfg.ResonanceGainOnBlock.FloorInt);
        s.SetExMeter(attacker, s.GetExMeter(attacker) + (_cfg.ExGainPerHit.FloorInt / 2));

        events.Emit(s.Frame, SimEventId.AttackBlocked, SimEventPayload.Pack(attacker, defender, chip));
        _hooks.OnHit(attacker, defender, chip, blocked: true);
    }

    private void ApplyHit(FightState s, SimEventSink events, int attacker, int defender, in MoveEntry move, bool attackerHitstop)
    {
        int damage = ScaledDamage(s, attacker, defender, move);

        s.SetHealth(defender, s.GetHealth(defender) - damage);
        s.SetComboCount(attacker, s.GetComboCount(attacker) + 1);
        s.SetComboDamage(attacker, s.GetComboDamage(attacker) + damage);

        bool wasAirborne = s.IsAirborne(defender);
        s.SetStateId(defender, FighterStateId.Hitstun);
        s.SetCrouching(defender, false);
        s.SetHitstun(defender, move.EffectiveHitstun);
        s.SetHitstop(defender, move.HitstopFrames);
        if (attackerHitstop) s.SetHitstop(attacker, move.HitstopFrames);
        s.SetConnect(attacker, 1);

        Launch(s, defender, move.Traits, move);
        if (wasAirborne && !move.Has(MoveTraits.Launcher)) s.SetVy(defender, -Fp.FromRaw(_cfg.JugglePopRaw)); // keeps a juggle alive
        Pushback(s, attacker, defender, move);

        GainOnHit(s, attacker, defender);

        events.Emit(s.Frame, SimEventId.AttackHit, SimEventPayload.Pack(attacker, defender, damage));
        _hooks.OnHit(attacker, defender, damage, blocked: false);
        UnlockRage(s, events, defender);
        UnlockRage(s, events, attacker);
    }

    /// <summary>Damage after combo scaling and the Rage bonus. The combo counter is the attacker's, so it keeps growing across chained moves.</summary>
    private int ScaledDamage(FightState s, int attacker, int defender, in MoveEntry move)
    {
        int combo = s.GetComboCount(attacker);
        int scalePercent = System.Math.Max(_cfg.ComboScaleMinPercent, 100 - (combo * _cfg.ComboScaleStepPercent));
        int damage = move.Damage * scalePercent / 100;
        if (s.IsRaging(attacker)) damage = damage * _cfg.RageDamagePercent / 100;
        return (damage * _hooks.DamagePercent(attacker, defender) / 100) + _hooks.FlatDamage(attacker, defender);
    }

    /// <summary>Launchers throw the defender high, knockdowns low; both leave them airborne until they land.</summary>
    private void Launch(FightState s, int defender, MoveTraits traits, in MoveEntry move)
    {
        if ((traits & MoveTraits.Launcher) != 0)
        {
            s.SetAirborne(defender, true);
            s.SetVy(defender, -Fp.FromRaw(move.LaunchVyRaw != 0 ? move.LaunchVyRaw : _cfg.KnockdownPopRaw * 2));
        }
        else if ((traits & MoveTraits.Knockdown) != 0 && !s.IsAirborne(defender))
        {
            s.SetAirborne(defender, true);
            s.SetVy(defender, -Fp.FromRaw(_cfg.KnockdownPopRaw));
        }
    }

    /// <summary>
    /// Pushback moves the defender away from the attacker. A defender with its back to the wall cannot move, so
    /// the attacker is pushed back instead (corner pressure never lets a fighter escape the stage).
    /// </summary>
    private void Pushback(FightState s, int attacker, int defender, in MoveEntry move)
    {
        Fp push = Fp.FromRaw(move.PushbackRaw);
        int away = s.GetX(defender).Raw >= s.GetX(attacker).Raw ? 1 : -1;
        Fp wall = _cfg.StageHalfWidth - _cfg.PushboxHalfWidth;
        Fp roomBehind = away > 0 ? wall - s.GetX(defender) : s.GetX(defender) + wall;

        if (roomBehind < Fp.One)
        {
            s.SetVx(defender, Fp.Zero);
            s.SetVx(attacker, push * Fp.FromInt(-away));
        }
        else
        {
            s.SetVx(defender, push * Fp.FromInt(away));
        }
    }

    private void GainOnHit(FightState s, int attacker, int defender)
    {
        // Meter units are points (EX 0..300 = 3 segments, Resonance 0..100), so the gain is the INTEGER
        // part of the Fp tuning value. Using .Raw (12 * 65536) saturated both meters on the very first hit.
        s.SetExMeter(attacker, s.GetExMeter(attacker) + _cfg.ExGainPerHit.FloorInt);
        s.SetResonance(defender, s.GetResonance(defender) + _cfg.ResonanceGainOnHurt.FloorInt);
    }

    private void UnlockRage(FightState s, SimEventSink events, int f)
    {
        if (!s.IsRaging(f) && s.GetHealth(f) <= _cfg.RageHealthThreshold)
        {
            s.SetRaging(f, true);
            events.Emit(s.Frame, SimEventId.RageUnlocked, (uint)f);
        }
    }

    // ------------------------------------------------------------------ round end

    private void CheckRoundEnd(FightState s, SimEventSink events)
    {
        if (s.RoundState != 0) return;

        // A tag partner (or any module) may take over a KO'd fighter: the round then simply continues.
        bool ko0 = s.GetHealth(0) == 0 && !_hooks.TrySubstituteKo(0, s);
        bool ko1 = s.GetHealth(1) == 0 && !_hooks.TrySubstituteKo(1, s);
        if (ko0 || ko1)
        {
            // Double KO (trade on the same frame) is a draw.
            int winner = ko0 && ko1 ? -1 : ko0 ? 1 : 0;
            if (ko0) s.SetStateId(0, FighterStateId.Ko);
            if (ko1) s.SetStateId(1, FighterStateId.Ko);
            for (int slot = 0; slot < FightState.ProjectileSlots; slot++) s.DespawnProjectile(slot);
            s.EndRound(winner);
            events.Emit(s.Frame, SimEventId.RoundOver, unchecked((uint)winner));
            return;
        }

        // Timeout: higher health wins; equal health is a draw (-1).
        if (s.RoundTimerFrames == 0)
        {
            int h0 = s.GetHealth(0);
            int h1 = s.GetHealth(1);
            int winner = h0 == h1 ? -1 : h0 > h1 ? 0 : 1;
            s.EndRound(winner);
            events.Emit(s.Frame, SimEventId.RoundOver, unchecked((uint)winner));
        }
    }
}
