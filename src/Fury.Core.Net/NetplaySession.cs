using Fury.Core.Rollback;
using Fury.Core.Sim;

namespace Fury.Core.Net;

/// <summary>Health of a netplay session.</summary>
public enum NetplayStatus
{
    /// <summary>Simulating normally (possibly predicting).</summary>
    Running = 0,

    /// <summary>Waiting for remote input: the prediction barrier is reached.</summary>
    Stalled = 1,

    /// <summary>Peers computed different states for the same confirmed frame: the match is invalid.</summary>
    Desynced = 2,
}

/// <summary>
/// Engine-agnostic netplay driver (no Godot): glues a <see cref="RollbackRunner"/> to an
/// <see cref="INetTransport"/>. Call <see cref="Tick"/> once per 60 Hz simulation tick on both peers:
/// it delivers newly arrived remote inputs (resimulating on misprediction), advances the sim while
/// the prediction barrier allows, then sends a redundant input window starting at the peer's last
/// ack plus a confirmed-state fingerprint. Loss/jitter heal through redundancy; divergence is
/// detected, never silent.
/// <para>
/// A whole match (several rounds) runs on ONE continuous network timeline. Each round is a fresh
/// simulation whose local frame 0 sits at network frame <see cref="NetFrameBase"/>; <see cref="StartNextRound"/>
/// swaps the simulation without touching the transport, so frames of the previous round that the peer still
/// misses keep being retransmitted from a local history (a restart of the frame counter would let the two
/// peers wait for each other forever).
/// </para>
/// </summary>
public sealed class NetplaySession
{
    /// <summary>Confirmed-state fingerprints are exchanged for frames that are multiples of this.</summary>
    public const int CheckInterval = 30;

    private const int HistoryFrames = 256;

    private readonly INetTransport _transport;
    private readonly Func<int, InputState> _localInput;
    private readonly int _localSlot;
    private readonly int _inputDelay;
    private readonly int _maxPrediction;

    // Our own inputs by NETWORK frame, kept across rounds so unacknowledged frames can always be re-sent.
    private readonly ushort[] _historyBits = new ushort[HistoryFrames];
    private readonly int[] _historyFrame = new int[HistoryFrames];

    private FightSimulation _sim;
    private RollbackRunner _runner;
    private int _nextRemoteFrame; // first remote LOCAL frame not yet delivered to the runner
    private int _recordedThrough = -1; // highest local frame whose input was copied to the history

    public NetplaySession(
        FightSimulation sim,
        INetTransport transport,
        Func<int, InputState> localInput,
        int localSlot,
        int inputDelay = 2,
        int maxPrediction = RollbackRunner.DefaultMaxPrediction,
        int netFrameBase = 0)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(localInput);
        ArgumentOutOfRangeException.ThrowIfNegative(netFrameBase);
        _sim = sim;
        _transport = transport;
        _localInput = localInput;
        _localSlot = localSlot;
        _inputDelay = inputDelay;
        _maxPrediction = maxPrediction;
        NetFrameBase = netFrameBase;
        Array.Fill(_historyFrame, -1);
        _runner = new RollbackRunner(sim, localInput, inputDelay, localSlot, maxPrediction);
        RecordLocalHistory();
    }

    public RollbackRunner Runner => _runner;

    public NetplayStatus Status { get; private set; } = NetplayStatus.Running;

    /// <summary>Network frame of the current simulation's frame 0.</summary>
    public int NetFrameBase { get; private set; }

    /// <summary>Local frame at which a desync was detected (-1 while healthy).</summary>
    public int DesyncFrame { get; private set; } = -1;

    /// <summary>Local frame of the current simulation.</summary>
    public int Frame => _sim.Frame;

    public int RttTicks => _transport.RttTicks;

    /// <summary>
    /// Replaces the finished simulation with a fresh one whose frame 0 is network frame <paramref name="netFrameBase"/>.
    /// Both peers must use the same base (derive it from confirmed state, e.g. the round end frame) and call this only
    /// after every frame of the previous round up to that base has been confirmed.
    /// </summary>
    public void StartNextRound(FightSimulation newSim, int netFrameBase)
    {
        ArgumentNullException.ThrowIfNull(newSim);
        if (netFrameBase != NetFrameBase + _sim.Frame)
        {
            throw new ArgumentOutOfRangeException(nameof(netFrameBase), "The next round must start exactly where the previous one stopped (same network frame on both peers).");
        }
        _sim = newSim;
        NetFrameBase = netFrameBase;
        _nextRemoteFrame = 0;
        _recordedThrough = -1;
        _runner = new RollbackRunner(newSim, _localInput, _inputDelay, _localSlot, _maxPrediction);

        // The first inputDelay inputs of the new round were already sampled (and sent) while the old round ran.
        for (int f = 0; f < _inputDelay; f++)
        {
            int net = netFrameBase + f;
            int slot = net % HistoryFrames;
            _runner.SeedLocalInput(f, new InputState(_historyFrame[slot] == net ? _historyBits[slot] : (ushort)0));
        }

        Status = NetplayStatus.Running;
        DesyncFrame = -1;
        RecordLocalHistory();
    }

    /// <summary>One simulation tick. Returns true when the simulation advanced a frame.</summary>
    public bool Tick()
    {
        if (Status == NetplayStatus.Desynced)
        {
            Send();
            return false;
        }

        DeliverPending();
        CheckDesync();
        if (Status == NetplayStatus.Desynced) return false;

        bool advanced = _runner.TryAdvance();
        if (advanced) RecordLocalHistory();
        Status = advanced ? NetplayStatus.Running : NetplayStatus.Stalled;
        Send();
        return advanced;
    }

    /// <summary>
    /// Keep-alive without simulating: delivers inputs, checks for desync and re-sends the window.
    /// Use while the local sim is intentionally paused (pause menu, end of match) so the peer keeps converging.
    /// </summary>
    public void Pump()
    {
        if (Status != NetplayStatus.Desynced)
        {
            DeliverPending();
            CheckDesync();
        }
        Send();
    }

    private void DeliverPending()
    {
        // Strictly in frame order: the runner resimulates from the earliest mispredicted frame.
        while (_transport.Remote.TryGet(NetFrameBase + _nextRemoteFrame, out ushort bits))
        {
            _runner.DeliverRemote(_nextRemoteFrame, new InputState(bits));
            _nextRemoteFrame++;
        }
    }

    private void CheckDesync()
    {
        if (!_transport.Remote.TryGetPeerCheck(out int netFrame, out ulong peerHash)) return;
        int frame = netFrame - NetFrameBase;
        if (frame < 0) return; // a fingerprint of the previous round
        ulong mine = _runner.ConfirmedHash(frame);
        if (mine != 0 && mine != peerHash)
        {
            Status = NetplayStatus.Desynced;
            DesyncFrame = frame;
        }
    }

    /// <summary>Copies every local input the runner knows (up to current frame + input delay) into the network-frame history.</summary>
    private void RecordLocalHistory()
    {
        int latest = _runner.LatestLocalFrame;
        for (int f = System.Math.Max(0, _recordedThrough + 1); f <= latest; f++)
        {
            int net = NetFrameBase + f;
            int slot = net % HistoryFrames;
            _historyFrame[slot] = net;
            _historyBits[slot] = _runner.LocalInput(f).Bits;
        }
        if (latest > _recordedThrough) _recordedThrough = latest;
    }

    private void Send()
    {
        int latestNet = NetFrameBase + _runner.LatestLocalFrame;
        if (latestNet < 0) return;

        int first = System.Math.Max(0, System.Math.Max(_transport.Remote.PeerAck + 1, latestNet - (InputPacket.MaxFrames - 1)));
        if (first > latestNet) first = latestNet; // nothing new to say: heartbeat the newest frame (carries the ack)
        int count = latestNet - first + 1;

        Span<ushort> bits = stackalloc ushort[InputPacket.MaxFrames];
        for (int i = 0; i < count; i++)
        {
            int net = first + i;
            int slot = net % HistoryFrames;
            bits[i] = _historyFrame[slot] == net ? _historyBits[slot] : (ushort)0;
        }

        int confirmed = _runner.Session.ConfirmedThrough;
        int checkFrame = -1;
        ulong checkHash = 0;
        if (confirmed >= 0)
        {
            int candidate = confirmed - (confirmed % CheckInterval);
            ulong hash = _runner.ConfirmedHash(candidate);
            if (hash != 0)
            {
                checkFrame = NetFrameBase + candidate;
                checkHash = hash;
            }
        }

        _transport.SendInputs(first, bits[..count], _transport.Remote.ContiguousThrough, checkFrame, checkHash);
    }
}
