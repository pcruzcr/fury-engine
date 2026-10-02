using Fury.Core.Math;

namespace Fury.Core.Net;

/// <summary>
/// In-memory duplex transport for tests and the determinism harness. Implements the same
/// contract as <see cref="UdpTransport"/> (redundant windows + acks) with seeded, fully
/// deterministic per-datagram delay, reordering and loss, so the F2 gate replays jitter
/// scenarios bit-exactly. Call <see cref="Tick"/> once per simulated tick to deliver due packets.
/// </summary>
public sealed class LoopbackPair
{
    private readonly List<Packet> _inFlight = [];
    private readonly Xoshiro256StarStar _rng;
    private readonly int _dropRatePermyriad;
    private readonly int _maxDelay;
    private int _now;

    public LoopbackPair(ulong seed, int maxDelayTicks = 0, int dropRatePermyriad = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDelayTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(dropRatePermyriad);
        _rng = new Xoshiro256StarStar(seed);
        _dropRatePermyriad = dropRatePermyriad;
        _maxDelay = maxDelayTicks;
        A = new Endpoint(this, 0);
        B = new Endpoint(this, 1);
    }

    public Endpoint A { get; }

    public Endpoint B { get; }

    /// <summary>Datagrams sent but not yet delivered or dropped.</summary>
    public int InFlight => _inFlight.Count;

    /// <summary>Advances simulated time one tick and delivers every datagram that is due.</summary>
    public void Tick()
    {
        _now++;
        for (int i = 0; i < _inFlight.Count;)
        {
            var packet = _inFlight[i];
            if (packet.DeliverAt > _now)
            {
                i++;
                continue;
            }
            _inFlight.RemoveAt(i);
            var target = packet.ToEndpoint == 0 ? A : B;
            target.Remote.Accept(packet.FirstFrame, packet.Bits, packet.Ack, packet.CheckFrame, packet.CheckHash);
        }
    }

    private sealed record Packet(int ToEndpoint, int DeliverAt, int FirstFrame, ushort[] Bits, int Ack, int CheckFrame, ulong CheckHash);

    public sealed class Endpoint : INetTransport
    {
        private readonly LoopbackPair _pair;
        private readonly int _index;

        internal Endpoint(LoopbackPair pair, int index)
        {
            _pair = pair;
            _index = index;
        }

        public RemoteInputBuffer Remote { get; } = new();

        public int RttTicks => 0;

        public void SendInputs(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame, ulong checkHash)
        {
            // One drop decision per datagram (whole window or nothing) — carrier loss.
            if (_pair._rng.NextInt(10_000) < _pair._dropRatePermyriad) return;
            int delay = _pair._maxDelay == 0 ? 0 : _pair._rng.NextInt(_pair._maxDelay + 1);
            _pair._inFlight.Add(new Packet(1 - _index, _pair._now + delay, firstFrame, bits.ToArray(), ackFrame, checkFrame, checkHash));
        }
    }
}
