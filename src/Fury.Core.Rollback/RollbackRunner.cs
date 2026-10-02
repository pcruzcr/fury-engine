using Fury.Core.Sim;

namespace Fury.Core.Rollback;

/// <summary>
/// Drives one side of a rollback netmatch: samples local input (applied after a fixed input
/// delay), predicts the remote, simulates, and on input mismatch resimulates the scheduled range
/// from the StateStore with corrected inputs. Deterministic end-state parity is the invariant
/// (gate F2: jitter + drops must converge to the offline hash). Zero allocation per frame.
/// <para>
/// Both peers MUST feed the simulation in the same slot order. <c>localSlot</c> says which
/// fighter this peer controls (0 = P1, 1 = P2); the remote input goes to the other slot. Without
/// it the second peer simulated with the players swapped and desynced from the first frame.
/// </para>
/// </summary>
public sealed class RollbackRunner
{
    /// <summary>Default prediction barrier: never run further ahead of confirmed remote input.</summary>
    public const int DefaultMaxPrediction = 8;

    private readonly FightSimulation _sim;
    private readonly RollbackSession _session;
    private readonly StateStore _store;
    private readonly Func<int, InputState> _localInput;
    private readonly int _localSlot;
    private readonly int[] _restoreBuffer;
    private readonly int[] _initialState;

    public RollbackRunner(
        FightSimulation sim,
        Func<int, InputState> localInput,
        int inputDelay,
        int localSlot = 0,
        int maxPrediction = DefaultMaxPrediction)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(localInput);
        ArgumentOutOfRangeException.ThrowIfNegative(inputDelay);
        if (localSlot is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(localSlot));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPrediction);

        _sim = sim;
        _restoreBuffer = new int[sim.SnapshotWords];
        _initialState = new int[sim.SnapshotWords];
        _localInput = localInput;
        _localSlot = localSlot;
        MaxPrediction = maxPrediction;
        _session = new RollbackSession(inputDelay);
        // Capacity must cover max jitter + input delay + resimulation window.
        _store = new StateStore(sim.SnapshotWords, capacityFrames: 64);
        _sim.SnapshotSpan.CopyTo(_initialState); // rollback target for frame 0 corrections
        Session = _session;
    }

    public RollbackSession Session { get; }

    /// <summary>Fighter slot this peer controls (0 = P1, 1 = P2).</summary>
    public int LocalSlot => _localSlot;

    /// <summary>Maximum frames the sim may run ahead of the last fully confirmed remote frame.</summary>
    public int MaxPrediction { get; }

    /// <summary>Frames simulated beyond the confirmed remote input (prediction depth).</summary>
    public int PredictionDepth => _sim.Frame - 1 - _session.ConfirmedThrough;

    /// <summary>True when advancing one more frame stays within the prediction barrier.</summary>
    public bool CanAdvance => PredictionDepth < MaxPrediction;

    /// <summary>Last frame whose local input is already known (current frame + input delay).</summary>
    public int LatestLocalFrame => _sim.Frame + _session.InputDelay - 1;

    /// <summary>
    /// Local input labelled by the frame it applies to; this is what goes on the wire. Valid for
    /// frames already sampled (<see cref="LatestLocalFrame"/> and the retransmission window).
    /// </summary>
    public InputState LocalInput(int appliedFrame) => _session.GetLocal(appliedFrame);

    /// <summary>
    /// Provides the local input of a frame that was sampled before this runner existed (the first <c>inputDelay</c>
    /// frames of a round were sampled while the previous round was still running). Call before the first advance.
    /// </summary>
    public void SeedLocalInput(int frame, InputState input)
    {
        if (_sim.Frame != 0) throw new InvalidOperationException("Local inputs can only be seeded before the first frame.");
        _session.PushLocal(frame, input);
    }

    /// <summary>
    /// Advances one frame if the prediction barrier allows it. Returns false (and does nothing)
    /// when the peer is too far ahead: the caller should stall and keep delivering remote input.
    /// </summary>
    public bool TryAdvance()
    {
        if (!CanAdvance) return false;
        Advance(_sim.Frame);
        return true;
    }

    /// <summary>Advances one frame: sample + schedule local input, predict, simulate, persist snapshot + hash.</summary>
    public void Advance(int frame)
    {
        if (frame != _sim.Frame)
        {
            throw new ArgumentException($"Advance({frame}) out of sequence: the simulation is at frame {_sim.Frame}.", nameof(frame));
        }

        // Input sampled now takes effect InputDelay frames later (hides network jitter).
        _session.PushLocal(frame + _session.InputDelay, _localInput(frame));
        _session.PredictFor(frame);

        StepFrame(frame);
        _store.Save(frame, _sim.SnapshotSpan);
        _session.RecordHash(frame, _sim.SnapshotHash);
    }

    /// <summary>
    /// Delivers a remote input packet; resimulates the affected range when the simulation
    /// consumed something different. Frame 0 rolls back onto the initial state snapshot.
    /// </summary>
    public void DeliverRemote(int frame, InputState remote)
    {
        _session.DeliverRemote(frame, remote, _sim.Frame - 1);
        if (!_session.NeedsResim) return;

        int from = _session.ResimFrom;
        int to = _session.ResimTo;

        if (from == 0)
        {
            _sim.Restore(_initialState);
        }
        else if (from <= to)
        {
            if (!_store.TryLoad(from - 1, _restoreBuffer))
            {
                throw new InvalidOperationException(
                    $"Cannot resimulate: state for frame {from - 1} has been overwritten. " +
                    "Increase StateStore capacity or reduce input delay.");
            }
            _sim.Restore(_restoreBuffer);
        }

        if (from <= to)
        {
            _sim.Stats.RecordRollback(to - System.Math.Max(from, 0) + 1);
            for (int f = System.Math.Max(from, 0); f <= to; f++)
            {
                StepFrame(f);
                _store.Save(f, _sim.SnapshotSpan);
                _session.RecordHash(f, _sim.SnapshotHash);
            }
        }
        _session.ClearResim();
    }

    /// <summary>
    /// Hash of a frame whose remote input is fully confirmed (safe to compare with the peer to
    /// detect desyncs); 0 when the frame is still speculative or unknown.
    /// </summary>
    public ulong ConfirmedHash(int frame) => frame <= _session.ConfirmedThrough ? _session.GetHash(frame) : 0;

    /// <summary>Feeds the sim in canonical slot order (P1, P2) regardless of which side is local.</summary>
    private void StepFrame(int frame)
    {
        InputState local = _session.GetLocal(frame);
        InputState remote = _session.GetRemote(frame);
        if (_localSlot == 0) _sim.Step(local, remote);
        else _sim.Step(remote, local);
    }
}
