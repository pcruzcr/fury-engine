using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Memory;
using Fury.Core.Observability;

namespace Fury.Core.Sim;

/// <summary>
/// The deterministic fight: orchestrates the fixed 9-phase pipeline over arena memory.
/// All state lives in <see cref="FightState"/> (SoA, ADR-002) plus the optional <see cref="ISimModule"/>s;
/// no heap allocation happens inside <see cref="Step"/>. Identical input sequences produce bit-identical
/// state hashes on any platform: the engine's core contract.
/// Pipeline: InputRead → Buffer → Command → FSM Commit → Movement → HitDetection → Resolution+Energy →
/// Modules → Snapshot → Events.
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
    private readonly ISimModule[] _modules;
    private readonly bool[] _connects = new bool[FightState.FighterCount];
    private readonly bool[] _projectileHits = new bool[FightState.ProjectileSlots];
    private readonly int[] _snapshot;
    private readonly int[] _ringScratch = new int[InputBuffer.History];

    public FightSimulation(
        CharacterConfig p1,
        CharacterConfig p2,
        FightConfig? cfg = null,
        IFightTracer? tracer = null,
        int arenaCapacityBytes = 64 * 1024,
        IReadOnlyList<ISimModule>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(p1);
        ArgumentNullException.ThrowIfNull(p2);
        _cfg = cfg ?? FightConfig.Default();
        _modules = modules is null ? [] : [.. modules];

        int moduleWords = 0;
        foreach (var module in _modules) moduleWords += module.StateWords;
        SnapshotWords = FightState.WordCount + moduleWords;
        _snapshot = new int[SnapshotWords];

        var arena = new Arena(arenaCapacityBytes);
        State = new FightState(arena);
        _events = new SimEventSink(tracer);
        var hooks = new ModuleHooks(_modules);
        var roster = new FighterRoster(p1, p2, hooks);
        _fsm = new FighterStateMachine(_cfg, roster, _events, hooks);
        _movement = new MovementSystem(_cfg, roster, _events, hooks);
        _hitDetection = new HitDetectionSystem(_fsm);
        _combat = new CombatResolutionSystem(_cfg, _fsm, _buffers, hooks);
        _tracer = tracer;
        State.SetHealth(0, p1.MaxHealth);
        State.SetHealth(1, p2.MaxHealth);
        State.SetRoundTimer(_cfg.RoundSeconds * 60);
        foreach (var module in _modules)
        {
            if (module is SimModule hooked) hooked.Attach(this);
        }
        WriteSnapshot();
    }

    public FightState State { get; }

    /// <summary>The input ring of a fighter (modules read presses from here; read-only by convention).</summary>
    public InputBuffer Input(int f) => _buffers[f];

    /// <summary>Returns the first attached module of type <typeparamref name="T"/>, or null.</summary>
    public T? GetModule<T>() where T : class, ISimModule
    {
        foreach (var module in _modules)
        {
            if (module is T match) return match;
        }
        return null;
    }

    /// <summary>Emits a simulation event on behalf of a module (presented once per frame, rollback-safe).</summary>
    public void EmitEvent(int eventId, ulong payload) => _events.Emit(State.Frame, eventId, payload);

    /// <summary>Total words of one snapshot: the base state plus every module (size every snapshot buffer with this).</summary>
    public int SnapshotWords { get; }

    /// <summary>Rollback counters (updated by the netcode runner) exposed to the tracer.</summary>
    public SimStats Stats { get; } = new();

    public int Frame => State.Frame;

    /// <summary>Rule constants of this fight (stage size, gravity, ...).</summary>
    public FightConfig Config => _cfg;

    /// <summary>Character data of a fighter slot (presentation reads stats from here).</summary>
    public CharacterConfig Data(int f) => _fsm.Data(f);

    /// <summary>Active hitbox of fighter <paramref name="f"/> in world units (debug overlays, training tools).</summary>
    public bool TryGetHitbox(int f, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY) =>
        _fsm.TryGetHitbox(State, f, out minX, out minY, out maxX, out maxY);

    /// <summary>Hurtbox of fighter <paramref name="f"/> in world units.</summary>
    public void GetHurtbox(int f, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY) =>
        _fsm.GetHurtbox(State, f, out minX, out minY, out maxX, out maxY);

    /// <summary>Hitbox of a live projectile slot in world units.</summary>
    public void GetProjectileBox(int slot, out Fp minX, out Fp minY, out Fp maxX, out Fp maxY) =>
        _fsm.GetProjectileBox(State, slot, out minX, out minY, out maxX, out maxY);

    /// <summary>Advances exactly one deterministic tick. The sim is RNG-free by design: all
    /// variance enters through inputs, so parity is purely a function of input streams.</summary>
    public void Step(InputState input1, InputState input2)
    {
        _events.BeginFrame(State.Frame);

        // Phases 1-2: input read + buffer push (SOCD cleaned).
        _buffers[0].Push(input1);
        _buffers[1].Push(input2);

        // Phase 3: command resolution against the character move tables.
        int move1 = ResolveCommand(0);
        int move2 = ResolveCommand(1);

        // Phase 4: FSM commit (timers, hitstop, move starts, cancels).
        _fsm.Commit(State, 0, move1);
        _fsm.Commit(State, 1, move2);

        // Phase 5: movement (ground control then integration + projectiles).
        _movement.HandleGroundControl(State, _buffers);
        _movement.Integrate(State);

        // Phase 6: hit detection (SIMD filter + exact confirm).
        _hitDetection.Detect(State, _connects, _projectileHits);

        // Phase 7: resolution + energy + round end.
        _combat.Resolve(State, _connects, _projectileHits, _events);

        // Optional modules advance after combat, in registration order.
        foreach (var module in _modules) module.Tick(State);

        // Global round clock runs only while the round is live.
        if (State.RoundState == 0) State.TickRoundTimer();

        State.TickFrame();

        // Phase 8: snapshot AFTER all state mutation of the tick (rollback contract).
        // Input rings are part of the savestate: without them, resimulation breaks edges.
        SyncBuffersIntoState();
        WriteSnapshot();

        // Phase 9: frame trace to observability.
        _tracer?.OnFrame(new FrameTrace(Frame, Stats.Rollbacks, Stats.ResimulatedFrames, SnapshotHash));
    }

    private int ResolveCommand(int f)
    {
        var ctx = new FighterContext(
            State.Frame,
            State.GetFacing(f),
            CommandResolver.StanceOf(State.IsAirborne(f), _buffers[f].Held(InputButton.Down)),
            State.GetExMeter(f),
            State.GetInputConsumed(f));
        return CommandResolver.Resolve(_buffers[f], _fsm.Data(f), ctx);
    }

    /// <summary>Read-only view of the fight from one fighter's perspective (pre-tick state).</summary>
    public FightView ViewFor(int f)
    {
        int other = 1 - f;
        return new FightView(
            State.GetStateId(f),
            State.GetStateId(other),
            State.GetMoveIndex(other),
            Fp.Abs(State.GetX(f) - State.GetX(other)).Raw,
            State.GetFacing(f))
        {
            MyExMeter = State.GetExMeter(f),
            OpponentAirborne = State.IsAirborne(other),
            OpponentFireballLive = State.ProjectileCount(other) > 0,
        };
    }

    /// <summary>Convenience for harnesses and arcade mode: poll providers against the pre-tick state, then step.</summary>
    public void StepWithProviders(IInputProvider p1, IInputProvider p2)
    {
        var v0 = ViewFor(0);
        var v1 = ViewFor(1);
        Step(p1.Poll(State.Frame, v0), p2.Poll(State.Frame, v1));
    }

    /// <summary>xxHash64 of the latest snapshot (state + modules): the per-frame determinism fingerprint.</summary>
    public ulong SnapshotHash => FpHash.HashInts(_snapshot);

    /// <summary>Read-only view of the latest serialized state (delta and rollback inputs).</summary>
    public ReadOnlySpan<int> SnapshotSpan => _snapshot;

    /// <summary>Rollback restore: overwrites all state (base + modules) from a snapshot span.</summary>
    public void Restore(ReadOnlySpan<int> snapshot)
    {
        if (snapshot.Length < SnapshotWords) throw new ArgumentException("Snapshot too small.", nameof(snapshot));
        State.Deserialize(snapshot);
        int offset = FightState.WordCount;
        foreach (var module in _modules)
        {
            module.Deserialize(snapshot.Slice(offset, module.StateWords));
            offset += module.StateWords;
        }
        SyncStateIntoBuffers();
        WriteSnapshot();
    }

    private void WriteSnapshot()
    {
        State.Serialize(_snapshot);
        int offset = FightState.WordCount;
        foreach (var module in _modules)
        {
            module.Serialize(_snapshot.AsSpan(offset, module.StateWords));
            offset += module.StateWords;
        }
    }

    private void SyncBuffersIntoState()
    {
        for (int f = 0; f < FightState.FighterCount; f++)
        {
            _buffers[f].CopyRing(_ringScratch, out var head);
            State.SetInputRing(f, head, _ringScratch);
        }
    }

    private void SyncStateIntoBuffers()
    {
        for (int f = 0; f < FightState.FighterCount; f++)
        {
            State.GetInputRing(f, _ringScratch, out var head);
            _buffers[f].LoadRing(_ringScratch, head);
        }
    }
}
