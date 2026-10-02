using Fury.Core.Sim;

namespace Fury.Core.Rollback;

/// <summary>
/// Deterministic two-endpoint rollback session (the sim-agnostic core of the netcode).
/// Protocol per frame f (both sides symmetric):
///   1. Local input for f is known immediately; a fixed input delay hides jitter.
///   2. Remote input for f is predicted (last confirmed) until the peer delivers it.
///   3. On delivery, if the true input differs from what was simulated, the session
///      schedules frames [f .. current] for resimulation.
/// The runner feeds the sim: GetLocal/GetRemote per frame, resimulates the scheduled
/// range from the StateStore snapshot, then clears the schedule.
/// </summary>
public sealed class RollbackSession
{
    private readonly InputState[] _remoteConfirmed;
    private readonly InputState[] _remotePredicted;
    private readonly InputState[] _localHistory;
    private readonly ulong[] _hashes;
    private readonly bool[] _confirmed;
    // Frame that currently owns each ring slot. Without it, after the ring wrapped
    // (capacity frames) a slot still said "confirmed" with the input of frame - capacity,
    // so PredictFor skipped prediction and the sim consumed a stale remote input.
    private readonly int[] _slotFrame;
    private readonly int _capacity;
    private int _confirmedThrough = -1;
    private int _resimFrom = int.MaxValue;
    private int _resimTo = -1;

    public RollbackSession(int inputDelay, int capacityFrames = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 8);
        InputDelay = inputDelay;
        _capacity = capacityFrames;
        _remoteConfirmed = new InputState[capacityFrames];
        _remotePredicted = new InputState[capacityFrames];
        _localHistory = new InputState[capacityFrames];
        _hashes = new ulong[capacityFrames];
        _confirmed = new bool[capacityFrames];
        _slotFrame = new int[capacityFrames];
        Array.Fill(_slotFrame, -1);
    }

    private int Slot(int frame) => ((frame % _capacity) + _capacity) % _capacity;

    /// <summary>Claims the slot for <paramref name="frame"/>, discarding data of an older frame.</summary>
    private int Claim(int frame)
    {
        int slot = Slot(frame);
        if (_slotFrame[slot] != frame)
        {
            _slotFrame[slot] = frame;
            _confirmed[slot] = false;
            _remoteConfirmed[slot] = default;
            _remotePredicted[slot] = default;
            _localHistory[slot] = default;
            _hashes[slot] = 0;
        }
        return slot;
    }

    private bool IsConfirmed(int frame) => _slotFrame[Slot(frame)] == frame && _confirmed[Slot(frame)];

    /// <summary>Fixed input delay in frames (jitter hiding budget).</summary>
    public int InputDelay { get; }

    /// <summary>Start of the pending resimulation range; int.MaxValue when idle.</summary>
    public int ResimFrom => _resimFrom;

    /// <summary>End (inclusive) of the pending resimulation range; -1 when idle.</summary>
    public int ResimTo => _resimTo;

    public bool NeedsResim => _resimTo >= 0;

    /// <summary>Highest frame N such that remote inputs of frames 0..N are all confirmed (-1 if none).</summary>
    public int ConfirmedThrough => _confirmedThrough;

    public void PushLocal(int frame, InputState local) => _localHistory[Claim(frame)] = local;

    public InputState GetLocal(int frame) => _slotFrame[Slot(frame)] == frame ? _localHistory[Slot(frame)] : default;

    /// <summary>Remote input to simulate for a frame: confirmed if known, else the prediction.</summary>
    public InputState GetRemote(int frame)
    {
        int slot = Slot(frame);
        if (_slotFrame[slot] != frame) return default;
        return _confirmed[slot] ? _remoteConfirmed[slot] : _remotePredicted[slot];
    }

    /// <summary>Seeds the prediction for a frame with the last confirmed remote input.</summary>
    public void PredictFor(int frame)
    {
        int slot = Claim(frame);
        if (_confirmed[slot]) return;
        InputState last = default;
        for (int back = 1; back <= 8 && frame - back >= 0; back++)
        {
            if (IsConfirmed(frame - back))
            {
                last = _remoteConfirmed[Slot(frame - back)];
                break;
            }
        }
        _remotePredicted[slot] = last;
    }

    /// <summary>
    /// Delivers the true remote input for a frame. Schedules resimulation only when the
    /// simulation already consumed something different (wrong prediction or stale confirm).
    /// </summary>
    public void DeliverRemote(int frame, InputState remote, int currentFrame)
    {
        // Too old to resimulate: its ring slot (and the runner's snapshot of frame - 1,
        // same capacity) already belongs to a newer frame. Drop it instead of corrupting.
        if (frame < 0 || frame <= currentFrame - (_capacity - 1)) return;
        int slot = Claim(frame);
        InputState simulated = _confirmed[slot] ? _remoteConfirmed[slot] : _remotePredicted[slot];
        bool wrong = simulated != remote;

        _remoteConfirmed[slot] = remote;
        _remotePredicted[slot] = remote;
        _confirmed[slot] = true;
        while (IsConfirmed(_confirmedThrough + 1)) _confirmedThrough++;

        if (wrong && frame <= currentFrame)
        {
            _resimFrom = System.Math.Min(_resimFrom, frame);
            _resimTo = System.Math.Max(_resimTo, currentFrame);
        }
    }

    public void RecordHash(int frame, ulong hash) => _hashes[Claim(frame)] = hash;

    /// <summary>Our recorded post-frame hash (for cross-peer verification); 0 if unknown.</summary>
    public ulong GetHash(int frame) => _slotFrame[Slot(frame)] == frame ? _hashes[Slot(frame)] : 0;

    /// <summary>Desync detector: compares our recorded hash for a frame with the peer's.</summary>
    public bool HashMatchesPeer(int frame, ulong peerHash) => _slotFrame[Slot(frame)] == frame && _hashes[Slot(frame)] == peerHash;

    public void ClearResim()
    {
        _resimFrom = int.MaxValue;
        _resimTo = -1;
    }
}
