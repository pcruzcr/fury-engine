using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Deterministic CPU brain: a pure function of (frame, view) driven by its own seeded
/// xoshiro stream — independent from the sim's (RNG-free) state. Same seed + same input
/// stream of views ⇒ identical behavior on every platform, which is what makes blind
/// AI-vs-AI parity harnesses possible (ADR-006).
/// Policy: approach, attack in range with seeded weight, occasionally jump, block reactively.
/// </summary>
public sealed class CpuBrain : IInputProvider
{
    private readonly Xoshiro256StarStar _rng;
    private readonly int _aggression; // 1..10: probability weight of attacking in range

    public CpuBrain(ulong seed, int aggression = 5)
    {
        _rng = new Xoshiro256StarStar(seed);
        _aggression = System.Math.Clamp(aggression, 1, 10);
    }

    public InputState Poll(int frame, in FightView view)
    {
        ushort bits = 0;
        int dist = view.DistanceRaw; // raw Q16.16 of |dx|
        int inRange = Fp.FromRatio(3, 1).Raw; // inside max poke reach

        if (dist > inRange)
        {
            bits |= (ushort)(view.MyFacing > 0 ? InputButton.Right : InputButton.Left);
        }
        else if (view.OpponentStateId == FighterStateId.Attack && view.OpponentMoveIndex != MoveSlot.Super)
        {
            bits |= (ushort)(view.MyFacing > 0 ? InputButton.Left : InputButton.Right); // hold back: block
        }
        else
        {
            int roll = _rng.NextInt(100);
            if (roll < _aggression * 6)
            {
                bits |= roll switch
                {
                    < 18 => (ushort)InputButton.Light,
                    < 34 => (ushort)InputButton.Medium,
                    < 46 => (ushort)InputButton.Heavy,
                    < 52 => (ushort)InputButton.Special,
                    < 56 => (ushort)(InputButton.Down | InputButton.Heavy),
                    _ => (ushort)InputButton.Light,
                };
            }
            else if (roll > 94)
            {
                bits |= (ushort)InputButton.Up;
            }
        }

        return new InputState(bits);
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
