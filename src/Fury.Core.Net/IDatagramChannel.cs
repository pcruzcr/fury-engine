using System.Net;
using System.Net.Sockets;

namespace Fury.Core.Net;

/// <summary>
/// A bidirectional datagram pipe to ONE peer, hiding how it is reached: straight over UDP (<see cref="DirectChannel"/>) or
/// through a rendezvous/relay server (<see cref="RelayChannel"/>). The handshake and the input transport only talk to this
/// interface, so every security property (directional keys, HMAC, replay window, desync checks) holds identically on both
/// paths; the relay can drop or delay packets but can never forge them.
/// </summary>
public interface IDatagramChannel : IDisposable
{
    /// <summary>Sends one datagram to the peer. Never throws for transient network errors (the redundancy window re-sends).</summary>
    void Send(ReadOnlySpan<byte> datagram);

    /// <summary>
    /// Waits up to <paramref name="timeoutMilliseconds"/> for a datagram from the peer. Returns its length, or -1 when the
    /// timeout elapsed or the datagram was not from the peer / not valid for this channel (the caller simply tries again).
    /// Throws <see cref="ObjectDisposedException"/> once the channel is disposed.
    /// </summary>
    int Receive(Span<byte> buffer, int timeoutMilliseconds);
}

/// <summary>Plain UDP to a known peer address. Datagrams from any other source are discarded before parsing.</summary>
public sealed class DirectChannel : IDatagramChannel
{
    private readonly Socket _socket;
    private readonly SocketAddress _remote;
    private readonly SocketAddress _from;
    private volatile bool _disposed;

    /// <param name="localPort">Local UDP port (0 = any).</param>
    /// <param name="remoteHost">Peer IP address or host name.</param>
    public DirectChannel(int localPort, string remoteHost, int remotePort)
    {
        IPAddress address = DatagramSockets.Resolve(remoteHost);
        _socket = DatagramSockets.Bind(address.AddressFamily, localPort);

        RemoteEndPoint = new IPEndPoint(address, remotePort);
        _remote = RemoteEndPoint.Serialize();
        _from = new SocketAddress(address.AddressFamily);
    }

    public IPEndPoint RemoteEndPoint { get; }

    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public void Send(ReadOnlySpan<byte> datagram)
    {
        if (_disposed) return;
        try
        {
            _socket.SendTo(datagram, SocketFlags.None, _remote);
        }
        catch (SocketException)
        {
            // Transient failure (network unreachable): the next window re-sends.
        }
        catch (ObjectDisposedException)
        {
            // Closed concurrently.
        }
    }

    public int Receive(Span<byte> buffer, int timeoutMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            if (!_socket.Poll(System.Math.Max(0, timeoutMilliseconds) * 1000, SelectMode.SelectRead)) return -1;
            int received = _socket.ReceiveFrom(buffer, SocketFlags.None, _from);
            return _from.Equals(_remote) ? received : -1; // foreign sources never reach the crypto or the parser
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Thread.Sleep(1); // transient socket error: do not spin, the caller keeps listening
            return -1;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose(); // unblocks a pending Poll / ReceiveFrom
    }
}

/// <summary>Socket creation shared by the channels.</summary>
internal static class DatagramSockets
{
    private const int WindowsUdpConnReset = -1744830452; // SIO_UDP_CONNRESET

    /// <summary>Creates a UDP socket bound to the wildcard address of <paramref name="family"/> on <paramref name="localPort"/> (0 = any).</summary>
    public static Socket Bind(AddressFamily family, int localPort)
    {
        var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, localPort));
            if (OperatingSystem.IsWindows())
            {
                // Windows reports ICMP "port unreachable" as a receive error; it must not break the loop.
                try { socket.IOControl(WindowsUdpConnReset, [0, 0, 0, 0], null); }
                catch (SocketException) { /* best effort */ }
            }
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Parses an IP literal or resolves a host name to its first address.</summary>
    public static IPAddress Resolve(string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        return IPAddress.TryParse(host, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(host).FirstOrDefault() ?? throw new ArgumentException($"Cannot resolve host '{host}'.", nameof(host));
    }
}
