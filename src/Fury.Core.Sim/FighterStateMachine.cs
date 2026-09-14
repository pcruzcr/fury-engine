using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phases 4–5 of the pipeline: state machine commit and movement integration. Transitions
/// are driven by the FMT1 move tables (data-driven, ADR-003); frame countdowns run here.
/// Per-fighter hitstop freezes state frames and movement but the hitstop counter itself.
/// </summary>
public sealed class FighterStateMachine
{
    private readonly FightConfig _cfg;
    private readonly CharacterConfig[] _roster;

    public FighterStateMachine(FightConfig cfg, CharacterConfig p1, CharacterConfig p2)
    {
        _cfg = cfg;
        _roster = [p1, p2];
    }

    public CharacterConfig Data(int f) => _roster[f];

    /// <summary>Advances timers and applies a resolved intent for <paramref name="f"/>.</summary>
    public void Commit(FightState s, int f, MoveIntent intent)
    {
        int state = s.GetStateId(f);

        if (s.GetHitstop(f) > 0)
        {
            s.SetHitstop(f, s.GetHitstop(f) - 1);
            return; // frozen: no state frames, no intents
        }

        switch (state)
        {
            case FighterStateId.Ko:
                return;

            case FighterStateId.Hitstun:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetHitstun(f) > 0)
                {
                    s.SetHitstun(f, s.GetHitstun(f) - 1);
                }
                if (s.GetHitstun(f) == 0 && s.GetStateFrame(f) > 0 && !s.IsAirborne(f))
                {
                    s.SetStateId(f, FighterStateId.Idle);
                }
                break;

            case FighterStateId.Blockstun:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                s.SetBlockstun(f, s.GetBlockstun(f) - 1);
                if (s.GetBlockstun(f) == 0)
                {
                    s.SetStateId(f, FighterStateId.Idle);
                }
                break;

            case FighterStateId.Knockdown:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                if (s.GetStateFrame(f) >= 45)
                {
                    s.SetStateId(f, FighterStateId.Idle);
                }
                break;

            case FighterStateId.Attack:
                AdvanceAttack(s, f);
                break;

            case FighterStateId.Jump:
                s.SetStateFrame(f, s.GetStateFrame(f) + 1);
                break;
        }

        if (FighterStateId.IsNeutral(s.GetStateId(f)) && intent != MoveIntent.None)
        {
            StartAttack(s, f, intent);
        }
    }

    private void StartAttack(FightState s, int f, MoveIntent intent)
    {
        int slot = CommandResolver.ToMoveSlot(intent);
        if (slot < 0) return;

        var entry = Data(f).Moves.GetMove(slot);
        bool super = intent == MoveIntent.Super;
        bool special = intent == MoveIntent.Special;

        // Super spends all EX; special spends one segment when metered (EX special).
        if (super)
        {
            if (s.GetExMeter(f) < 300) return;
            s.SetExMeter(f, 0);
        }
        else if (special && s.GetExMeter(f) >= 100)
        {
            s.SetExMeter(f, s.GetExMeter(f) - 100);
        }

        s.SetStateId(f, FighterStateId.Attack);
        s.SetMoveIndex(f, slot);
        s.SetHasHit(f, false);
        s.SetComboCount(f, 0);
        s.SetComboDamage(f, 0);
        s.SetCrouching(f, intent is MoveIntent.CrouchLight or MoveIntent.CrouchHeavy);
    }

    private void AdvanceAttack(FightState s, int f)
    {
        s.SetStateFrame(f, s.GetStateFrame(f) + 1);
        var entry = Data(f).Moves.GetMove(s.GetMoveIndex(f));
        int total = entry.StartupFrames + entry.ActiveFrames + entry.RecoveryFrames;
        if (s.GetStateFrame(f) >= total && !s.IsAirborne(f))
        {
            s.SetStateId(f, FighterStateId.Idle);
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
        if (!IsActiveFrame(entry, s.GetStateFrame(f)) || s.HasHitThisActive(f)) return false;

        Fp facing = Fp.FromInt(s.GetFacing(f));
        Fp front = s.GetX(f) + (_cfg.PushboxHalfWidth * facing);
        Fp tip = front + (Fp.FromRaw(entry.ReachRaw) * facing);
        Fp centerY = s.GetY(f) - Fp.FromRaw(entry.BoxCenterYRaw);
        Fp half = Fp.FromRaw(entry.BoxHalfHeightRaw);

        minX = Fp.Min(front, tip);
        maxX = Fp.Max(front, tip);
        minY = centerY - half;
        maxY = centerY + half;
        return true;
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
