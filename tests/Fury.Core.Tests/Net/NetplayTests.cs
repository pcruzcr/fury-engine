using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Net;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Net;

/// <summary>
/// Netplay gate (F2): two real rollback peers (slot 0 / slot 1) over lossy, jittery, reordering
/// links must converge bit-exactly to the offline simulation, detect divergence, and never stall
/// on packet loss. Also covers the wire format, key handling and the real UDP transport.
/// </summary>
public class NetplayTests
{
    private static FightSimulation CreateSim(FightConfig? cfg = null) => new(GlenData.Create(), KiraData.Create(), cfg);

    private static ushort[] RandomInputs(ulong seed, int frames)
    {
        var rng = new Xoshiro256StarStar(seed);
        var inputs = new ushort[frames];
        for (int i = 0; i < frames; i++) inputs[i] = (ushort)(rng.NextULong() & 0x00FF);
        return inputs;
    }

    // ------------------------------------------------------------------ two-peer convergence

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(2, 4, 1500)]
    [InlineData(3, 6, 3000)]
    public void TwoPeers_ConvergeToOfflineHash_UnderJitterAndLoss(int inputDelay, int maxDelayTicks, int dropPermyriad)
    {
        const int frames = 400; // > 64-frame rings: exercises slot reuse
        var sampled1 = RandomInputs(11, frames + 64);
        var sampled2 = RandomInputs(22, frames + 64);

        // A peer samples at frame f and the input applies at f + inputDelay.
        ushort Applied(ushort[] sampled, int f) => f >= inputDelay ? sampled[f - inputDelay] : (ushort)0;

        var offline = CreateSim();
        var offlineHashes = new ulong[frames];
        for (int f = 0; f < frames; f++)
        {
            offline.Step(new InputState(Applied(sampled1, f)), new InputState(Applied(sampled2, f)));
            offlineHashes[f] = offline.SnapshotHash;
        }

        var link = new LoopbackPair(seed: 99, maxDelayTicks, dropPermyriad);
        var simA = CreateSim();
        var simB = CreateSim();
        var a = new NetplaySession(simA, link.A, f => new InputState(sampled1[f]), localSlot: 0, inputDelay);
        var b = new NetplaySession(simB, link.B, f => new InputState(sampled2[f]), localSlot: 1, inputDelay);

        int guard = 0;
        while ((a.Frame < frames || b.Frame < frames) && guard++ < 20_000)
        {
            if (a.Frame < frames) a.Tick(); else a.Pump();
            if (b.Frame < frames) b.Tick(); else b.Pump();
            link.Tick();
        }
        Assert.True(guard < 20_000, "Peers never reached the target frame (stalled forever).");

        // Flush: keep exchanging until every remote input up to the last frame is confirmed.
        for (int i = 0; i < 2000 && (a.Runner.Session.ConfirmedThrough < frames - 1 || b.Runner.Session.ConfirmedThrough < frames - 1); i++)
        {
            a.Pump();
            b.Pump();
            link.Tick();
        }

        Assert.NotEqual(NetplayStatus.Desynced, a.Status);
        Assert.NotEqual(NetplayStatus.Desynced, b.Status);
        Assert.True(a.Runner.Session.ConfirmedThrough >= frames - 1);
        Assert.True(b.Runner.Session.ConfirmedThrough >= frames - 1);

        // Every confirmed frame still held in the 64-frame ring must equal the offline hash.
        for (int f = frames - 40; f < frames; f++)
        {
            Assert.Equal(offlineHashes[f], a.Runner.ConfirmedHash(f));
            Assert.Equal(offlineHashes[f], b.Runner.ConfirmedHash(f));
        }
        if (maxDelayTicks > 0 || dropPermyriad > 0)
        {
            Assert.True(simA.Stats.Rollbacks + simB.Stats.Rollbacks > 0, "Lossy links must have forced at least one rollback.");
        }
    }

    [Fact]
    public void SecondPeer_UsesItsOwnSlot_NotPlayerOne()
    {
        // Regression: the runner used to feed (local, remote) in that order on BOTH peers, so the
        // second peer simulated with the fighters swapped and desynced immediately.
        var link = new LoopbackPair(seed: 1);
        var simB = CreateSim();
        var b = new NetplaySession(simB, link.B, _ => new InputState((ushort)InputButton.Right), localSlot: 1, inputDelay: 0);

        link.A.SendInputs(0, [0], -1, -1, 0);
        link.Tick();
        b.Tick();

        // P2 walked right (towards the wall); P1 stayed. If slots were swapped, P1 would move.
        Assert.Equal(Fp.FromRatio(-3, 1), simB.State.GetX(0));
        Assert.NotEqual(Fp.FromRatio(3, 1), simB.State.GetX(1));
    }

    [Fact]
    public void PredictionBarrier_StallsInsteadOfRunningAwayFromARemoteThatWentSilent()
    {
        var link = new LoopbackPair(seed: 2);
        var sim = CreateSim();
        var a = new NetplaySession(sim, link.A, _ => new InputState(0), localSlot: 0, inputDelay: 0, maxPrediction: 8);

        for (int i = 0; i < 100; i++)
        {
            a.Tick();
            link.Tick();
        }

        Assert.Equal(8, a.Frame);
        Assert.Equal(NetplayStatus.Stalled, a.Status);
    }

    [Fact]
    public void Desync_IsDetectedWithinTwoCheckIntervals()
    {
        // Peers that disagree about the rules produce different states for the same inputs.
        var link = new LoopbackPair(seed: 3);
        var a = new NetplaySession(CreateSim(), link.A, _ => new InputState(0), 0, inputDelay: 1);
        var b = new NetplaySession(CreateSim(FightConfig.Default() with { RoundSeconds = 30 }), link.B, _ => new InputState(0), 1, inputDelay: 1);

        for (int i = 0; i < 400 && a.Status != NetplayStatus.Desynced && b.Status != NetplayStatus.Desynced; i++)
        {
            a.Tick();
            b.Tick();
            link.Tick();
        }

        Assert.True(a.Status == NetplayStatus.Desynced || b.Status == NetplayStatus.Desynced);
        var detector = a.Status == NetplayStatus.Desynced ? a : b;
        Assert.True(detector.DesyncFrame >= 0);
        Assert.True(detector.DesyncFrame < NetplaySession.CheckInterval * 2);
    }

    [Fact]
    public void Advance_OutOfSequence_IsRejected()
    {
        var runner = new Fury.Core.Rollback.RollbackRunner(CreateSim(), _ => new InputState(0), inputDelay: 0);
        Assert.Throws<ArgumentException>(() => runner.Advance(5));
    }


    // ------------------------------------------------------------------ multi-round matches

    private const int ResultFrames = 40;
    private static readonly FightConfig ShortRounds = FightConfig.Default() with { RoundSeconds = 2 };

    private sealed class MatchPeer
    {
        public NetplaySession Net = null!;
        public required FightSimulation Sim;
        public int Round;
        public readonly List<ulong> EndHashes = [];

        public void Step(int totalRounds)
        {
            if (Sim.State.RoundState == 0)
            {
                Net.Tick();
                return;
            }

            // Round over: keep playing it out, then (once every frame up to the next start is confirmed) swap the sim.
            int target = Sim.State.RoundEndFrame + 1 + ResultFrames;
            if (Net.Frame < target)
            {
                Net.Tick();
                return;
            }

            Net.Pump();
            if (Net.Frame != target || Net.Runner.Session.ConfirmedThrough < target - 1 || Round + 1 >= totalRounds) return;

            EndHashes.Add(Net.Runner.ConfirmedHash(target - 1));
            Round++;
            Sim = new FightSimulation(GlenData.Create(), KiraData.Create(), ShortRounds);
            Net.StartNextRound(Sim, Net.NetFrameBase + target);
        }
    }

    [Fact]
    public void TwoPeers_PlayAFullMatchOfSeveralRounds_OnOneContinuousTimeline()
    {
        const int rounds = 3;
        const int delay = 2;
        var sampled1 = RandomInputs(71, 4000);
        var sampled2 = RandomInputs(72, 4000);
        ushort Applied(ushort[] s, int abs) => abs >= delay ? s[abs - delay] : (ushort)0;

        // Offline reference: the same rounds, the same absolute input timeline.
        var expectedEnd = new List<ulong>();
        int offlineBase = 0;
        for (int r = 0; r < rounds - 1; r++)
        {
            var sim = new FightSimulation(GlenData.Create(), KiraData.Create(), ShortRounds);
            int f = 0;
            int target = int.MaxValue;
            while (f < target)
            {
                sim.Step(new InputState(Applied(sampled1, offlineBase + f)), new InputState(Applied(sampled2, offlineBase + f)));
                f++;
                if (sim.State.RoundState != 0 && target == int.MaxValue) target = sim.State.RoundEndFrame + 1 + ResultFrames;
            }
            expectedEnd.Add(sim.SnapshotHash);
            offlineBase += target;
        }

        var link = new LoopbackPair(seed: 8, maxDelayTicks: 4, dropRatePermyriad: 2000);
        var simA = new FightSimulation(GlenData.Create(), KiraData.Create(), ShortRounds);
        var simB = new FightSimulation(GlenData.Create(), KiraData.Create(), ShortRounds);
        var a = new MatchPeer { Sim = simA };
        var b = new MatchPeer { Sim = simB };

        // The sampling function receives LOCAL frames; offset it by each round's base so both peers keep one timeline.
        a.Net = new NetplaySession(simA, link.A, f => new InputState(sampled1[System.Math.Min(a.Net.NetFrameBase + f, sampled1.Length - 1)]), 0, delay);
        b.Net = new NetplaySession(simB, link.B, f => new InputState(sampled2[System.Math.Min(b.Net.NetFrameBase + f, sampled2.Length - 1)]), 1, delay);

        for (int guard = 0; guard < 60_000 && (a.Round < rounds - 1 || b.Round < rounds - 1); guard++)
        {
            a.Step(rounds);
            b.Step(rounds);
            link.Tick();
        }

        Assert.Equal(rounds - 1, a.Round);
        Assert.Equal(rounds - 1, b.Round);
        Assert.NotEqual(NetplayStatus.Desynced, a.Net.Status);
        Assert.NotEqual(NetplayStatus.Desynced, b.Net.Status);
        Assert.Equal(rounds - 1, a.EndHashes.Count);
        for (int r = 0; r < rounds - 1; r++)
        {
            Assert.NotEqual(0UL, a.EndHashes[r]);
            Assert.Equal(a.EndHashes[r], b.EndHashes[r]);
            Assert.Equal(expectedEnd[r], a.EndHashes[r]); // and both equal the offline simulation of the same timeline
        }
    }

    [Fact]
    public void NextRound_MustStartExactlyWhereThePreviousOneStopped()
    {
        var link = new LoopbackPair(seed: 1);
        var net = new NetplaySession(CreateSim(), link.A, _ => new InputState(0), 0, inputDelay: 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => net.StartNextRound(CreateSim(), netFrameBase: 50));
    }

    // ------------------------------------------------------------------ wire format

    [Fact]
    public void InputPacket_RoundTrips()
    {
        ushort[] bits = [1, 2, 0xFFFF, 4];
        Span<byte> buffer = stackalloc byte[InputPacket.EncodedLength(bits.Length)];
        int written = InputPacket.Encode(10, bits, 7, 30, 0xDEADBEEFCAFEUL, buffer);

        Span<ushort> decoded = stackalloc ushort[InputPacket.MaxFrames];
        Assert.True(InputPacket.TryDecode(buffer[..written], decoded, out int first, out int ack, out int cf, out ulong ch, out int count));
        Assert.Equal((10, 7, 30, 0xDEADBEEFCAFEUL, 4), (first, ack, cf, ch, count));
        Assert.True(bits.AsSpan().SequenceEqual(decoded[..count]));
    }

    [Fact]
    public void InputPacket_RejectsEveryMalformedVariant()
    {
        ushort[] bits = [1, 2, 3];
        byte[] good = new byte[InputPacket.EncodedLength(3)];
        InputPacket.Encode(5, bits, 2, -1, 0, good);
        Span<ushort> scratch = stackalloc ushort[InputPacket.MaxFrames];

        for (int len = 0; len < good.Length; len++)
        {
            Assert.False(InputPacket.TryDecode(good.AsSpan(0, len), scratch, out _, out _, out _, out _, out _), $"truncated to {len}");
        }

        var wrongVersion = (byte[])good.Clone();
        wrongVersion[0] = 99;
        Assert.False(InputPacket.TryDecode(wrongVersion, scratch, out _, out _, out _, out _, out _));

        var zeroCount = (byte[])good.Clone();
        zeroCount[1] = 0;
        Assert.False(InputPacket.TryDecode(zeroCount, scratch, out _, out _, out _, out _, out _));

        var negativeFrame = (byte[])good.Clone();
        BitConverter.TryWriteBytes(negativeFrame.AsSpan(2), -5);
        Assert.False(InputPacket.TryDecode(negativeFrame, scratch, out _, out _, out _, out _, out _));

        Assert.Throws<ArgumentOutOfRangeException>(() => InputPacket.Encode(0, new ushort[InputPacket.MaxFrames + 1], -1, -1, 0, new byte[512]));
    }

    [Fact]
    public void RemoteInputBuffer_IsBounded_Idempotent_AndTracksContiguity()
    {
        var buffer = new RemoteInputBuffer();
        buffer.Accept(0, [10, 11, 12], ackFrame: -1);
        Assert.Equal(2, buffer.ContiguousThrough);

        buffer.Accept(0, [10, 11, 12], ackFrame: -1); // duplicate window
        Assert.Equal(2, buffer.ContiguousThrough);

        buffer.Accept(5, [15], ackFrame: -1); // gap at 3..4
        Assert.Equal(2, buffer.ContiguousThrough);
        Assert.True(buffer.TryGet(5, out ushort five));
        Assert.Equal(15, five);
        buffer.Accept(3, [13, 14], ackFrame: -1);
        Assert.Equal(5, buffer.ContiguousThrough);

        // A hostile frame number far beyond the window cannot grow or poison the buffer.
        buffer.Accept(int.MaxValue - 100, [1], ackFrame: -1);
        Assert.False(buffer.TryGet(int.MaxValue - 100, out _));
        Assert.Equal(5, buffer.ContiguousThrough);

        Assert.True(buffer.Accept(0, [10], ackFrame: 4));
        Assert.False(buffer.Accept(0, [10], ackFrame: 2)); // ack never goes backwards
        Assert.Equal(4, buffer.PeerAck);
    }

    [Fact]
    public void RttEstimator_MeasuresSendToAck_AndIgnoresRetransmits()
    {
        var rtt = new RttEstimator();
        long freq = System.Diagnostics.Stopwatch.Frequency;
        rtt.OnSend(10, timestamp: 0);
        rtt.OnSend(10, timestamp: freq / 10); // retransmission must NOT shorten the measurement
        rtt.OnAck(10, timestamp: freq / 20);  // 50 ms after the first send
        Assert.InRange(rtt.RttMilliseconds, 49.0, 51.0);
        Assert.Equal(3, rtt.RttTicks); // 50 ms = 3 ticks of 16.7 ms
        rtt.OnAck(99, timestamp: freq); // never sent: ignored
        Assert.InRange(rtt.RttMilliseconds, 49.0, 51.0);
    }

    // ------------------------------------------------------------------ keys

    [Fact]
    public void SessionKeys_AreDirectional_AndMirrored()
    {
        using var a = new HandshakeManager();
        using var b = new HandshakeManager();
        a.CompleteHandshake(b.GetPublicKey());
        b.CompleteHandshake(a.GetPublicKey());

        Assert.Equal(a.SessionKeys!.SendKey, b.SessionKeys!.ReceiveKey);
        Assert.Equal(a.SessionKeys.ReceiveKey, b.SessionKeys.SendKey);
        Assert.NotEqual(a.SessionKeys.SendKey, a.SessionKeys.ReceiveKey);

        // A packet reflected back to its sender must fail authentication.
        using var signA = new PacketAuthenticator(a.SessionKeys.SendKey);
        using var verifyA = new PacketAuthenticator(a.SessionKeys.ReceiveKey);
        using var verifyB = new PacketAuthenticator(b.SessionKeys.ReceiveKey);
        byte[] packet = new byte[PacketAuthenticator.EncodedLength(8)];
        signA.Stamp(new byte[8], 1, packet);
        Assert.True(verifyB.Verify(packet, new byte[8], out _));
        Assert.False(verifyA.Verify(packet, new byte[8], out _));
    }

    [Fact]
    public void MatchToken_MismatchProducesDifferentKeys_SoAManInTheMiddleIsLockedOut()
    {
        byte[] token = [1, 2, 3, 4];
        using var a = new HandshakeManager(token);
        using var honest = new HandshakeManager(token);
        using var mitm = new HandshakeManager(); // does not know the matchmaker token

        a.CompleteHandshake(honest.GetPublicKey());
        honest.CompleteHandshake(a.GetPublicKey());
        Assert.Equal(a.SessionKeys!.SendKey, honest.SessionKeys!.ReceiveKey);

        using var a2 = new HandshakeManager(token);
        a2.CompleteHandshake(mitm.GetPublicKey());
        mitm.CompleteHandshake(a2.GetPublicKey());
        Assert.NotEqual(a2.SessionKeys!.SendKey, mitm.SessionKeys!.ReceiveKey);
    }

    [Fact]
    public void Handshake_RejectsInvalidAndReflectedPeerKeys()
    {
        using var a = new HandshakeManager();
        Assert.Throws<InvalidDataException>(() => a.CompleteHandshake(new byte[10]));
        Assert.Throws<InvalidDataException>(() => a.CompleteHandshake(a.GetPublicKey())); // own key echoed

        var offCurve = new byte[65];
        offCurve[0] = 0x04;
        offCurve[1] = 1;
        offCurve[33] = 1;
        Assert.Throws<InvalidDataException>(() => a.CompleteHandshake(offCurve));
    }

    // ------------------------------------------------------------------ real UDP

    private static SessionKeys[] MirroredKeys()
    {
        byte[] ab = RandomNumberGenerator.GetBytes(32);
        byte[] ba = RandomNumberGenerator.GetBytes(32);
        return [new SessionKeys(ab, ba), new SessionKeys((byte[])ba.Clone(), (byte[])ab.Clone())];
    }

    private static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

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

    [Fact]
    public async Task UdpHandshake_TwoPeers_AgreeOnMirroredKeys_AndExchangeSetup()
    {
        int pa = FreePort(), pb = FreePort();
        byte[] token = [9, 9, 9];
        var taskA = Task.Run(() => UdpHandshake.Run(pa, "127.0.0.1", pb, "glen", 0x1111, 0xAAAA, token, TimeSpan.FromSeconds(5)));
        var taskB = Task.Run(() => UdpHandshake.Run(pb, "127.0.0.1", pa, "kira", 0x1111, 0xBBBB, token, TimeSpan.FromSeconds(5)));
        var (keysA, peerOfA) = await taskA;
        var (keysB, peerOfB) = await taskB;

        Assert.Equal(keysA.SendKey, keysB.ReceiveKey);
        Assert.Equal(keysA.ReceiveKey, keysB.SendKey);
        Assert.Equal(new PeerSetup("kira", 0x1111, 0xBBBB), peerOfA);
        Assert.Equal(new PeerSetup("glen", 0x1111, 0xAAAA), peerOfB);
    }

    [Fact]
    public void UdpHandshake_TimesOutWhenNobodyAnswers()
    {
        Assert.Throws<TimeoutException>(() => UdpHandshake.Run(FreePort(), "127.0.0.1", FreePort(), "glen", 1, 2, default, TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void Udp_RequiresSessionKeys()
    {
        Assert.Throws<ArgumentNullException>(() => new UdpTransport(0, null, 0, null!));
    }

    [Fact]
    public void Udp_DeliversRedundantWindows_AndAcks_AndMeasuresRtt()
    {
        var keys = MirroredKeys();
        int pa = FreePort(), pb = FreePort();
        using var a = new UdpTransport(pa, "127.0.0.1", pb, keys[0]);
        using var b = new UdpTransport(pb, "127.0.0.1", pa, keys[1]);

        a.SendInputs(4, [0x44, 0x55, 0x66], ackFrame: -1, checkFrame: -1, checkHash: 0);
        Assert.True(WaitFor(() => b.Remote.TryGet(6, out _)));
        Assert.True(b.Remote.TryGet(4, out ushort v4));
        Assert.Equal(0x44, v4);
        Assert.False(b.Remote.TryGet(3, out _));

        // Frames 0..3 arrive later; the buffer becomes contiguous.
        a.SendInputs(0, [1, 2, 3, 4, 0x44, 0x55, 0x66], ackFrame: -1, checkFrame: 30, checkHash: 0xABCD);
        Assert.True(WaitFor(() => b.Remote.ContiguousThrough == 6));
        Assert.True(b.Remote.TryGetPeerCheck(out int cf, out ulong ch));
        Assert.Equal((30, 0xABCDUL), (cf, ch));

        // B acknowledges; A learns it and gets an RTT sample.
        b.SendInputs(0, [9], ackFrame: b.Remote.ContiguousThrough, checkFrame: -1, checkHash: 0);
        Assert.True(WaitFor(() => a.Remote.PeerAck == 6));
        Assert.True(a.RttMilliseconds >= 0);
    }

    [Fact]
    public void Udp_IgnoresForeignSources_Forgeries_AndRunts_WithoutDyingOrPoisoning()
    {
        var keys = MirroredKeys();
        int pa = FreePort(), pb = FreePort();
        using var a = new UdpTransport(pa, "127.0.0.1", pb, keys[0]);
        using var b = new UdpTransport(pb, "127.0.0.1", pa, keys[1]);

        using (var attacker = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var target = new IPEndPoint(IPAddress.Loopback, pb);
            attacker.Send([0x01], 1, target);                        // 1-byte runt
            attacker.Send(new byte[200], 200, target);                // wrong source + garbage
            // Correctly formatted and signed with the WRONG key, from the wrong port.
            using var forger = new PacketAuthenticator(RandomNumberGenerator.GetBytes(32));
            byte[] payload = new byte[InputPacket.EncodedLength(1)];
            InputPacket.Encode(0, [0xFFFF], -1, -1, 0, payload);
            byte[] forged = new byte[PacketAuthenticator.EncodedLength(payload.Length)];
            forger.Stamp(payload, 1, forged);
            attacker.Send(forged, forged.Length, target);
        }

        Thread.Sleep(50);
        Assert.False(b.Remote.TryGet(0, out _));

        a.SendInputs(0, [0x11], -1, -1, 0);
        Assert.True(WaitFor(() => b.Remote.TryGet(0, out _)));
    }

    [Fact]
    public void Udp_TwoRealPeers_RunAMatchOverSockets_AndAgreeOnTheState()
    {
        const int frames = 180;
        var keys = MirroredKeys();
        int pa = FreePort(), pb = FreePort();
        using var ta = new UdpTransport(pa, "127.0.0.1", pb, keys[0]);
        using var tb = new UdpTransport(pb, "127.0.0.1", pa, keys[1]);
        var in1 = RandomInputs(5, frames + 64);
        var in2 = RandomInputs(6, frames + 64);

        var a = new NetplaySession(CreateSim(), ta, f => new InputState(in1[f]), 0, inputDelay: 2);
        var b = new NetplaySession(CreateSim(), tb, f => new InputState(in2[f]), 1, inputDelay: 2);

        var deadline = Environment.TickCount64 + 15_000;
        while ((a.Frame < frames || b.Frame < frames) && Environment.TickCount64 < deadline)
        {
            if (a.Frame < frames) a.Tick(); else a.Pump();
            if (b.Frame < frames) b.Tick(); else b.Pump();
            Thread.Sleep(1);
        }
        Assert.True(a.Frame >= frames && b.Frame >= frames, "Match did not complete over UDP.");

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
            Assert.NotEqual(0UL, a.Runner.ConfirmedHash(f));
        }
    }
}
