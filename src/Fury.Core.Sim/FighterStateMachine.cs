using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 4 of the pipeline: state machine commit. Transitions are driven by the move tables (data-driven,
/// ADR-003); frame countdowns run here. Per-fighter hitstop freezes state frames and movement but the hitstop
/// counter itself. Handles move starts (including air normals), cancels into other categories once an attack
/// has connected, projectile spawns, knockdown/wake-up and the end of combos.
/// </summary>
public sealed class FighterStateMachine
{
    private readonly FightConfig _cfg;
    private readonly FighterRoster _roster;
    private readonly SimEventSink _events;
    private readonly ModuleHooks _hooks;

    internal FighterStateMachine(FightConfig cfg, FighterRoster roster, SimEventSink events, ModuleHooks hooks)
    {
        _cfg = cfg;
        _roster = roster;
        _events = events;
        _hooks = hooks;
    }

    public CharacterConfig Data(int f) => _roster.Get(f);

    /// <summary>Advances timers and applies the resolved command (<paramref name="move"/>, or <see cref="CommandResolver.NoMove"/>) for <paramref name="f"/>.</summary>
    public void Commit(FightState s, int f, int move)
    {
        if (s.GetHitstop(f) > 0)
        {
            s.SetHitstop(f, s.GetHitstop(f) - 1);
            return; // frozen: no state frames, no intents
        }

        if (s.GetInvuln(f) > 0) s.SetInvuln(f, s.GetInvuln(f) - 1);

        switch (s.GetStateId(f))
        {
            case FighterStateId.Ko:
                return;

            case FighterStateId.Hitstun:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetHitstun(f) > 0) s.SetHitstun(f, s.GetHitstun(f) - 1);
                // Airborne fighters stay in hitstun until they land (landing turns it into a knockdown).
                if (s.GetHitstun(f) == 0 && !s.IsAirborne(f)) EndCombo(s, f, FighterStateId.Idle);
                break;

            case FighterStateId.Blockstun:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                s.SetBlockstun(f, s.GetBlockstun(f) - 1);
                if (s.GetBlockstun(f) <= 0) s.SetStateId(f, FighterStateId.Idle);
                break;

            case FighterStateId.Knockdown:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetStateFrame(f) >= _cfg.KnockdownFrames)
                {
                    EndCombo(s, f, FighterStateId.Idle);
                    s.SetInvuln(f, _cfg.WakeupInvulnFrames);
                }
                break;

            case FighterStateId.Attack:
                AdvanceAttack(s, f);
                break;

            case FighterStateId.Jump:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                break;

            case FighterStateId.Dash:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetStateFrame(f) >= Data(f).EffectiveDashFrames) EndMotion(s, f);
                break;

            case FighterStateId.Backdash:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetStateFrame(f) >= Data(f).EffectiveBackdashFrames) EndMotion(s, f);
                break;
        }

        if (move != CommandResolver.NoMove) TryStart(s, f, move);
    }

    /// <summary>The defender recovered: the opponent's combo ends and the counters reset.</summary>
    private static void EndCombo(FightState s, int defender, int nextState)
    {
        s.SetStateId(defender, nextState);
        int attacker = 1 - defender;
        s.SetComboCount(attacker, 0);
        s.SetComboDamage(attacker, 0);
    }

    private static void EndMotion(FightState s, int f)
    {
        s.SetVx(f, Fp.Zero);
        s.SetStateId(f, FighterStateId.Idle);
    }

    /// <summary>Why a command may start: grounded neutral, an air normal from a jump, or a cancel of a connected attack.</summary>
    private bool CanStart(FightState s, int f, int index, in MoveEntry move)
    {
        int state = s.GetStateId(f);
        if (FighterStateId.IsNeutral(state)) return !move.Has(MoveTraits.Air) || s.IsAirborne(f);
        if (state == FighterStateId.Jump && s.IsAirborne(f)) return move.Has(MoveTraits.Air);
        if (state != FighterStateId.Attack) return false;

        // Cancel: the current attack connected (hit or block), we are past its startup, the target is allowed.
        var current = Data(f).Moves.GetMove(s.GetMoveIndex(f));
        return s.GetConnect(f) != 0
            && index != s.GetMoveIndex(f)
            && s.GetStateFrame(f) > current.StartupFrames
            && (current.CancelMask & MoveCategory.Bit(move.Category)) != 0
            && (!move.Has(MoveTraits.Air) || s.IsAirborne(f));
    }

    private void TryStart(FightState s, int f, int index)
    {
        var move = Data(f).Moves.GetMove(index);
        if (!CanStart(s, f, index, move)) return;
        if (move.MeterCost > s.GetExMeter(f)) return;

        bool cancel = s.GetStateId(f) == FighterStateId.Attack;
        s.SetExMeter(f, s.GetExMeter(f) - move.MeterCost);

        s.SetStateId(f, FighterStateId.Attack);
        s.SetMoveIndex(f, index);
        s.SetHasHit(f, false);
        s.SetConnect(f, 0);
        s.SetCrouching(f, MoveCommand.Stance(move.Command) == MoveStance.Crouch);
        s.SetInputConsumed(f, s.Frame + 1); // the presses that triggered this move are used up

        _events.Emit(s.Frame, cancel ? SimEventId.Cancel : SimEventId.MoveStarted, SimEventPayload.Pack(f, index, 0));
    }

    private void AdvanceAttack(FightState s, int f)
    {
        s.SetStateFrame(f, s.GetStateFrame(f) + 1);
        var move = Data(f).Moves.GetMove(s.GetMoveIndex(f));

        if (move.Has(MoveTraits.Projectile) && s.GetStateFrame(f) == move.ProjectileSpawnFrame) SpawnProjectile(s, f, move);

        if (s.GetStateFrame(f) >= move.TotalFrames)
        {
            // An air attack that outlasts the jump falls back into the jump state; grounded ones return to neutral.
            s.SetStateId(f, s.IsAirborne(f) ? FighterStateId.Jump : FighterStateId.Idle);
        }
    }

    private void SpawnProjectile(FightState s, int f, in MoveEntry move)
    {
        if (s.ProjectileCount(f) >= _cfg.MaxProjectilesPerFighter) return;
        Fp facing = Fp.FromInt(s.GetFacing(f));
        Fp x = s.GetX(f) + ((_cfg.PushboxHalfWidth + Fp.Half) * facing);
        Fp y = s.GetY(f) - Fp.FromRaw(move.BoxCenterYRaw);
        if (s.TrySpawnProjectile(f, s.GetMoveIndex(f), x, y, Fp.FromRaw(move.ProjectileSpeedRaw) * facing, System.Math.Max(1, move.ProjectileLifetime)))
        {
            _events.Emit(s.Frame, SimEventId.ProjectileSpawned, SimEventPayload.Pack(f, s.GetMoveIndex(f), 0));
        }
    }

    /// <summary>True while frame <paramref name="stateFrame"/> is inside the active window of the move.</summary>
    public static bool IsActiveFrame(MoveEntry entry, int stateFrame)
    {
        return stateFrame > entry.StartupFrames
            && stateFrame <= entry.StartupFrames + entry.ActiveFrames;
    }

    /// <summary>Active-window hitbox of the current attack in world coordinates, if attacking and active.</summary>
    public bool TryGetHitbox(FightState s, int f, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY)
    {
        minX = minY = maxX = maxY = Fp.Zero;
        if (s.GetStateId(f) != FighterStateId.Attack) return false;

        var entry = Data(f).Moves.GetMove(s.GetMoveIndex(f));
        if (entry.Has(MoveTraits.Projectile) || !IsActiveFrame(entry, s.GetStateFrame(f)) || s.HasHitThisActive(f)) return false;

        Fp facing = Fp.FromInt(s.GetFacing(f));
        Fp front = s.GetX(f) + (_cfg.PushboxHalfWidth * facing);
        Fp tip = front + (Fp.FromRaw(entry.ReachRaw + _hooks.ReachBonusRaw(f)) * facing);
        Fp centerY = s.GetY(f) - Fp.FromRaw(entry.BoxCenterYRaw);
        Fp half = Fp.FromRaw(entry.BoxHalfHeightRaw);

        minX = Fp.Min(front, tip);
        maxX = Fp.Max(front, tip);
        minY = centerY - half;
        maxY = centerY + half;
        return true;
    }

    /// <summary>Hitbox of an active projectile slot in world coordinates.</summary>
    public void GetProjectileBox(FightState s, int slot, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY)
    {
        var entry = Data(s.ProjectileOwner(slot)).Moves.GetMove(s.ProjectileMove(slot));
        Fp halfWidth = Fp.FromRaw(entry.ReachRaw) * Fp.Half;
        Fp half = Fp.FromRaw(entry.BoxHalfHeightRaw);
        Fp x = s.ProjectileX(slot);
        Fp y = s.ProjectileY(slot);
        minX = x - halfWidth;
        maxX = x + halfWidth;
        minY = y - half;
        maxY = y + half;
    }

    /// <summary>Static hurtbox in world coordinates (standing or crouching height).</summary>
    public void GetHurtbox(FightState s, int f, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY)
    {
        Fp half = _cfg.HurtboxHalfWidth;
        Fp height = s.IsCrouching(f) ? (_cfg.HurtboxHeight * Fp.Half) : _cfg.HurtboxHeight;
        Fp x = s.GetX(f);
        Fp y = s.GetY(f);
        minX = x - half;
        maxX = x + half;
        minY = y - height;
        maxY = y;
    }
}
