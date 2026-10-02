using System.Net;
using System.Net.Sockets;

namespace Fury.Core.Net;

/// <summary>
/// Authenticated UDP transport for real network play. Every datagram is
/// [InputPacket payload][seq:4][HMAC-SHA256:32] signed with a DIRECTIONAL key (see
/// <see cref="SessionKeys"/>), replay-filtered and source-filtered before any parsing. The
/// receive path runs on one dedicated thread over a preallocated buffer: no per-packet
/// allocations, and a malformed datagram can never terminate it.
/// </summary>
public sealed class UdpTransport : INetTransport, IDisposable
{
    private const int MaxDatagramBytes = 512;
    private const int WindowsUdpConnReset = -1744830452; // SIO_UDP_CONNRESET

    private readonly Socket _socket;
    private readonly PacketAuthenticator _tx;
    private readonly PacketAuthenticator _rx;
    private readonly PacketReplayGuard _replayGuard = new();
    private readonly RttEstimator _rtt = new();
    private readonly Thread _receiveThread;
    private SocketAddress? _remote;
    private uint _localSequence;
    private volatile bool _disposed;

    /// <param name="localPort">Local UDP port (0 = any).</param>
    /// <param name="remoteHost">Peer address or host name; null to learn the peer from the first authenticated packet.</param>
    /// <param name="keys">Directional keys from <see cref="HandshakeManager"/>. Required: an all-zero default key
    /// once let anyone forge authenticated packets (ADR-004).</param>
    public UdpTransport(int localPort, string? remoteHost, int remotePort, SessionKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _tx = new PacketAuthenticator(keys.SendKey);
        _rx = new PacketAuthenticator(keys.ReceiveKey);

        IPAddress? remoteAddress = null;
        if (remoteHost != null && !IPAddress.TryParse(remoteHost, out remoteAddress))
        {
            remoteAddress = Dns.GetHostAddresses(remoteHost).FirstOrDefault()
                ?? throw new ArgumentException($"Cannot resolve host '{remoteHost}'.", nameof(remoteHost));
        }

        var family = remoteAddress?.AddressFamily ?? AddressFamily.InterNetwork;
        _socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, localPort));
            if (OperatingSystem.IsWindows())
            {
                // Windows reports ICMP "port unreachable" as a receive error; it must not break the loop.
                try { _socket.IOControl(WindowsUdpConnReset, [0, 0, 0, 0], null); }
                catch (SocketException) { /* best effort */ }
            }
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        if (remoteAddress != null)
        {
            RemoteEndPoint = new IPEndPoint(remoteAddress, remotePort);
            _remote = RemoteEndPoint.Serialize();
        }

        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "FuryUdpReceive" };
        _receiveThread.Start();
    }

    public RemoteInputBuffer Remote { get; } = new();

    public IPEndPoint? RemoteEndPoint { get; private set; }

    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    /// <summary>Smoothed round-trip time in ticks (1/60 s), measured send-to-ack.</summary>
    public int RttTicks => _rtt.RttTicks;

    public double RttMilliseconds => _rtt.RttMilliseconds;

    public void SendInputs(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame, ulong checkHash)
    {
        SocketAddress? remote = _remote;
        if (remote == null || _disposed) return;

        Span<byte> payload = stackalloc byte[InputPacket.EncodedLength(InputPacket.MaxFrames)];
        int payloadLength = InputPacket.Encode(firstFrame, bits, ackFrame, checkFrame, checkHash, payload);

        Span<byte> packet = stackalloc byte[PacketAuthenticator.EncodedLength(payloadLength)];
        // Pre-increment: sequence 0 is reserved/rejected by PacketReplayGuard.
        _tx.Stamp(payload[..payloadLength], Interlocked.Increment(ref _localSequence), packet);

        _rtt.OnSend(firstFrame + bits.Length - 1);
        try
        {
            _socket.SendTo(packet, SocketFlags.None, remote);
        }
        catch (SocketException)
        {
            // Transient send failure (unreachable network): the redundancy window re-sends next frame.
        }
        catch (ObjectDisposedException)
        {
            // Closed concurrently.
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxDatagramBytes];
        Span<byte> payload = stackalloc byte[MaxDatagramBytes];
        Span<ushort> bits = stackalloc ushort[InputPacket.MaxFrames];
        var from = new SocketAddress(_socket.AddressFamily);

        while (!_disposed)
        {
            int received;
            try
            {
                received = _socket.ReceiveFrom(buffer, SocketFlags.None, from);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (_disposed)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // transient socket error: keep listening
            }

            ProcessDatagram(buffer.AsSpan(0, received), from, payload, bits);
        }
    }

    private void ProcessDatagram(ReadOnlySpan<byte> packet, SocketAddress from, Span<byte> payloadScratch, Span<ushort> bitsScratch)
    {
        // Cheap structural filters first: foreign sources and runts never reach the crypto.
        if (_remote != null && !from.Equals(_remote)) return;
        int payloadLength = packet.Length - PacketAuthenticator.SequenceBytes - PacketAuthenticator.MacBytes;
        if (payloadLength < InputPacket.HeaderBytes) return;

        if (!_rx.Verify(packet, payloadScratch, out uint sequence)) return;
        if (!_replayGuard.Accept(sequence)) return;
        if (!InputPacket.TryDecode(payloadScratch[..payloadLength], bitsScratch, out int firstFrame, out int ack, out int checkFrame, out ulong checkHash, out int count)) return;

        if (_remote == null)
        {
            // Learn the peer from the first AUTHENTICATED datagram (NAT-friendly rendezvous).
            var learned = new SocketAddress(from.Family, from.Size);
            from.Buffer.CopyTo(learned.Buffer);
            _remote = learned;
            RemoteEndPoint = (IPEndPoint)new IPEndPoint(IPAddress.Any, 0).Create(learned);
        }

        if (Remote.Accept(firstFrame, bitsScratch[..count], ack, checkFrame, checkHash)) _rtt.OnAck(ack);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose(); // unblocks ReceiveFrom
        _receiveThread.Join(500);
        _tx.Dispose();
        _rx.Dispose();
    }
}
