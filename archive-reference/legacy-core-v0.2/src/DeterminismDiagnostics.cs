// Fighter.Core — determinism diagnostics (REFACTOR+HARDEN).
// Typed failure modes (all derive InvalidOperationException, so existing
// `catch (InvalidOperationException)` / Assert.Throws<InvalidOperationException>
// call sites keep working) + DEBUG-only execution guards with zero RELEASE cost.
namespace Fighter.Core;

/// <summary>Snapshot schema/version failure (replaces generic
/// <see cref="InvalidOperationException"/> at schema boundaries).</summary>
public sealed class SnapshotSchemaException : InvalidOperationException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Human-readable cause (schema ids, offsets, sizes).</param>
    public SnapshotSchemaException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner cause.</summary>
    /// <param name="message">Human-readable cause.</param>
    /// <param name="inner">Underlying failure.</param>
    public SnapshotSchemaException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Replay file corruption/failure (magic, version, schema, lengths,
/// truncation, compression integrity, footer hash, ids). Never a silent
/// default snapshot, empty replay, or zero hash.</summary>
public sealed class ReplayCorruptException : InvalidOperationException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Human-readable cause (which check failed, where).</param>
    public ReplayCorruptException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner cause.</summary>
    /// <param name="message">Human-readable cause.</param>
    /// <param name="inner">Underlying failure.</param>
    public ReplayCorruptException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Determinism hash mismatch at a specific frame.</summary>
public sealed class DeterminismViolationException : InvalidOperationException
{
    /// <summary>Simulation frame where the hashes diverged.</summary>
    public int Frame { get; }

    /// <summary>Expected hash.</summary>
    public ulong Expected { get; }

    /// <summary>Actual hash.</summary>
    public ulong Actual { get; }

    /// <summary>Creates the violation record.</summary>
    /// <param name="frame">Frame of divergence.</param>
    /// <param name="expected">Expected hash.</param>
    /// <param name="actual">Actual hash.</param>
    public DeterminismViolationException(int frame, ulong expected, ulong actual)
        : base($"Determinism violation at frame {frame}: expected 0x{expected:X16}, actual 0x{actual:X16}.")
    {
        Frame = frame;
        Expected = expected;
        Actual = actual;
    }
}

/// <summary>Hash recorder + equality gate for determinism checks. The diagnostic
/// hash is independent of replay integrity policy, security hashing, and
/// network authentication.</summary>
public sealed class DeterminismProbe
{
    private readonly System.Collections.Generic.List<(int Frame, ulong Hash)> _frames = new();

    /// <summary>Recorded frame count.</summary>
    public int Count => _frames.Count;

    /// <summary>Records one frame hash.</summary>
    /// <param name="hash">State hash for the frame.</param>
    /// <param name="frame">Simulation frame number.</param>
    public void RecordFrame(ulong hash, int frame) => _frames.Add((frame, hash));

    /// <summary>Recorded hash at index <paramref name="index"/> (record order).</summary>
    /// <param name="index">Record order index.</param>
    /// <returns>The (frame, hash) pair.</returns>
    public (int Frame, ulong Hash) GetRecord(int index) => _frames[index];

    /// <summary>Asserts hash equality for <paramref name="frame"/>.</summary>
    /// <param name="expected">Expected hash.</param>
    /// <param name="actual">Actual hash.</param>
    /// <param name="frame">Simulation frame number (reported on failure).</param>
    /// <exception cref="DeterminismViolationException">Thrown on mismatch.</exception>
    public void Assert(ulong expected, ulong actual, int frame)
    {
        if (expected != actual) throw new DeterminismViolationException(frame, expected, actual);
    }
}

/// <summary>Marks types/methods that must run on the single simulation thread.
/// Enforced in DEBUG by <see cref="SingleThreadGuard"/>; documentation in RELEASE.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, Inherited = false)]
public sealed class SingleThreadedAttribute : Attribute
{
}

/// <summary>DEBUG-only single-thread affinity guard. RELEASE cost is zero
/// (methods are [Conditional("DEBUG")]: call sites vanish from RELEASE IL).</summary>
/// <remarks>Process-wide capture: the first captured thread owns the sim.
/// Multi-sim test runners must not share guarded sims across threads; each
/// test's sim is used on one thread, which is the supported pattern.</remarks>
public static class SingleThreadGuard
{
    private static int _ownerThreadId = -1;

    /// <summary>Captures the current thread as the simulation owner (first call wins).</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Capture()
    {
        int me = Environment.CurrentManagedThreadId;
        int seen = System.Threading.Interlocked.CompareExchange(ref _ownerThreadId, me, -1);
        if (seen != -1 && seen != me)
            throw new InvalidOperationException($"Simulation used from thread {me}; owner is thread {seen}.");
    }

    /// <summary>Asserts the current thread is the captured owner.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Check()
    {
        int me = Environment.CurrentManagedThreadId;
        if (_ownerThreadId != -1 && _ownerThreadId != me)
            throw new InvalidOperationException($"Simulation used from thread {me}; owner is thread {_ownerThreadId}.");
    }

    /// <summary>Releases ownership (test teardown / deliberate hand-off).</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Release() => _ownerThreadId = -1;
}

/// <summary>DEBUG-only simulation phase-order enforcer. A correct simulation
/// never depends on this type for its result; it only detects out-of-order
/// calls during development. RELEASE cost is zero.</summary>
/// <remarks>Official phase order: InputRead → InputBuffer → StateMachine →
/// CommandNormal → AabbSimulation → HitDetection → EnergySystem → Snapshot →
/// RenderSignal. Phases must be entered in order; <see cref="ResetFrame"/>
/// starts a new frame.</remarks>
public sealed class SimulationLoopEnforcer
{
    /// <summary>Simulation pipeline phases in official order.</summary>
    public enum Phase
    {
        /// <summary>No phase entered yet (frame start).</summary>
        None = 0,
        /// <summary>Phase 1: input read (IInputProvider only).</summary>
        InputRead = 1,
        /// <summary>Phase 2: RingBuffer.Push().</summary>
        InputBuffer = 2,
        /// <summary>Phase 3: FSM.Update().</summary>
        StateMachine = 3,
        /// <summary>Phase 4: CommandNormalResolver.Resolve().</summary>
        CommandNormal = 4,
        /// <summary>Phase 5: PhysicsStep() with fixed vectors.</summary>
        AabbSimulation = 5,
        /// <summary>Phase 6: HitboxManager.CheckHits().</summary>
        HitDetection = 6,
        /// <summary>Phase 7: EnergySystem.Update().</summary>
        EnergySystem = 7,
        /// <summary>Phase 8: StateSnapshot.Capture().</summary>
        Snapshot = 8,
        /// <summary>Phase 9: presentation event/delegate (no Godot types in Core).</summary>
        RenderSignal = 9,
    }

#if DEBUG
    private Phase _current = Phase.None;
#endif

    /// <summary>Enters <paramref name="phase"/>; must be the immediate successor of the current phase.</summary>
    /// <param name="phase">Phase to enter.</param>
    /// <exception cref="InvalidOperationException">Thrown in DEBUG on out-of-order entry.</exception>
    [System.Diagnostics.Conditional("DEBUG")]
    public void Enter(Phase phase)
    {
#if DEBUG
        if ((int)phase != (int)_current + 1)
            throw new InvalidOperationException($"Simulation phase out of order: entered {phase} after {_current}.");
        _current = phase;
#endif
    }

    /// <summary>Resets to frame start (call once per simulated frame).</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public void ResetFrame()
    {
#if DEBUG
        _current = Phase.None;
#endif
    }
}
