using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fury.Core.Net;

/// <summary>What the peer announced during the handshake.</summary>
public sealed record PeerSetup(string CharacterId, ulong ConfigHash, ulong CharacterHash);

/// <summary>
/// Bootstrap exchange that runs before <see cref="UdpTransport"/>: both peers repeatedly send
/// [magic "FRYH"][version][public key:65][configHash:8][characterHash:8][idLen:1][id], until the
/// peer's message arrives. The keys then go through <see cref="SessionCrypto"/> (directional keys,
/// transcript + optional matchmaker token). Runs on the calling thread with a hard timeout, never
/// allocates unboundedly and ignores anything that is not a well-formed handshake from the peer.
/// </summary>
public static class UdpHandshake
{
    private const uint HandshakeMagic = 0x48595246; // "FRYH"
    private const byte HandshakeVersion = 1;
    private const int MaxIdBytes = 32;

    /// <summary>Performs the exchange and returns the directional session keys plus what the peer announced.</summary>
    /// <exception cref="TimeoutException">The peer never answered.</exception>
    /// <exception cref="InvalidDataException">The peer sent an invalid key.</exception>
    public static (SessionKeys Keys, PeerSetup Peer) Run(
        int localPort,
        string remoteHost,
        int remotePort,
        string characterId,
        ulong configHash,
        ulong characterHash,
        ReadOnlySpan<byte> matchToken,
        TimeSpan timeout,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(remoteHost);
        ArgumentNullException.ThrowIfNull(characterId);
        byte[] idBytes = Encoding.UTF8.GetBytes(characterId);
        if (idBytes.Length > MaxIdBytes) throw new ArgumentException($"Character id is limited to {MaxIdBytes} UTF-8 bytes.", nameof(characterId));

        IPAddress remoteAddress = IPAddress.TryParse(remoteHost, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(remoteHost).FirstOrDefault() ?? throw new ArgumentException($"Cannot resolve host '{remoteHost}'.", nameof(remoteHost));
        var remote = new IPEndPoint(remoteAddress, remotePort);

        using var handshake = new HandshakeManager(matchToken);
        byte[] message = BuildMessage(handshake.GetPublicKey(), configHash, characterHash, idBytes);

        using var socket = new Socket(remoteAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(remoteAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, localPort));
        socket.ReceiveTimeout = 100;

        var remoteAddr = remote.Serialize();
        var from = new SocketAddress(remoteAddress.AddressFamily);
        byte[] buffer = new byte[256];
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        bool completed = false;
        PeerSetup? peer = null;
        long lingerUntil = long.MaxValue; // keep answering for a while after we heard the peer so it can finish too

        while (Environment.TickCount64 < deadline && Environment.TickCount64 < lingerUntil)
        {
            cancellation.ThrowIfCancellationRequested();
            try { socket.SendTo(message, SocketFlags.None, remoteAddr); }
            catch (SocketException) { /* peer not reachable yet */ }

            try
            {
                int received = socket.ReceiveFrom(buffer, SocketFlags.None, from);
                if (from.Equals(remoteAddr) && !completed && TryParse(buffer.AsSpan(0, received), out var peerKey, out peer))
                {
                    handshake.CompleteHandshake(peerKey);
                    completed = true;
                    lingerUntil = Environment.TickCount64 + 600;
                }
            }
            catch (SocketException)
            {
                // receive timeout or transient error: resend and keep waiting
            }

            if (completed) Thread.Sleep(20);
        }

        if (!completed || peer == null || handshake.SessionKeys == null) throw new TimeoutException("Handshake timed out: the peer did not answer.");
        return (new SessionKeys((byte[])handshake.SessionKeys.SendKey.Clone(), (byte[])handshake.SessionKeys.ReceiveKey.Clone()), peer);
    }

    private static byte[] BuildMessage(byte[] publicKey, ulong configHash, ulong characterHash, byte[] id)
    {
        var message = new byte[4 + 1 + publicKey.Length + 8 + 8 + 1 + id.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, HandshakeMagic);
        message[4] = HandshakeVersion;
        publicKey.CopyTo(message.AsSpan(5));
        int o = 5 + publicKey.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(o), configHash);
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(o + 8), characterHash);
        message[o + 16] = (byte)id.Length;
        id.CopyTo(message.AsSpan(o + 17));
        return message;
    }

    private static bool TryParse(ReadOnlySpan<byte> data, out byte[] publicKey, out PeerSetup? setup)
    {
        publicKey = [];
        setup = null;
        const int keyLength = 65;
        const int fixedLength = 4 + 1 + keyLength + 8 + 8 + 1;
        if (data.Length < fixedLength || BinaryPrimitives.ReadUInt32LittleEndian(data) != HandshakeMagic || data[4] != HandshakeVersion) return false;

        int o = 5 + keyLength;
        int idLength = data[o + 16];
        if (idLength > MaxIdBytes || data.Length != fixedLength + idLength) return false;

        string id;
        try { id = new UTF8Encoding(false, true).GetString(data.Slice(o + 17, idLength)); }
        catch (ArgumentException) { return false; }

        publicKey = data.Slice(5, keyLength).ToArray();
        setup = new PeerSetup(id, BinaryPrimitives.ReadUInt64LittleEndian(data[o..]), BinaryPrimitives.ReadUInt64LittleEndian(data[(o + 8)..]));
        return true;
    }
}
