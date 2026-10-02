using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Deterministic CPU brain: a pure function of (frame, view) driven by its own seeded xoshiro stream,
/// independent from the simulation (which is RNG-free). Same seed + same stream of views gives identical
/// behaviour on every platform, which is what makes blind AI-vs-AI parity harnesses possible (ADR-006).
/// It plays the whole move set the way a player does: motion inputs (fireballs, anti-air dragon punches)
/// are queued as frame-exact input sequences, normals are chained, throws and overhead command normals
/// mix up blockers, jump-ins use air normals, and it blocks reactively (standing or crouching).
/// </summary>
public sealed class CpuBrain : IInputProvider
{
    private const int QueueCapacity = 24;

    private readonly Xoshiro256StarStar _rng;
    private readonly int _aggression; // 1..10: probability weight of attacking in range
    private readonly ushort[] _queue = new ushort[QueueCapacity];
    private int _head;
    private int _count;
    private ushort _holdBits;
    private int _holdFrames;

    public CpuBrain(ulong seed, int aggression = 5)
    {
        _rng = new Xoshiro256StarStar(seed);
        _aggression = System.Math.Clamp(aggression, 1, 10);
    }

    public InputState Poll(int frame, in FightView view)
    {
        if (_count > 0) return new InputState(Dequeue());
        if (_holdFrames > 0)
        {
            _holdFrames--;
            return new InputState(_holdBits);
        }

        Decide(view);
        if (_count > 0) return new InputState(Dequeue());
        if (_holdFrames > 0)
        {
            _holdFrames--;
            return new InputState(_holdBits);
        }
        return new InputState(0);
    }

    // ------------------------------------------------------------------ decisions

    private void Decide(in FightView view)
    {
        ushort fwd = (ushort)(view.MyFacing > 0 ? InputButton.Right : InputButton.Left);
        ushort back = (ushort)(view.MyFacing > 0 ? InputButton.Left : InputButton.Right);
        const ushort down = (ushort)InputButton.Down;
        const ushort up = (ushort)InputButton.Up;
        int dist = view.DistanceRaw;
        int far = Fp.FromRatio(6, 1).Raw;
        int close = Fp.FromRatio(13, 5).Raw;
        int roll = _rng.NextInt(100);

        bool grounded = view.MyStateId is FighterStateId.Idle or FighterStateId.WalkForward or FighterStateId.WalkBack or FighterStateId.Crouch;
        if (!grounded)
        {
            // Hit, blocking or already acting: keep the guard up while being pressured.
            if (view.MyStateId == FighterStateId.Blockstun) Hold((ushort)(back | (roll < 40 ? down : 0)), 2);
            return;
        }

        // Super when available and the opponent is in range.
        if (view.MyExMeter >= 300 && dist < far && roll < 35)
        {
            Enqueue((ushort)InputButton.Macro2);
            return;
        }

        bool opponentAttacking = view.OpponentStateId == FighterStateId.Attack;
        if (opponentAttacking && dist < Fp.FromRatio(5, 1).Raw && _rng.NextInt(100) < 35 + (_aggression * 4))
        {
            // Block; crouch-block a third of the time so overheads sometimes land.
            Hold((ushort)(back | (_rng.NextInt(3) == 0 ? down : 0)), 8 + _rng.NextInt(10));
            return;
        }

        if (view.OpponentAirborne && dist < Fp.FromRatio(7, 2).Raw)
        {
            // Anti-air dragon punch: 6, 2, 3 + heavy.
            Enqueue(fwd, down, (ushort)(down | fwd), (ushort)(fwd | (ushort)InputButton.Heavy));
            return;
        }

        if (dist > far)
        {
            if (roll < 14 && !view.OpponentFireballLive) Fireball(fwd, down);
            else if (roll < 24) Dash(fwd);
            else if (roll < 30) JumpIn(fwd, up);
            else Hold(fwd, 6 + _rng.NextInt(10));
            return;
        }

        if (dist > close)
        {
            if (roll < 16 && !view.OpponentFireballLive) Fireball(fwd, down);
            else if (roll < 30) Dash(fwd);
            else if (roll < 42) Hold((ushort)(fwd | (ushort)InputButton.Heavy), 1); // overhead command normal
            else if (roll < 48) JumpIn(fwd, up);
            else Hold(fwd, 4 + _rng.NextInt(8));
            return;
        }

        // Close range: attack, or reposition.
        if (roll < 30 + (_aggression * 6))
        {
            int pick = _rng.NextInt(100);
            if (pick < 14) Combo((ushort)InputButton.Light, (ushort)InputButton.Medium);
            else if (pick < 26) Combo((ushort)InputButton.Light, (ushort)InputButton.Heavy);
            else if (pick < 36) Enqueue((ushort)InputButton.Heavy);
            else if (pick < 46) Enqueue((ushort)(down | (ushort)InputButton.Light));
            else if (pick < 54) Enqueue((ushort)(down | (ushort)InputButton.Heavy));
            else if (pick < 66) Enqueue((ushort)InputButton.Macro1); // throw
            else if (pick < 76) Hold((ushort)(fwd | (ushort)InputButton.Heavy), 1);
            else if (pick < 84) Enqueue((ushort)InputButton.Special);
            else if (pick < 92) Combo((ushort)InputButton.Light, 0, fireballFinish: true, fwd, down);
            else Enqueue((ushort)InputButton.Medium);
        }
        else if (roll < 75)
        {
            Hold(back, 5 + _rng.NextInt(10));
        }
        else if (roll < 80)
        {
            Backdash(back);
        }
        else if (roll > 94)
        {
            Hold(up, 1);
        }
        else
        {
            Hold(0, 4 + _rng.NextInt(6));
        }
    }

    private void Fireball(ushort fwd, ushort down) =>
        Enqueue(down, (ushort)(down | fwd), fwd, (ushort)(fwd | (ushort)InputButton.Special));

    /// <summary>Double tap forward: forward, release, forward (held so the dash keeps going into a walk).</summary>
    private void Dash(ushort fwd) => Enqueue(fwd, 0, fwd, fwd);

    private void Backdash(ushort back) => Enqueue(back, 0, back);

    /// <summary>Jump forward and press heavy on the way down (air normal).</summary>
    private void JumpIn(ushort fwd, ushort up)
    {
        Enqueue((ushort)(up | fwd));
        for (int i = 0; i < 9 + _rng.NextInt(6); i++) Enqueue(fwd);
        Enqueue((ushort)(fwd | (ushort)InputButton.Heavy));
    }

    /// <summary>First normal, a few neutral frames (so the second press is a fresh edge inside the cancel window), second normal or fireball.</summary>
    private void Combo(ushort first, ushort second, bool fireballFinish = false, ushort fwd = 0, ushort down = 0)
    {
        Enqueue(first);
        Enqueue(0, 0, 0, 0, 0);
        if (fireballFinish) Fireball(fwd, down);
        else Enqueue(second);
    }

    // ------------------------------------------------------------------ tiny input queue (allocation-free)

    private void Hold(ushort bits, int frames)
    {
        _holdBits = bits;
        _holdFrames = frames;
    }

    private void Enqueue(params ReadOnlySpan<ushort> inputs)
    {
        foreach (ushort bits in inputs)
        {
            if (_count == QueueCapacity) return;
            _queue[(_head + _count) % QueueCapacity] = bits;
            _count++;
        }
    }

    private ushort Dequeue()
    {
        ushort bits = _queue[_head];
        _head = (_head + 1) % QueueCapacity;
        _count--;
        return bits;
    }
}

/// <summary>Always-neutral provider: training dummy.</summary>
public sealed class DummyBrain : IInputProvider
{
    public InputState Poll(int frame, in FightView view) => new(0);
}

/// <summary>Replays a recorded input stream; out-of-range frames are neutral.</summary>
public sealed class ScriptedInputProvider(ReadOnlySpan<InputState> frames) : IInputProvider
{
    private readonly InputState[] _frames = frames.ToArray();

    public InputState Poll(int frame, in FightView view) =>
        (uint)frame < (uint)_frames.Length ? _frames[frame] : new InputState(0);
}
