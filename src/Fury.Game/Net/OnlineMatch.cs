using System.Net;
using Fury.Core.Data;
using Fury.Core.Net;
using Fury.Core.Observability;
using Fury.Core.Sim;

namespace Fury.Game.Net;

/// <summary>
/// One online match against a direct-connect peer, free of Godot types: runs the key exchange on a worker
/// thread, validates that both builds agree on rules and character data, then creates the simulation and
/// the <see cref="NetplaySession"/>. The host is fighter slot 0, the guest slot 1; the guest's characters
/// and rules must match bit-for-bit or the match is refused (a mismatch would desync on frame 1).
/// </summary>
public sealed class OnlineMatch : IDisposable
{
    public enum Phase
    {
        Connecting,
        Ready,
        Failed,
    }

    private readonly bool _host;
    private readonly string _localCharacterId;
    private readonly FightConfig _config;
    private readonly int _inputDelay;
    private readonly Task<(SessionKeys Keys, PeerSetup Peer)> _handshake;
    private readonly string _remoteHost;
    private readonly int _remotePort;
    private readonly int _localPort;
    private readonly CancellationTokenSource _cancel = new();
    private CharacterConfig? _p1;
    private CharacterConfig? _p2;
    private IFightTracer? _tracer;

    public OnlineMatch(
        bool host,
        string remoteHost,
        int port,
        string localCharacterId,
        FightConfig config,
        int inputDelay,
        ReadOnlyMemory<byte> matchToken = default)
    {
        _host = host;
        _remoteHost = remoteHost;
        _localCharacterId = localCharacterId;
        _config = config;
        _inputDelay = inputDelay;

        // Two instances on one machine cannot share a port: on loopback the guest uses port + 1.
        bool loopback = IPAddress.TryParse(remoteHost, out var ip) ? IPAddress.IsLoopback(ip) : remoteHost.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        _localPort = loopback && !host ? port + 1 : port;
        _remotePort = loopback && host ? port + 1 : port;

        var character = CharacterRoster.Create(localCharacterId);
        byte[] token = matchToken.ToArray();
        _handshake = Task.Run(() => UdpHandshake.Run(
            _localPort, _remoteHost, _remotePort, localCharacterId, config.Fingerprint(), character.Fingerprint(), token, TimeSpan.FromSeconds(30), _cancel.Token));
    }

    public Phase Status { get; private set; } = Phase.Connecting;

    public string? Error { get; private set; }

    public FightSimulation? Sim { get; private set; }

    public NetplaySession? Session { get; private set; }

    public UdpTransport? Transport { get; private set; }

    /// <summary>Local fighter slot (host = 0).</summary>
    public int LocalSlot => _host ? 0 : 1;

    public string P1Id { get; private set; } = "";

    public string P2Id { get; private set; } = "";

    /// <summary>Advances the connection; once the handshake finishes it builds the simulation and session.</summary>
    public void Poll(Func<int, InputState> localInput, IFightTracer? tracer)
    {
        if (Status != Phase.Connecting || !_handshake.IsCompleted) return;

        if (_handshake.IsFaulted || _handshake.IsCanceled)
        {
            Fail(_handshake.Exception?.GetBaseException().Message ?? "Connection cancelled.");
            return;
        }

        var (keys, peer) = _handshake.Result;
        try
        {
            if (peer.ConfigHash != _config.Fingerprint()) throw new InvalidDataException("The opponent runs different game rules (version mismatch).");
            var remote = CharacterRoster.Create(peer.CharacterId);
            if (remote.Fingerprint() != peer.CharacterHash) throw new InvalidDataException("The opponent's character data differs from yours (version mismatch).");

            var local = CharacterRoster.Create(_localCharacterId);
            var p1 = _host ? local : remote;
            var p2 = _host ? remote : local;
            P1Id = p1.Id;
            P2Id = p2.Id;
            _p1 = p1;
            _p2 = p2;
            _tracer = tracer;

            Sim = new FightSimulation(p1, p2, _config, tracer);
            Transport = new UdpTransport(_localPort, _remoteHost, _remotePort, keys);
            Session = new NetplaySession(Sim, Transport, localInput, LocalSlot, _inputDelay);
            Status = Phase.Ready;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or System.Net.Sockets.SocketException)
        {
            keys.Dispose();
            Fail(e.Message);
        }
    }

    /// <summary>
    /// Starts the next round of the match on the same network timeline. <paramref name="framesPlayed"/> is the exact length of
    /// the finished round (both peers derive it from confirmed state, see <see cref="FightState.RoundEndFrame"/>).
    /// </summary>
    public FightSimulation BeginNextRound(int framesPlayed)
    {
        if (Session is null || _p1 is null || _p2 is null) throw new InvalidOperationException("The match has not started.");
        var sim = new FightSimulation(_p1, _p2, _config, _tracer);
        Session.StartNextRound(sim, Session.NetFrameBase + framesPlayed);
        Sim = sim;
        return sim;
    }

    private void Fail(string message)
    {
        Error = message;
        Status = Phase.Failed;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        Transport?.Dispose();
        _cancel.Dispose();
    }
}
