using Fury.Core.Math;
using Fury.Core.Memory;
using Fury.Core.Observability;

namespace Fury.Core.Sim;

/// <summary>
/// The deterministic fight: orchestrates the fixed 9-phase pipeline over arena memory.
/// All state lives in <see cref="FightState"/> (SoA, ADR-002) plus reconstructible input
/// buffers; no heap allocation happens inside <see cref="Step"/>. Identical input sequences
/// produce bit-identical state hashes on any platform — the engine's core contract.
/// Pipeline: InputRead → Buffer → Command → FSM Commit → Movement → HitDetection →
/// Resolution+Energy → Snapshot → Events.
/// </summary>
public sealed class FightSimulation
{
    private readonly FightConfig _cfg;
    private readonly FighterStateMachine _fsm;
    private readonly MovementSystem _movement;
    private readonly HitDetectionSystem _hitDetection;
    private readonly CombatResolutionSystem _combat;
    private readonly SimEventSink _events;
    private readonly InputBuffer[] _buffers = [new InputBuffer(), new InputBuffer()];
    private readonly IFightTracer? _tracer;
    private readonly bool[] _connects = new bool[FightState.FighterCount];
    private readonly int[] _snapshot = new int[FightState.WordCount];

    public FightSimulation(
        CharacterConfig p1,
        CharacterConfig p2,
        FightConfig? cfg = null,
        IFightTracer? tracer = null,
        int arenaCapacityBytes = 64 * 1024)
    {
        _cfg = cfg ?? FightConfig.Default();
        var arena = new Arena(arenaCapacityBytes);
        State = new FightState(arena);
        _fsm = new FighterStateMachine(_cfg, p1, p2);
        _movement = new MovementSystem(_cfg, p1, p2);
        _hitDetection = new HitDetectionSystem(_fsm);
        _combat = new CombatResolutionSystem(_cfg, _fsm, _buffers);
        _events = new SimEventSink(tracer);
        _tracer = tracer;
        State.SetHealth(0, p1.MaxHealth);
        State.SetHealth(1, p2.MaxHealth);
        State.SetRoundTimer(_cfg.RoundSeconds * 60);
        State.Serialize(_snapshot);
    }

    public FightState State { get; }

    public int Frame => State.Frame;

    /// <summary>Character data of a fighter slot (presentation reads stats from here).</summary>
    public CharacterConfig Data(int f) => _fsm.Data(f);

    /// <summary>Advances exactly one deterministic tick. The sim is RNG-free by design: all
    /// variance enters through inputs, so parity is purely a function of input streams.</summary>
    public void Step(InputState input1, InputState input2)
    {
        // Phases 1-2: input read + buffer push.
        _buffers[0].Push(input1);
        _buffers[1].Push(input2);

        // Phase 3: command resolution (meter gating via the read-only context).
        var intent1 = CommandResolver.Resolve(_buffers[0], Context(0));
        var intent2 = CommandResolver.Resolve(_buffers[1], Context(1));

        // Phase 4: FSM commit (timers, hitstop, intents).
        _fsm.Commit(State, 0, intent1);
        _fsm.Commit(State, 1, intent2);

        // Phase 5: movement (ground control then integration).
        _movement.HandleGroundControl(State, _buffers);
        _movement.Integrate(State);

        // Phase 6: hit detection (SIMD filter + exact confirm).
        _hitDetection.Detect(State, _connects);

        // Phase 7: resolution + energy + round end.
        _combat.Resolve(State, _connects, _events);

        // Global round clock runs only while the round is live.
        if (State.RoundState == 0)
        {
            State.TickRoundTimer();
        }

        State.TickFrame();

        // Phase 8: snapshot AFTER all state mutation of the tick (rollback contract).
        State.Serialize(_snapshot);

        // Phase 9: frame trace to observability.
        _tracer?.OnFrame(new FrameTrace(Frame, 0, 0, SnapshotHash));
    }

    private FighterContext Context(int f) => new(State.GetExMeter(f) / 100);

    /// <summary>Read-only view of the fight from one fighter's perspective (pre-tick state).</summary>
    public FightView ViewFor(int f)
    {
        int other = 1 - f;
        return new FightView(
            State.Frame,
            State.GetHealth(f),
            State.GetHealth(other),
            State.GetStateId(f),
            State.GetStateId(other),
            State.GetMoveIndex(other),
            Fp.Abs(State.GetX(f) - State.GetX(other)).Raw,
            State.GetFacing(f));
    }

    /// <summary>Convenience for harnesses and arcade mode: poll providers against the pre-tick state, then step.</summary>
    public void StepWithProviders(IInputProvider p1, IInputProvider p2)
    {
        var v0 = ViewFor(0);
        var v1 = ViewFor(1);
        Step(p1.Poll(State.Frame, v0), p2.Poll(State.Frame, v1));
    }

    /// <summary>xxHash64 of the latest snapshot — the per-frame determinism fingerprint.</summary>
    public ulong SnapshotHash => FpHash.HashInts(_snapshot.AsSpan(0, FightState.WordCount));

    /// <summary>Read-only view of the latest serialized state (delta and rollback inputs).</summary>
    public ReadOnlySpan<int> SnapshotSpan => _snapshot.AsSpan(0, FightState.WordCount);

    /// <summary>Rollback restore: overwrites all state from a snapshot span.</summary>
    public void Restore(ReadOnlySpan<int> snapshot)
    {
        State.Deserialize(snapshot);
        State.Serialize(_snapshot);
    }
}
