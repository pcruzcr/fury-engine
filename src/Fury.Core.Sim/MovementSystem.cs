using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 5 movement integration: walk velocities, jump arc, gravity, pushbox separation
/// and stage clamping — all Fp, all deterministic. Facing auto-updates in grounded neutral.
/// </summary>
public sealed class MovementSystem
{
    private readonly FightConfig _cfg;
    private readonly CharacterConfig[] _roster;

    public MovementSystem(FightConfig cfg, CharacterConfig p1, CharacterConfig p2)
    {
        _cfg = cfg;
        _roster = [p1, p2];
    }

    public CharacterConfig Data(int f) => _roster[f];

    /// <summary>Promotes neutral inputs into walk/crouch/jump states (before Integrate).</summary>
    public void HandleGroundControl(FightState s, InputBuffer[] buffers)
    {
        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (s.GetHitstop(f) > 0 || !FighterStateId.IsNeutral(s.GetStateId(f))) continue;

            var input = buffers[f];
            int facing = s.GetFacing(f);
            bool forwardHeld = facing > 0 ? input.Held(InputButton.Right) : input.Held(InputButton.Left);
            bool backHeld = facing > 0 ? input.Held(InputButton.Left) : input.Held(InputButton.Right);

            if (input.Held(InputButton.Up))
            {
                s.SetStateId(f, FighterStateId.Jump);
                s.SetAirborne(f, true);
                s.SetVy(f, -_cfg.JumpVelocityRaw);
                s.SetVx(f, forwardHeld ? Data(f).WalkForwardRaw : backHeld ? -Data(f).WalkBackRaw : Fp.Zero);
                continue;
            }

            bool downHeld = input.Held(InputButton.Down);
            s.SetCrouching(f, downHeld);
            s.SetStateId(f, downHeld ? FighterStateId.Crouch
                : forwardHeld ? FighterStateId.WalkForward
                : backHeld ? FighterStateId.WalkBack
                : FighterStateId.Idle);
        }
    }

    /// <summary>Integrates velocities, gravity and pushboxes for the frame.</summary>
    public void Integrate(FightState s)
    {
        // Side ordering before movement: separation must never let fighters swap sides,
        // even when one frame of movement exceeds the pushbox diameter (no tunneling).
        Fp preDelta = s.GetX(1) - s.GetX(0);

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (s.GetHitstop(f) > 0) continue;

            int other = 1 - f;
            UpdateFacing(s, f, other);

            if (s.IsAirborne(f))
            {
                Fp vy = s.GetVy(f) + _cfg.GravityRaw;
                s.SetVy(f, vy);
                s.SetX(f, s.GetX(f) + s.GetVx(f));
                s.SetY(f, s.GetY(f) + vy);

                if (s.GetY(f) >= Fp.Zero && vy > Fp.Zero)
                {
                    s.SetY(f, Fp.Zero);
                    s.SetVy(f, Fp.Zero);
                    s.SetVx(f, Fp.Zero);
                    s.SetAirborne(f, false);
                    if (s.GetStateId(f) == FighterStateId.Jump) s.SetStateId(f, FighterStateId.Idle);
                }
            }
            else if (s.GetStateId(f) is FighterStateId.WalkForward or FighterStateId.WalkBack)
            {
                Fp vx = s.GetStateId(f) == FighterStateId.WalkForward ? Data(f).WalkForwardRaw : -Data(f).WalkBackRaw;
                s.SetVx(f, vx);
                s.SetX(f, s.GetX(f) + vx);
            }
            else if (!s.IsAirborne(f))
            {
                s.SetVx(f, Fp.Zero);
            }

            ClampToStage(s, f);
        }

        Separate(s, preDelta >= Fp.Zero);
    }

    private static void UpdateFacing(FightState s, int f, int other)
    {
        if (!FighterStateId.IsNeutral(s.GetStateId(f))) return;
        Fp delta = s.GetX(other) - s.GetX(f);
        if (delta != Fp.Zero) s.SetFacing(f, delta.Raw > 0 ? 1 : -1);
    }

    private void ClampToStage(FightState s, int f)
    {
        Fp x = s.GetX(f);
        Fp limit = _cfg.StageHalfWidth - _cfg.PushboxHalfWidth;
        s.SetX(f, Fp.Clamp(x, -limit, limit));
    }

    /// <summary>
    /// Hard positional constraint: fighters keep at least one pushbox diameter of distance
    /// and never swap sides. Corner rule: a fighter at the stage wall absorbs the push.
    /// </summary>
    private void Separate(FightState s, bool p1WasLeft)
    {
        Fp minDist = _cfg.PushboxHalfWidth * Fp.FromInt(2);
        Fp limit = _cfg.StageHalfWidth - _cfg.PushboxHalfWidth;
        Fp delta = s.GetX(1) - s.GetX(0);
        bool p1Left = delta.Raw >= 0;

        if (p1Left != p1WasLeft)
        {
            // Tunneling guard: reposition around the midpoint preserving original sides.
            Fp mid = (s.GetX(0) + s.GetX(1)) * Fp.Half;
            Fp half = minDist * Fp.Half;
            s.SetX(0, p1WasLeft ? mid - half : mid + half);
            s.SetX(1, p1WasLeft ? mid + half : mid - half);
            return;
        }

        if (Fp.Abs(delta) < minDist)
        {
            Fp overlap = minDist - Fp.Abs(delta);
            int dir = p1Left ? -1 : 1; // push P1 away from P2
            Fp half = overlap * Fp.Half;
            s.SetX(0, Fp.Clamp(s.GetX(0) + (half * Fp.FromInt(dir)), -limit, limit));
            s.SetX(1, Fp.Clamp(s.GetX(1) - (half * Fp.FromInt(dir)), -limit, limit));

            // Wall absorb: if the pushed side hit the wall, give the remainder to the other.
            Fp after = s.GetX(1) - s.GetX(0);
            if (Fp.Abs(after) < minDist)
            {
                s.SetX(0, Fp.Clamp(s.GetX(0) + (overlap * Fp.FromInt(dir)), -limit, limit));
                s.SetX(1, Fp.Clamp(s.GetX(1) - (overlap * Fp.FromInt(dir)), -limit, limit));
            }
        }
    }
}
