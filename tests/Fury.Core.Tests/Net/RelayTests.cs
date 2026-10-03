using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Fury.Core.Data;
using Fury.Core.Net;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Net;

/// <summary>
/// Relay (NAT fallback): room codes, forwarding rules, abuse limits, and the property that matters most: a relay that
/// does not know the room code can forward or drop traffic but never take part in the session.
/// </summary>
public class RelayTests
{
    private static readonly byte[] RoomA = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    private static readonly byte[] RoomB = Enumerable.Range(101, 16).Select(i => (byte)i).ToArray();

    private static RelayServer StartRelay(RelayServerOptions? options = null)
    {
        var server = new RelayServer(0, options, IPAddress.Loopback);
        server.Start();
        return server;
    }

    private static RelayChannel Join(RelayServer server, byte[] room) => new("127.0.0.1", server.Port, room);

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return condition();
    }

    private static int ReceiveWithin(IDatagramChannel channel, byte[] buffer, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            int n = channel.Receive(buffer, 20);
            if (n >= 0) return n;
        }
        return -1;
    }

    /// <summary>Registers both players (the relay learns an endpoint from its first datagram) and drains the greeting.</summary>
    private static void Pair(RelayChannel a, RelayChannel b)
    {
        byte[] buffer = new byte[600];
        for (int i = 0; i < 10; i++)
        {
            a.Send([0xA0]);
            b.Send([0xB0]);
            Thread.Sleep(10);
        }
        while (a.Receive(buffer, 30) >= 0) { }
        while (b.Receive(buffer, 30) >= 0) { }
    }

    [Fact]
    public void RoomCodes_AreRandom_NormalisedAndDeriveStableDistinctSecrets()
    {
        string code = RelayRoom.GenerateCode();
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
        Assert.NotEqual(code, RelayRoom.GenerateCode());

        var (roomId, token) = RelayRoom.Derive(code);
        var (sameRoom, sameToken) = RelayRoom.Derive(code.ToLowerInvariant().Replace("-", " "));
        Assert.Equal(roomId, sameRoom);
        Assert.Equal(token, sameToken);
        Assert.Equal(RelayProtocol.RoomIdBytes, roomId.Length);
        Assert.Equal(32, token.Length);
        Assert.NotEqual(roomId, token[..RelayProtocol.RoomIdBytes]);

        var (otherRoom, otherToken) = RelayRoom.Derive(RelayRoom.GenerateCode());
        Assert.NotEqual(roomId, otherRoom);
        Assert.NotEqual(token, otherToken);
    }

    [Fact]
    public void RoomCodes_TooShort_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => RelayRoom.Derive("abc-123"));
        Assert.Throws<ArgumentException>(() => RelayRoom.Derive(""));
    }

    [Fact]
    public void Relay_ForwardsBothWays_AndKeepsRoomsApart()
    {
        using var relay = StartRelay();
        using var a = Join(relay, RoomA);
        using var b = Join(relay, RoomA);
        using var other = Join(relay, RoomB);
        Pair(a, b);

        a.Send([1, 2, 3]);
        byte[] buffer = new byte[600];
        Assert.Equal(3, ReceiveWithin(b, buffer, 2000));
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..3]);

        b.Send([9, 8]);
        Assert.Equal(2, ReceiveWithin(a, buffer, 2000));
        Assert.Equal(new byte[] { 9, 8 }, buffer[..2]);

        other.Send([7]);
        Assert.Equal(-1, ReceiveWithin(a, buffer, 150));
        Assert.Equal(-1, ReceiveWithin(b, buffer, 150));
        Assert.Equal(2, relay.ActiveRooms);
    }

    [Fact]
    public void Relay_RefusesAThirdPlayerInAFullRoom()
    {
        using var relay = StartRelay();
        using var a = Join(relay, RoomA);
        using var b = Join(relay, RoomA);
        using var intruder = Join(relay, RoomA);
        Pair(a, b);

        long droppedBefore = relay.Dropped;
        intruder.Send([0x66]);
        Assert.True(WaitFor(() => relay.Dropped > droppedBefore));

        byte[] buffer = new byte[600];
        Assert.Equal(-1, ReceiveWithin(a, buffer, 150));
        Assert.Equal(-1, ReceiveWithin(b, buffer, 150));

        a.Send([5]);
        Assert.Equal(1, ReceiveWithin(b, buffer, 2000));
        Assert.Equal(-1, ReceiveWithin(intruder, buffer, 100));
    }

    [Fact]
    public void RelayChannel_DiscardsDatagramsThatDoNotComeFromTheRelay()
    {
        using var relay = StartRelay();
        using var a = Join(relay, RoomA);
        using var spoofer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        // A perfectly formed relay frame, but from a socket that is not the relay.
        byte[] frame = new byte[RelayProtocol.ServerHeaderBytes + 3];
        Span<byte> span = frame;
        RelayProtocol.WriteServerHeader(span);
        spoofer.Send(frame, frame.Length, new IPEndPoint(IPAddress.Loopback, a.LocalPort));

        Assert.Equal(-1, ReceiveWithin(a, new byte[600], 200));
    }

    [Fact]
    public void Relay_IgnoresGarbage_AndKeepsServing()
    {
        using var relay = StartRelay();
        using var a = Join(relay, RoomA);
        using var b = Join(relay, RoomA);
        using var raw = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = new IPEndPoint(IPAddress.Loopback, relay.Port);

        raw.Send([0x00], 1, target);                         // runt
        raw.Send(new byte[300], 300, target);                // right size, wrong magic
        byte[] oversized = new byte[RelayProtocol.MaxDatagramBytes + 100];
        RelayProtocol.WriteClientHeader(oversized, RoomA);
        raw.Send(oversized, oversized.Length, target);       // right header, too large
        Assert.True(WaitFor(() => relay.Dropped >= 3));
        Assert.Equal(0, relay.ActiveRooms);

        Pair(a, b);
        a.Send([4]);
        Assert.Equal(1, ReceiveWithin(b, new byte[600], 2000));
    }

    [Fact]
    public void Relay_RateLimitsOnePlayer()
    {
        using var relay = StartRelay(new RelayServerOptions { MaxPacketsPerSecond = 20 });
        using var a = Join(relay, RoomA);
        using var b = Join(relay, RoomA);
        Pair(a, b);

        long forwardedBefore = relay.Forwarded;
        long droppedBefore = relay.Dropped;
        for (int i = 0; i < 200; i++) a.Send([1]);

        Assert.True(WaitFor(() => relay.Forwarded + relay.Dropped >= forwardedBefore + droppedBefore + 190));
        Assert.True(relay.Forwarded - forwardedBefore <= 25, $"forwarded {relay.Forwarded - forwardedBefore} of 200");
        Assert.True(relay.Dropped - droppedBefore >= 150);
    }

    [Fact]
    public void Relay_CapsRoomsPerAddress_AndTotal_AndExpiresIdleRooms()
    {
        using var relay = StartRelay(new RelayServerOptions { MaxRooms = 5, MaxRoomsPerAddress = 3, IdleTimeoutSeconds = 30 });
        for (int i = 0; i < 4; i++)
        {
            byte[] room = RoomA.ToArray();
            room[0] = (byte)(200 + i);
            using var ch = Join(relay, room);
            ch.Send([1]);
        }

        // Four rooms were asked for from one address; only three fit.
        Assert.True(WaitFor(() => relay.ActiveRooms == 3 && relay.Dropped >= 1));

        relay.SweepExpired(Environment.TickCount64 + 29_000);
        Assert.Equal(3, relay.ActiveRooms);
        relay.SweepExpired(Environment.TickCount64 + 31_000);
        Assert.Equal(0, relay.ActiveRooms);
    }

    [Fact]
    public async Task Relay_CarriesTheHandshake_AndAuthenticatedInputs()
    {
        using var relay = StartRelay();
        var (roomId, token) = RelayRoom.Derive("ABCDE-FGHJK");
        using var chA = Join(relay, roomId);
        using var chB = Join(relay, roomId);

        var taskA = Task.Run(() => UdpHandshake.Run(chA, "glen", 0x1111, 0xAAAA, token, TimeSpan.FromSeconds(10)));
        var taskB = Task.Run(() => UdpHandshake.Run(chB, "kira", 0x1111, 0xBBBB, token, TimeSpan.FromSeconds(10)));
        var (keysA, peerOfA) = await taskA;
        var (keysB, peerOfB) = await taskB;
        Assert.Equal("kira", peerOfA.CharacterId);
        Assert.Equal("glen", peerOfB.CharacterId);

        using var a = new UdpTransport(chA, keysA, ownsChannel: false);
        using var b = new UdpTransport(chB, keysB, ownsChannel: false);
        a.SendInputs(0, [0x11, 0x22], ackFrame: -1, checkFrame: -1, checkHash: 0);
        Assert.True(WaitFor(() => b.Remote.TryGet(1, out _)));
        b.SendInputs(0, [0x33], ackFrame: 1, checkFrame: -1, checkHash: 0);
        Assert.True(WaitFor(() => a.Remote.PeerAck == 1));
    }

    [Fact]
    public async Task Relay_WithoutTheRoomCode_CannotJoinTheSession()
    {
        // The relay (or anyone sitting on the path) knows the room id but not the token derived from the code. If it
        // impersonates a player it can finish the key exchange, but its keys differ, so nothing it sends is accepted.
        using var relay = StartRelay();
        var (roomId, token) = RelayRoom.Derive("ABCDE-FGHJK");
        byte[] wrongToken = RandomNumberGenerator.GetBytes(32);
        using var chA = Join(relay, roomId);
        using var chB = Join(relay, roomId);

        var taskA = Task.Run(() => UdpHandshake.Run(chA, "glen", 1, 1, token, TimeSpan.FromSeconds(10)));
        var taskB = Task.Run(() => UdpHandshake.Run(chB, "kira", 1, 1, wrongToken, TimeSpan.FromSeconds(10)));
        var (keysA, _) = await taskA;
        var (keysB, _) = await taskB;
        Assert.NotEqual(keysA.SendKey, keysB.ReceiveKey);

        using var a = new UdpTransport(chA, keysA, ownsChannel: false);
        using var b = new UdpTransport(chB, keysB, ownsChannel: false);
        a.SendInputs(0, [0x11], -1, -1, 0);
        Thread.Sleep(300);
        Assert.False(b.Remote.TryGet(0, out _));
    }

    [Fact]
    public async Task Relay_FullRollbackMatch_ConvergesLikeDirectPlay()
    {
        const int frames = 150;
        using var relay = StartRelay();
        var (roomId, token) = RelayRoom.Derive("MATCH-ROOM2");
        using var chA = Join(relay, roomId);
        using var chB = Join(relay, roomId);

        var taskA = Task.Run(() => UdpHandshake.Run(chA, "glen", 1, 1, token, TimeSpan.FromSeconds(10)));
        var taskB = Task.Run(() => UdpHandshake.Run(chB, "kira", 1, 1, token, TimeSpan.FromSeconds(10)));
        var (keysA, _) = await taskA;
        var (keysB, _) = await taskB;
        using var ta = new UdpTransport(chA, keysA, ownsChannel: false);
        using var tb = new UdpTransport(chB, keysB, ownsChannel: false);

        var in1 = Inputs(11, frames + 64);
        var in2 = Inputs(12, frames + 64);
        var a = new NetplaySession(new FightSimulation(GlenData.Create(), KiraData.Create()), ta, f => new InputState(in1[f]), 0, inputDelay: 2);
        var b = new NetplaySession(new FightSimulation(GlenData.Create(), KiraData.Create()), tb, f => new InputState(in2[f]), 1, inputDelay: 2);

        var deadline = Environment.TickCount64 + 20_000;
        while ((a.Frame < frames || b.Frame < frames) && Environment.TickCount64 < deadline)
        {
            if (a.Frame < frames) a.Tick(); else a.Pump();
            if (b.Frame < frames) b.Tick(); else b.Pump();
            Thread.Sleep(1);
        }
        Assert.True(a.Frame >= frames && b.Frame >= frames, "Match did not complete through the relay.");

        Assert.True(WaitFor(() =>
        {
            a.Pump();
            b.Pump();
            return a.Runner.Session.ConfirmedThrough >= frames - 1 && b.Runner.Session.ConfirmedThrough >= frames - 1;
        }, 8000));
        Assert.NotEqual(NetplayStatus.Desynced, a.Status);
        Assert.NotEqual(NetplayStatus.Desynced, b.Status);
        for (int f = frames - 30; f < frames; f++)
        {
            Assert.Equal(a.Runner.ConfirmedHash(f), b.Runner.ConfirmedHash(f));
        }
    }

    private static ushort[] Inputs(ulong seed, int frames)
    {
        var inputs = new ushort[frames];
        ulong s = seed;
        for (int i = 0; i < frames; i++)
        {
            s = (s * 6364136223846793005UL) + 1442695040888963407UL;
            inputs[i] = (ushort)((s >> 40) & 0x3FF);
        }
        return inputs;
    }
}
