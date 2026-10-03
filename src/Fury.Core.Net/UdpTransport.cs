namespace Fury.Core.Net;

/// <summary>
/// Authenticated datagram transport for real network play, over any <see cref="IDatagramChannel"/> (direct UDP or a relay).
/// Every datagram is [InputPacket payload][seq:4][HMAC-SHA256:32] signed with a DIRECTIONAL key (see
/// <see cref="SessionKeys"/>), replay-filtered and structure-checked before any parsing. The receive path runs on one
/// dedicated thread over a preallocated buffer: no per-packet allocations, and a malformed datagram can never terminate it.
/// </summary>
public sealed class UdpTransport : INetTransport, IDisposable
{
    private const int MaxDatagramBytes = RelayProtocol.MaxPayloadBytes;
    private const int ReceivePollMilliseconds = 100;

    private readonly IDatagramChannel _channel;
    private readonly bool _ownsChannel;
    private readonly PacketAuthenticator _tx;
    private readonly PacketAuthenticator _rx;
    private readonly PacketReplayGuard _replayGuard = new();
    private readonly RttEstimator _rtt = new();
    private readonly Thread _receiveThread;
    private uint _localSequence;
    private volatile bool _disposed;

    /// <param name="channel">Pipe to the peer. The same channel should carry the handshake, so a relay sees one endpoint per player.</param>
    /// <param name="keys">Directional keys from <see cref="HandshakeManager"/>. Required: an all-zero default key
    /// once let anyone forge authenticated packets (ADR-004).</param>
    /// <param name="ownsChannel">Dispose the channel together with the transport.</param>
    public UdpTransport(IDatagramChannel channel, SessionKeys keys, bool ownsChannel = true)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(keys);
        _channel = channel;
        _ownsChannel = ownsChannel;
        _tx = new PacketAuthenticator(keys.SendKey);
        _rx = new PacketAuthenticator(keys.ReceiveKey);

        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "FuryUdpReceive" };
        _receiveThread.Start();
    }

    /// <summary>Direct UDP to a known peer address.</summary>
    /// <param name="localPort">Local UDP port (0 = any).</param>
    /// <param name="remoteHost">Peer IP address or host name.</param>
    public UdpTransport(int localPort, string remoteHost, int remotePort, SessionKeys keys)
        : this(CreateDirect(localPort, remoteHost, remotePort, keys), keys)
    {
    }

    public RemoteInputBuffer Remote { get; } = new();

    /// <summary>Smoothed round-trip time in ticks (1/60 s), measured send-to-ack.</summary>
    public int RttTicks => _rtt.RttTicks;

    public double RttMilliseconds => _rtt.RttMilliseconds;

    private static DirectChannel CreateDirect(int localPort, string remoteHost, int remotePort, SessionKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys); // before any socket exists
        return new DirectChannel(localPort, remoteHost, remotePort);
    }

    public void SendInputs(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame, ulong checkHash)
    {
        if (_disposed) return;

        Span<byte> payload = stackalloc byte[InputPacket.EncodedLength(InputPacket.MaxFrames)];
        int payloadLength = InputPacket.Encode(firstFrame, bits, ackFrame, checkFrame, checkHash, payload);

        Span<byte> packet = stackalloc byte[PacketAuthenticator.EncodedLength(payloadLength)];
        // Pre-increment: sequence 0 is reserved/rejected by PacketReplayGuard.
        _tx.Stamp(payload[..payloadLength], Interlocked.Increment(ref _localSequence), packet);

        _rtt.OnSend(firstFrame + bits.Length - 1);
        _channel.Send(packet); // channels swallow transient send failures: the redundancy window re-sends next frame
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxDatagramBytes];
        Span<byte> payload = stackalloc byte[MaxDatagramBytes];
        Span<ushort> bits = stackalloc ushort[InputPacket.MaxFrames];

        while (!_disposed)
        {
            int received;
            try
            {
                received = _channel.Receive(buffer, ReceivePollMilliseconds);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (received >= 0) ProcessDatagram(buffer.AsSpan(0, received), payload, bits);
        }
    }

    private void ProcessDatagram(ReadOnlySpan<byte> packet, Span<byte> payloadScratch, Span<ushort> bitsScratch)
    {
        // Cheap structural filter first: runts never reach the crypto (the channel already dropped foreign sources).
        int payloadLength = packet.Length - PacketAuthenticator.SequenceBytes - PacketAuthenticator.MacBytes;
        if (payloadLength < InputPacket.HeaderBytes) return;

        if (!_rx.Verify(packet, payloadScratch, out uint sequence)) return;
        if (!_replayGuard.Accept(sequence)) return;
        if (!InputPacket.TryDecode(payloadScratch[..payloadLength], bitsScratch, out int firstFrame, out int ack, out int checkFrame, out ulong checkHash, out int count)) return;

        if (Remote.Accept(firstFrame, bitsScratch[..count], ack, checkFrame, checkHash)) _rtt.OnAck(ack);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsChannel) _channel.Dispose(); // wakes the receive thread
        _receiveThread.Join(500);
        _tx.Dispose();
        _rx.Dispose();
    }
}
