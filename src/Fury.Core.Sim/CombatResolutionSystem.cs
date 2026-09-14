using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 7: combat resolution (hit/block/whiff), damage scaling, hitstop application and
/// the dual energy economy (EX offensive / Resonance defensive / Rage comeback) per the
/// canonical design: hitstop tiers live in the move data (L4/M6/H10/EX12/Super20).
/// All arithmetic is integer Fp — no floats, no RNG.
/// </summary>
public sealed class CombatResolutionSystem
{
    private readonly FightConfig _cfg;
    private readonly FighterStateMachine _fsm;
    private readonly InputBuffer[] _buffers;

    public CombatResolutionSystem(FightConfig cfg, FighterStateMachine fsm, InputBuffer[] buffers)
    {
        _cfg = cfg;
        _fsm = fsm;
        _buffers = buffers;
    }

    /// <summary>Resolves detected connects into hits or blocks and drives meter/KO/timeout.</summary>
    public void Resolve(FightState s, ReadOnlySpan<bool> connects, SimEventSink events)
    {
        for (int attacker = 0; attacker < FightState.FighterCount; attacker++)
        {
            if (!connects[attacker] || s.HasHitThisActive(attacker)) continue;

            int defender = 1 - attacker;
            var move = _fsm.Data(attacker).Moves.GetMove(s.GetMoveIndex(attacker));

            bool blocked = FighterStateId.CanBlock(s.GetStateId(defender)) && IsHoldingBack(s, defender, attacker);

            if (blocked)
            {
                ApplyBlock(s, events, attacker, defender, move);
            }
            else
            {
                ApplyHit(s, events, attacker, defender, move);
            }
            s.SetHasHit(attacker, true);
        }

        CheckRoundEnd(s, events);
    }

    private bool IsHoldingBack(FightState s, int defender, int attacker)
    {
        // Back relative to the attacker's side: defender holds away from the attacker.
        InputButton away = s.GetX(defender).Raw >= s.GetX(attacker).Raw ? InputButton.Right : InputButton.Left;
        return _buffers[defender].Held(away);
    }

    private void ApplyBlock(FightState s, SimEventSink events, int attacker, int defender, MoveEntry move)
    {
        int chip = move.Damage * _cfg.ChipDamagePercent / 100;
        s.SetHealth(defender, s.GetHealth(defender) - chip);

        s.SetStateId(defender, FighterStateId.Blockstun);
        s.SetBlockstun(defender, move.RecoveryFrames / 2 + 3);
        s.SetHitstop(defender, move.HitstopFrames);
        s.SetHitstop(attacker, move.HitstopFrames);

        int facing = s.GetFacing(attacker);
        s.SetVx(defender, Fp.FromRaw(move.PushbackRaw) * Fp.FromInt(facing));

        s.SetResonance(defender, s.GetResonance(defender) + _cfg.ResonanceGainOnBlock.Raw);
        s.SetExMeter(attacker, s.GetExMeter(attacker) + (_cfg.ExGainPerHit.Raw / 2));

        events.Emit(s.Frame, SimEventId.AttackBlocked, Pack(attacker, defender, chip));
    }

    private void ApplyHit(FightState s, SimEventSink events, int attacker, int defender, MoveEntry move)
    {
        int combo = s.GetComboCount(attacker);
        int scalePercent = System.Math.Max(_cfg.ComboScaleMinPercent, 100 - (combo * _cfg.ComboScaleStepPercent));
        int damage = move.Damage * scalePercent / 100;

        if (s.IsRaging(attacker))
        {
            damage = damage * _cfg.RageDamagePercent / 100;
        }

        s.SetHealth(defender, System.Math.Max(0, s.GetHealth(defender) - damage));
        s.SetComboCount(attacker, combo + 1);
        s.SetComboDamage(attacker, s.GetComboDamage(attacker) + damage);

        s.SetStateId(defender, s.IsAirborne(defender) ? FighterStateId.Knockdown : FighterStateId.Hitstun);
        s.SetHitstun(defender, move.RecoveryFrames + 6);
        s.SetHitstop(defender, move.HitstopFrames);
        s.SetHitstop(attacker, move.HitstopFrames);

        int facing = s.GetFacing(attacker);
        s.SetVx(defender, Fp.FromRaw(move.PushbackRaw) * Fp.FromInt(facing));

        s.SetExMeter(attacker, s.GetExMeter(attacker) + _cfg.ExGainPerHit.Raw);
        s.SetResonance(defender, s.GetResonance(defender) + _cfg.ResonanceGainOnHurt.Raw);

        events.Emit(s.Frame, SimEventId.AttackHit, Pack(attacker, defender, damage));
        UnlockRage(s, events, defender);
        UnlockRage(s, events, attacker);
    }

    private void UnlockRage(FightState s, SimEventSink events, int f)
    {
        if (!s.IsRaging(f) && s.GetHealth(f) <= _cfg.RageHealthThreshold)
        {
            s.SetRaging(f, true);
            events.Emit(s.Frame, SimEventId.RageUnlocked, (uint)f);
        }
    }

    private static void CheckRoundEnd(FightState s, SimEventSink events)
    {
        if (s.RoundState != 0)
        {
            return;
        }

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (s.GetHealth(f) == 0)
            {
                int winner = 1 - f;
                s.SetStateId(f, FighterStateId.Ko);
                s.EndRound(winner);
                events.Emit(s.Frame, SimEventId.RoundOver, (uint)winner);
                return;
            }
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

    private static ulong Pack(int a, int b, int v) => ((uint)a << 24) | ((uint)b << 16) | (uint)(v & 0xFFFF);
}
