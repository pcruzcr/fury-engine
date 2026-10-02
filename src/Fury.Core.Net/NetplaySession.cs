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
/// </summary>
public sealed class NetplaySession
{
    /// <summary>Confirmed-state fingerprints are exchanged for frames that are multiples of this.</summary>
    public const int CheckInterval = 30;

    private readonly FightSimulation _sim;
    private readonly INetTransport _transport;
    private readonly RollbackRunner _runner;
    private int _nextRemoteFrame;

    public NetplaySession(
        FightSimulation sim,
        INetTransport transport,
        Func<int, InputState> localInput,
        int localSlot,
        int inputDelay = 2,
        int maxPrediction = RollbackRunner.DefaultMaxPrediction)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(transport);
        _sim = sim;
        _transport = transport;
        _runner = new RollbackRunner(sim, localInput, inputDelay, localSlot, maxPrediction);
    }

    public RollbackRunner Runner => _runner;

    public NetplayStatus Status { get; private set; } = NetplayStatus.Running;

    /// <summary>Frame at which a desync was detected (-1 while healthy).</summary>
    public int DesyncFrame { get; private set; } = -1;

    public int Frame => _sim.Frame;

    public int RttTicks => _transport.RttTicks;

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
        while (_transport.Remote.TryGet(_nextRemoteFrame, out ushort bits))
        {
            _runner.DeliverRemote(_nextRemoteFrame, new InputState(bits));
            _nextRemoteFrame++;
        }
    }

    private void CheckDesync()
    {
        if (!_transport.Remote.TryGetPeerCheck(out int frame, out ulong peerHash)) return;
        ulong mine = _runner.ConfirmedHash(frame);
        if (mine != 0 && mine != peerHash)
        {
            Status = NetplayStatus.Desynced;
            DesyncFrame = frame;
        }
    }

    private void Send()
    {
        int latest = _runner.LatestLocalFrame;
        if (latest < 0) return;

        int first = System.Math.Max(0, System.Math.Max(_transport.Remote.PeerAck + 1, latest - (InputPacket.MaxFrames - 1)));
        if (first > latest) first = latest; // nothing new to say: heartbeat the newest frame (carries the ack)
        int count = latest - first + 1;

        Span<ushort> bits = stackalloc ushort[InputPacket.MaxFrames];
        for (int i = 0; i < count; i++) bits[i] = _runner.LocalInput(first + i).Bits;

        int confirmed = _runner.Session.ConfirmedThrough;
        int checkFrame = -1;
        ulong checkHash = 0;
        if (confirmed >= 0)
        {
            int candidate = confirmed - (confirmed % CheckInterval);
            ulong hash = _runner.ConfirmedHash(candidate);
            if (hash != 0)
            {
                checkFrame = candidate;
                checkHash = hash;
            }
        }

        _transport.SendInputs(first, bits[..count], _transport.Remote.ContiguousThrough, checkFrame, checkHash);
    }
}
