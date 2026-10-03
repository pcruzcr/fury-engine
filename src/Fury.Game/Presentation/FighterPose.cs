using Fury.Core.Data;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>Joints of the procedural fighter. Near = the limb on the side facing the camera.</summary>
public enum Joint
{
    Head,
    Neck,
    Hip,
    ShoulderNear,
    ElbowNear,
    HandNear,
    ShoulderFar,
    ElbowFar,
    HandFar,
    KneeNear,
    FootNear,
    KneeFar,
    FootFar,
}

/// <summary>
/// Procedural fighter used until a character ships art: a posed skeleton driven only by what the simulation already knows
/// (state, state frame, airborne, the current move's timing and hitbox). It reads the sim and never writes to it, so it
/// is as deterministic and rollback-safe as the rest of the presentation. Joints are in "fighter space": origin at the
/// feet, +X toward the opponent, +Y up, simulation units (a standing fighter is 4 units tall).
/// </summary>
public static class FighterPoser
{
    public const int JointCount = 13;
    public const float HeadRadius = 0.36f;
    public const float LimbWidth = 0.34f;

    private const float Thigh = 0.95f;
    private const float Shin = 0.95f;
    private const float UpperArm = 0.7f;
    private const float Forearm = 0.7f;
    private const float TorsoLength = 1.25f;
    private const float MaxStretch = 1.9f;

    /// <summary>Fills <paramref name="joints"/> (length <see cref="JointCount"/>) with the pose of fighter <paramref name="f"/>.</summary>
    public static void Compute(FightSimulation sim, int f, Vector2[] joints)
    {
        var s = sim.State;
        int state = s.GetStateId(f);
        int frame = s.GetStateFrame(f);
        bool crouching = s.IsCrouching(f);
        bool airborne = s.IsAirborne(f);
        float clock = s.Frame;
        float breath = Mathf.Sin(clock * 0.11f + (f * 1.7f));

        switch (state)
        {
            case FighterStateId.WalkForward:
                Walk(joints, frame, +1f);
                break;
            case FighterStateId.WalkBack:
                Walk(joints, frame, -1f);
                break;
            case FighterStateId.Dash:
                Dash(joints, frame, forward: true);
                break;
            case FighterStateId.Backdash:
                Dash(joints, frame, forward: false);
                break;
            case FighterStateId.Crouch:
                Crouch(joints, breath);
                break;
            case FighterStateId.Jump:
                Airborne(joints, s.GetVy(f).ToFloat());
                break;
            case FighterStateId.Attack:
                Attack(sim, f, joints, frame, crouching, airborne);
                break;
            case FighterStateId.Hitstun:
                Hit(joints, frame, airborne, crouching);
                break;
            case FighterStateId.Blockstun:
                Block(joints, frame, crouching);
                break;
            case FighterStateId.Knockdown:
                Lying(joints, frame, airborne, ko: false);
                break;
            case FighterStateId.Ko:
                Lying(joints, frame, airborne, ko: true);
                break;
            default:
                if (airborne) Airborne(joints, s.GetVy(f).ToFloat());
                else Idle(joints, breath);
                break;
        }
    }

    // ------------------------------------------------------------------ poses

    private static void Idle(Vector2[] j, float breath)
    {
        Place(j, new Vector2(0f, 1.80f + (breath * 0.035f)), lean: 0.12f,
            handNear: new Vector2(0.95f, 3.05f + (breath * 0.03f)), handFar: new Vector2(0.55f, 2.85f),
            footNear: new Vector2(0.55f, 0f), footFar: new Vector2(-0.5f, 0f));
    }

    private static void Walk(Vector2[] j, int frame, float direction)
    {
        float phase = frame * 0.3f * direction;
        float swing = Mathf.Sin(phase);
        float lift = Mathf.Max(0f, Mathf.Cos(phase)) * 0.3f;
        float liftFar = Mathf.Max(0f, -Mathf.Cos(phase)) * 0.3f;
        float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.06f;
        Place(j, new Vector2(0.05f * direction, 1.78f - bob), lean: direction > 0 ? 0.2f : 0.04f,
            handNear: new Vector2(0.95f, 3.0f), handFar: new Vector2(0.55f, 2.85f),
            footNear: new Vector2(0.15f + (swing * 0.7f), lift), footFar: new Vector2(0.1f - (swing * 0.7f), liftFar));
    }

    private static void Dash(Vector2[] j, int frame, bool forward)
    {
        float t = Mathf.Min(frame / 6f, 1f);
        if (forward)
        {
            Place(j, new Vector2(0.35f, 1.55f), lean: 0.55f,
                handNear: new Vector2(0.55f, 2.4f), handFar: new Vector2(-0.45f, 2.7f),
                footNear: new Vector2(1.0f, 0.15f * t), footFar: new Vector2(-0.95f, 0.2f * t));
        }
        else
        {
            Place(j, new Vector2(-0.3f, 1.6f + (0.15f * t)), lean: -0.3f,
                handNear: new Vector2(0.85f, 2.9f), handFar: new Vector2(0.45f, 2.7f),
                footNear: new Vector2(0.45f, 0.45f * t), footFar: new Vector2(-0.35f, 0.35f * t));
        }
    }

    private static void Crouch(Vector2[] j, float breath)
    {
        Place(j, new Vector2(-0.1f, 0.78f + (breath * 0.02f)), lean: 0.38f,
            handNear: new Vector2(0.95f, 1.45f), handFar: new Vector2(0.6f, 1.3f),
            footNear: new Vector2(0.75f, 0f), footFar: new Vector2(-0.65f, 0f), headRise: 0.82f);
    }

    private static void Airborne(Vector2[] j, float vy)
    {
        // vy is negative while rising (Y grows down in the simulation).
        float rising = Mathf.Clamp(-vy / 0.8f, -1f, 1f);
        float tuck = 0.5f + (0.25f * rising);
        Place(j, new Vector2(0f, 1.9f), lean: 0.1f,
            handNear: new Vector2(0.8f, 3.3f + (0.3f * rising)), handFar: new Vector2(0.15f, 3.35f),
            footNear: new Vector2(0.55f, tuck), footFar: new Vector2(-0.4f, tuck - 0.25f));
    }

    private static void Attack(FightSimulation sim, int f, Vector2[] j, int frame, bool crouching, bool airborne)
    {
        var s = sim.State;
        var entry = sim.Data(f).Moves.GetMove(s.GetMoveIndex(f));
        int startup = Mathf.Max(1, entry.StartupFrames);
        float ext;
        if (frame <= startup) ext = Ease(frame / (float)startup);
        else if (frame <= startup + entry.ActiveFrames) ext = 1f;
        else ext = 1f - Ease((frame - startup - entry.ActiveFrames) / (float)Mathf.Max(1, entry.RecoveryFrames));

        if (crouching) Crouch(j, 0f);
        else if (airborne) Airborne(j, s.GetVy(f).ToFloat());
        else Idle(j, 0f);

        float push = sim.Config.PushboxHalfWidth.ToFloat();
        float reach = push + (entry.ReachRaw / 65536f);
        float height = entry.BoxCenterYRaw / 65536f;

        if (entry.Has(MoveTraits.Projectile))
        {
            // Both hands thrust forward at chest height.
            Vector2 chest = new(0.6f + (ext * 1.2f), crouching ? 1.3f : 2.7f);
            Retarget(j, handNear: chest + new Vector2(0.15f, 0.05f), handFar: chest - new Vector2(0.1f, 0f), lean: 0.12f + (0.35f * ext), shift: 0.25f * ext);
            return;
        }

        if (entry.Has(MoveTraits.Throw))
        {
            Vector2 grab = new(0.5f + (ext * (reach - 0.2f)), crouching ? 1.4f : 2.4f);
            Retarget(j, handNear: grab + new Vector2(0.1f, 0.25f), handFar: grab - new Vector2(0.1f, 0.2f), lean: 0.2f + (0.4f * ext), shift: 0.3f * ext);
            return;
        }

        bool kick = height < (crouching ? 0.7f : 1.7f);
        if (kick)
        {
            // Plant the far foot, swing the near foot out to the hitbox, lean the torso away for balance.
            Vector2 start = new(0.55f, 0f);
            Vector2 tip = new(reach, Mathf.Max(0.1f, height));
            Vector2 foot = start.Lerp(tip, ext);
            if (!crouching && !airborne) foot.Y += Mathf.Sin(ext * Mathf.Pi) * 0.25f;
            Retarget(j, footNear: foot, lean: 0.12f - (0.22f * ext), shift: -0.1f * ext);
        }
        else
        {
            Vector2 guard = new(0.95f, crouching ? 1.45f : 3.05f);
            Vector2 tip = new(reach, height);
            Vector2 hand = guard.Lerp(tip, ext);
            Retarget(j, handNear: hand, lean: 0.12f + (0.38f * ext), shift: 0.35f * ext);
        }
    }

    private static void Hit(Vector2[] j, int frame, bool airborne, bool crouching)
    {
        float shake = frame < 8 ? Mathf.Sin(frame * 2.6f) * 0.07f : 0f;
        if (airborne)
        {
            Place(j, new Vector2(-0.6f + shake, 1.7f), lean: -0.95f,
                handNear: new Vector2(-1.2f, 2.9f), handFar: new Vector2(-1.5f, 2.4f),
                footNear: new Vector2(0.2f, 0.5f), footFar: new Vector2(0.9f, 0.7f));
        }
        else if (crouching)
        {
            Place(j, new Vector2(-0.3f + shake, 0.8f), lean: -0.2f,
                handNear: new Vector2(-0.3f, 1.8f), handFar: new Vector2(-0.7f, 1.5f),
                footNear: new Vector2(0.75f, 0f), footFar: new Vector2(-0.65f, 0f), headRise: 0.82f);
        }
        else
        {
            Place(j, new Vector2(-0.25f + shake, 1.72f), lean: -0.5f,
                handNear: new Vector2(-0.7f, 2.9f), handFar: new Vector2(-1.1f, 2.5f),
                footNear: new Vector2(0.45f, 0f), footFar: new Vector2(-0.7f, 0f));
        }
    }

    private static void Block(Vector2[] j, int frame, bool crouching)
    {
        float shake = frame < 6 ? Mathf.Sin(frame * 2.2f) * 0.05f : 0f;
        if (crouching)
        {
            Place(j, new Vector2(-0.15f + shake, 0.8f), lean: 0.3f,
                handNear: new Vector2(0.85f, 1.8f), handFar: new Vector2(0.7f, 1.55f),
                footNear: new Vector2(0.75f, 0f), footFar: new Vector2(-0.65f, 0f), headRise: 0.82f);
        }
        else
        {
            Place(j, new Vector2(-0.1f + shake, 1.72f), lean: 0.02f,
                handNear: new Vector2(0.75f, 3.25f), handFar: new Vector2(0.9f, 2.85f),
                footNear: new Vector2(0.5f, 0f), footFar: new Vector2(-0.55f, 0f));
        }
    }

    private static void Lying(Vector2[] j, int frame, bool airborne, bool ko)
    {
        if (airborne)
        {
            // Tumbling backwards: body tilted well past horizontal.
            Place(j, new Vector2(-0.5f, 1.4f), lean: -1.35f,
                handNear: new Vector2(-1.6f, 2.0f), handFar: new Vector2(-1.9f, 1.6f),
                footNear: new Vector2(0.5f, 1.3f), footFar: new Vector2(1.1f, 0.9f));
            return;
        }

        // On the ground, head away from the opponent.
        float settle = Mathf.Min(frame / 8f, 1f);
        j[(int)Joint.Hip] = new Vector2(-0.4f, 0.28f);
        j[(int)Joint.Neck] = new Vector2(-1.55f, 0.3f);
        j[(int)Joint.Head] = new Vector2(-2.1f, 0.34f);
        j[(int)Joint.ShoulderNear] = j[(int)Joint.ShoulderFar] = new Vector2(-1.4f, 0.32f);
        j[(int)Joint.ElbowNear] = new Vector2(ko ? -1.6f : -1.0f, 0.55f + (0.1f * (1f - settle)));
        j[(int)Joint.HandNear] = new Vector2(ko ? -1.9f : -0.55f, ko ? 0.2f : 0.3f);
        j[(int)Joint.ElbowFar] = new Vector2(ko ? -1.2f : -0.9f, 0.2f);
        j[(int)Joint.HandFar] = new Vector2(ko ? -0.7f : -0.4f, 0.12f);
        j[(int)Joint.KneeNear] = new Vector2(0.4f, 0.55f);
        j[(int)Joint.FootNear] = new Vector2(1.2f, 0.18f);
        j[(int)Joint.KneeFar] = new Vector2(0.55f, 0.2f);
        j[(int)Joint.FootFar] = new Vector2(1.4f, 0.1f);
    }

    // ------------------------------------------------------------------ building blocks

    /// <summary>Full-body placement from the hip, a torso lean (radians, + = toward the opponent) and the four end effectors.</summary>
    private static void Place(
        Vector2[] j, Vector2 hip, float lean, Vector2 handNear, Vector2 handFar, Vector2 footNear, Vector2 footFar, float headRise = 1f)
    {
        var up = new Vector2(Mathf.Sin(lean), Mathf.Cos(lean));
        var neck = hip + (up * (TorsoLength * headRise));
        var shoulder = hip + (up * (TorsoLength * headRise * 0.88f));
        var headDir = new Vector2(Mathf.Sin(lean * 0.6f), Mathf.Cos(lean * 0.6f));

        j[(int)Joint.Hip] = hip;
        j[(int)Joint.Neck] = neck;
        j[(int)Joint.Head] = neck + (headDir * (0.14f + HeadRadius));
        j[(int)Joint.ShoulderNear] = shoulder;
        j[(int)Joint.ShoulderFar] = shoulder + new Vector2(-0.06f, 0f);

        j[(int)Joint.HandNear] = handNear;
        j[(int)Joint.HandFar] = handFar;
        j[(int)Joint.FootNear] = footNear;
        j[(int)Joint.FootFar] = footFar;
        Solve(j);
    }

    /// <summary>Overrides some end effectors / the torso of an already placed pose (used by attacks on top of a stance).</summary>
    private static void Retarget(Vector2[] j, Vector2? handNear = null, Vector2? handFar = null, Vector2? footNear = null, float? lean = null, float shift = 0f)
    {
        var hip = j[(int)Joint.Hip];
        hip.X += shift;
        float torso = (j[(int)Joint.Neck] - j[(int)Joint.Hip]).Length();
        float angle = lean ?? Mathf.Atan2(j[(int)Joint.Neck].X - j[(int)Joint.Hip].X, j[(int)Joint.Neck].Y - j[(int)Joint.Hip].Y);
        var up = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        var neck = hip + (up * torso);
        var shoulder = hip + (up * (torso * 0.88f));

        j[(int)Joint.Hip] = hip;
        j[(int)Joint.Neck] = neck;
        j[(int)Joint.Head] = neck + (new Vector2(Mathf.Sin(angle * 0.6f), Mathf.Cos(angle * 0.6f)) * (0.14f + HeadRadius));
        j[(int)Joint.ShoulderNear] = shoulder;
        j[(int)Joint.ShoulderFar] = shoulder + new Vector2(-0.06f, 0f);
        if (handNear is { } hn) j[(int)Joint.HandNear] = hn;
        if (handFar is { } hf) j[(int)Joint.HandFar] = hf;
        if (footNear is { } fn) j[(int)Joint.FootNear] = fn;
        Solve(j);
    }

    /// <summary>Solves the elbows and knees from the shoulders/hip and the end effectors (two-bone IK with a stretch limit).</summary>
    private static void Solve(Vector2[] j)
    {
        j[(int)Joint.ElbowNear] = TwoBone(j[(int)Joint.ShoulderNear], ref j[(int)Joint.HandNear], UpperArm, Forearm, -1f);
        j[(int)Joint.ElbowFar] = TwoBone(j[(int)Joint.ShoulderFar], ref j[(int)Joint.HandFar], UpperArm, Forearm, -1f);
        j[(int)Joint.KneeNear] = TwoBone(j[(int)Joint.Hip], ref j[(int)Joint.FootNear], Thigh, Shin, +1f);
        j[(int)Joint.KneeFar] = TwoBone(j[(int)Joint.Hip], ref j[(int)Joint.FootFar], Thigh, Shin, +1f);
    }

    /// <summary>
    /// Returns the middle joint of a two-bone chain from <paramref name="root"/> to <paramref name="end"/>. A target beyond the
    /// natural reach is honoured by stretching the bones (rubber-hose style, capped), so a long move still visibly lands
    /// on its hitbox; beyond the cap the end joint is pulled back onto the stretched limb.
    /// </summary>
    private static Vector2 TwoBone(Vector2 root, ref Vector2 end, float l1, float l2, float bend)
    {
        var d = end - root;
        float dist = Mathf.Max(d.Length(), 0.001f);
        float reach = l1 + l2;
        if (dist > reach)
        {
            float stretch = Mathf.Min(dist / reach, MaxStretch);
            l1 *= stretch;
            l2 *= stretch;
            if (dist > l1 + l2)
            {
                dist = l1 + l2;
                end = root + (d / d.Length() * dist);
            }
        }

        dist = Mathf.Min(dist, l1 + l2 - 0.001f);
        float along = (((l1 * l1) - (l2 * l2)) + (dist * dist)) / (2f * dist);
        float h = Mathf.Sqrt(Mathf.Max(0f, (l1 * l1) - (along * along)));
        var dir = d / Mathf.Max(d.Length(), 0.001f);
        var perp = new Vector2(-dir.Y, dir.X) * bend;
        return root + (dir * along) + (perp * h);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}

/// <summary>Exponentially smooths a pose over time so state changes read as motion rather than teleports.</summary>
public sealed class PoseSmoother
{
    private readonly Vector2[] _current = new Vector2[FighterPoser.JointCount];
    private bool _primed;

    public Vector2[] Pose => _current;

    /// <summary>Moves the smoothed pose toward <paramref name="target"/>; <paramref name="k"/> = 1 snaps to it.</summary>
    public void Blend(Vector2[] target, float k)
    {
        if (!_primed || k >= 1f)
        {
            Array.Copy(target, _current, _current.Length);
            _primed = true;
            return;
        }

        for (int i = 0; i < _current.Length; i++) _current[i] = _current[i].Lerp(target[i], k);
    }

    /// <summary>Forget the previous pose (new round, rematch, rollback jump): the next blend snaps.</summary>
    public void Reset() => _primed = false;
}
