using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Phase 5 movement: ground control (walk, crouch, jump, dash, backdash), integration of velocities and
/// gravity, projectile flight, pushbox separation and stage clamping. All Fp, all deterministic. Facing
/// auto-updates in grounded neutral. Walking is relative to the facing (forward = toward the opponent): the
/// old code walked along the world X axis, so player 2 (facing left) walked away from the key it held.
/// Pushback from hits slides grounded fighters with friction (before this only airborne fighters were ever
/// moved by it).
/// </summary>
public sealed class MovementSystem
{
    private readonly FightConfig _cfg;
    private readonly FighterRoster _roster;
    private readonly SimEventSink _events;
    private readonly ModuleHooks _hooks;

    internal MovementSystem(FightConfig cfg, FighterRoster roster, SimEventSink events, ModuleHooks hooks)
    {
        _cfg = cfg;
        _roster = roster;
        _events = events;
        _hooks = hooks;
    }

    public CharacterConfig Data(int f) => _roster.Get(f);

    /// <summary>Scales a speed by the combined module percentage (speed buffs); exact integer math.</summary>
    private Fp Scaled(int f, Fp speed)
    {
        int percent = _hooks.WalkSpeedPercent(f);
        return percent == 100 ? speed : Fp.FromRaw((int)((long)speed.Raw * percent / 100));
    }

    /// <summary>Promotes neutral inputs into walk/crouch/jump/dash states (before Integrate).</summary>
    public void HandleGroundControl(FightState s, InputBuffer[] buffers)
    {
        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (s.GetHitstop(f) > 0 || !FighterStateId.IsNeutral(s.GetStateId(f))) continue;

            var input = buffers[f];
            int facing = s.GetFacing(f);
            var forwardButton = facing > 0 ? InputButton.Right : InputButton.Left;
            var backButton = facing > 0 ? InputButton.Left : InputButton.Right;
            bool forwardHeld = input.Held(forwardButton);
            bool backHeld = input.Held(backButton);

            if (TryDash(s, f, input, facing, forwardButton, backButton)) continue;

            if (input.Held(InputButton.Up))
            {
                s.SetStateId(f, FighterStateId.Jump);
                s.SetAirborne(f, true);
                s.SetCrouching(f, false);
                s.SetVy(f, -_cfg.JumpVelocityRaw);
                Fp sideways = forwardHeld ? Scaled(f, Data(f).WalkForwardRaw) : backHeld ? -Scaled(f, Data(f).EffectiveWalkBack) : Fp.Zero;
                s.SetVx(f, sideways * Fp.FromInt(facing));
                _events.Emit(s.Frame, SimEventId.FighterJumped, (uint)f);
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

    /// <summary>Double-tap forward / back starts a dash / backdash (the second tap must be a fresh press).</summary>
    private bool TryDash(FightState s, int f, InputBuffer input, int facing, InputButton forwardButton, InputButton backButton)
    {
        if (input.Held(InputButton.Down) || input.Held(InputButton.Up)) return false;

        bool forward = Fresh(s, f, input, forwardButton) && MotionRecognizer.Matches(MotionId.DoubleTapForward, input, facing);
        bool back = !forward && Fresh(s, f, input, backButton) && MotionRecognizer.Matches(MotionId.DoubleTapBack, input, facing);
        if (!forward && !back) return false;

        var data = Data(f);
        s.SetInputConsumed(f, s.Frame + 1);
        s.SetStateId(f, forward ? FighterStateId.Dash : FighterStateId.Backdash);
        s.SetCrouching(f, false);
        Fp speed = Scaled(f, forward ? data.EffectiveDashSpeed : -data.EffectiveBackdashSpeed);
        s.SetVx(f, speed * Fp.FromInt(facing));
        if (!forward) s.SetInvuln(f, data.EffectiveBackdashInvuln);
        _events.Emit(s.Frame, SimEventId.Dash, SimEventPayload.Pack(f, forward ? 1 : 0, 0));
        return true;
    }

    private static bool Fresh(FightState s, int f, InputBuffer input, InputButton button)
    {
        int age = input.PressAge(button, 2);
        return age >= 0 && s.Frame - age >= s.GetInputConsumed(f);
    }

    /// <summary>Integrates velocities, gravity, projectiles and pushboxes for the frame.</summary>
    public void Integrate(FightState s)
    {
        // Side ordering before movement: separation must never let fighters swap sides,
        // even when one frame of movement exceeds the pushbox diameter (no tunneling).
        Fp preDelta = s.GetX(1) - s.GetX(0);

        for (int f = 0; f < FightState.FighterCount; f++)
        {
            if (s.GetHitstop(f) > 0) continue;

            UpdateFacing(s, f, 1 - f);
            IntegrateFighter(s, f);
            ClampToStage(s, f);
        }

        Separate(s, preDelta >= Fp.Zero);
        UpdateProjectiles(s);
    }

    private void IntegrateFighter(FightState s, int f)
    {
        if (s.IsAirborne(f))
        {
            Fp vy = s.GetVy(f) + _cfg.GravityRaw;
            s.SetVy(f, vy);
            Fp previousY = s.GetY(f);
            s.SetX(f, s.GetX(f) + s.GetVx(f));
            s.SetY(f, previousY + vy);

            if (vy > Fp.Zero)
            {
                // Falling: a platform surface crossed this frame catches the fighter before the floor does.
                if (_hooks.Any && _hooks.TryGetSupport(f, s.GetX(f), previousY, s.GetY(f), true, out Fp surface) && previousY <= surface && s.GetY(f) >= surface)
                {
                    Land(s, f, surface);
                }
                else if (s.GetY(f) >= Fp.Zero)
                {
                    Land(s, f, Fp.Zero);
                }
            }
            return;
        }

        if (s.GetY(f) != Fp.Zero && !StillSupported(s, f)) return; // walked off a platform edge: now falling
        s.SetX(f, s.GetX(f) + _hooks.GroundPush(f, s.GetX(f), s.GetY(f)));

        Fp facing = Fp.FromInt(s.GetFacing(f));
        switch (s.GetStateId(f))
        {
            case FighterStateId.WalkForward:
                s.SetVx(f, Scaled(f, Data(f).WalkForwardRaw) * facing);
                s.SetX(f, s.GetX(f) + s.GetVx(f));
                break;

            case FighterStateId.WalkBack:
                s.SetVx(f, -Scaled(f, Data(f).EffectiveWalkBack) * facing);
                s.SetX(f, s.GetX(f) + s.GetVx(f));
                break;

            case FighterStateId.Dash:
            case FighterStateId.Backdash:
                s.SetX(f, s.GetX(f) + s.GetVx(f)); // velocity was set when the dash started
                break;

            case FighterStateId.Hitstun:
            case FighterStateId.Blockstun:
            case FighterStateId.Attack:
                // Pushback (also the attacker when the defender is cornered): slide, losing speed every frame.
                s.SetX(f, s.GetX(f) + s.GetVx(f));
                Fp slowed = s.GetVx(f) * Fp.FromRatio(_cfg.PushFrictionPercent, 100);
                s.SetVx(f, Fp.Abs(slowed).Raw < 0x400 ? Fp.Zero : slowed);
                break;

            default:
                s.SetVx(f, Fp.Zero);
                break;
        }
    }

    /// <summary>A grounded fighter above the floor must still have a surface under its feet, otherwise it starts to fall.</summary>
    private bool StillSupported(FightState s, int f)
    {
        if (_hooks.Any && _hooks.TryGetSupport(f, s.GetX(f), s.GetY(f), s.GetY(f), false, out Fp surface))
        {
            s.SetY(f, surface); // moving platforms carry the fighter
            return true;
        }

        s.SetAirborne(f, true);
        s.SetVy(f, Fp.Zero);
        if (FighterStateId.IsNeutral(s.GetStateId(f))) s.SetStateId(f, FighterStateId.Jump);
        return false;
    }

    private void Land(FightState s, int f, Fp surfaceY)
    {
        s.SetY(f, surfaceY);
        s.SetVy(f, Fp.Zero);
        s.SetVx(f, Fp.Zero);
        s.SetAirborne(f, false);

        int bounce = _hooks.Any ? _hooks.BounceVyRaw(f, s.GetX(f), surfaceY) : 0;
        if (bounce != 0 && s.GetStateId(f) is not (FighterStateId.Hitstun or FighterStateId.Knockdown or FighterStateId.Ko))
        {
            s.SetAirborne(f, true);
            s.SetVy(f, -Fp.FromRaw(bounce));
            if (FighterStateId.IsNeutral(s.GetStateId(f))) s.SetStateId(f, FighterStateId.Jump);
            return;
        }

        switch (s.GetStateId(f))
        {
            case FighterStateId.Jump:
                s.SetStateId(f, FighterStateId.Idle);
                break;
            case FighterStateId.Hitstun:
                // A launched fighter reaches the floor: hard knockdown.
                s.SetHitstun(f, 0);
                s.SetStateId(f, FighterStateId.Knockdown);
                break;
        }
    }

    /// <summary>Moves live projectiles and removes expired ones or those that left the stage.</summary>
    private void UpdateProjectiles(FightState s)
    {
        Fp limit = _cfg.StageHalfWidth + Fp.FromInt(2);
        for (int slot = 0; slot < FightState.ProjectileSlots; slot++)
        {
            if (!s.ProjectileActive(slot)) continue;
            Fp x = s.ProjectileX(slot) + s.ProjectileVx(slot);
            int life = s.ProjectileLife(slot) - 1;
            if (life <= 0 || Fp.Abs(x) > limit)
            {
                s.DespawnProjectile(slot);
                continue;
            }
            s.SetProjectileX(slot, x);
            s.SetProjectileLife(slot, life);
        }
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
