using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Fury.Core.Net;

/// <summary>
/// Wire format between a player and a relay server (the relay is a dumb UDP forwarder for players that cannot reach each
/// other directly, e.g. both behind NAT). Player to relay: [magic "FRYR"][version][room id:16][payload]; relay to player:
/// [magic][version][payload]. The payload is the normal authenticated game datagram, so the relay can drop or delay
/// traffic but cannot read, forge or replay it (see <see cref="SessionCrypto"/>).
/// </summary>
public static class RelayProtocol
{
    public const uint Magic = 0x52595246; // "FRYR"
    public const byte Version = 1;
    public const int RoomIdBytes = 16;
    public const int ClientHeaderBytes = 4 + 1 + RoomIdBytes;
    public const int ServerHeaderBytes = 4 + 1;

    /// <summary>Largest game datagram the relay carries (the transport never sends more).</summary>
    public const int MaxPayloadBytes = 512;

    public const int MaxDatagramBytes = ClientHeaderBytes + MaxPayloadBytes;

    public static int WriteClientHeader(Span<byte> destination, ReadOnlySpan<byte> roomId)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[4] = Version;
        roomId.CopyTo(destination[5..]);
        return ClientHeaderBytes;
    }

    public static int WriteServerHeader(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        destination[4] = Version;
        return ServerHeaderBytes;
    }

    public static bool HasHeader(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= ServerHeaderBytes && BinaryPrimitives.ReadUInt32LittleEndian(datagram) == Magic && datagram[4] == Version;
}

/// <summary>
/// Room code shared by two players out of band (chat, voice...). One slow key derivation yields the relay room id and
/// the secret <c>matchToken</c> that goes into the key exchange: a relay (or anyone else) that does not know the code
/// cannot man-in-the-middle the handshake, because it derives different session keys and every packet is rejected.
/// A relay operator can still try to guess a short code offline, so use <see cref="GenerateCode"/> rather than words.
/// </summary>
public static class RelayRoom
{
    public const int MinCodeLength = 8;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 32 symbols, no 0/O/1/I
    private const int Iterations = 100_000;
    private static readonly byte[] Salt = "fury-relay-room/v1"u8.ToArray();

    /// <summary>A new random code with 50 bits of entropy, formatted XXXXX-XXXXX.</summary>
    public static string GenerateCode()
    {
        var chars = new char[11];
        for (int i = 0, o = 0; i < 10; i++)
        {
            if (i == 5) chars[o++] = '-';
            chars[o++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>Upper-cases the code and drops separators so "abcde-fghjk" and "ABCDEFGHJK" are the same room.</summary>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var builder = new StringBuilder(code.Length);
        foreach (char c in code)
        {
            if (c is '-' or ' ') continue;
            builder.Append(char.ToUpperInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>Derives the relay room id (sent in clear to the relay) and the handshake match token (never sent).</summary>
    /// <exception cref="ArgumentException">The code is shorter than <see cref="MinCodeLength"/> symbols.</exception>
    public static (byte[] RoomId, byte[] MatchToken) Derive(string code)
    {
        string normalized = Normalize(code);
        if (normalized.Length < MinCodeLength) throw new ArgumentException($"The room code needs at least {MinCodeLength} characters.", nameof(code));

        byte[] okm = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalized), Salt, Iterations, HashAlgorithmName.SHA256, RelayProtocol.RoomIdBytes + 32);
        try
        {
            return (okm[..RelayProtocol.RoomIdBytes], okm[RelayProtocol.RoomIdBytes..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(okm);
        }
    }
}

/// <summary>Player side of the relay: wraps every datagram with the room header and unwraps what the relay forwards.</summary>
public sealed class RelayChannel : IDatagramChannel
{
    private readonly Socket _socket;
    private readonly SocketAddress _relay;
    private readonly SocketAddress _from;
    private readonly byte[] _roomId;
    private readonly byte[] _receiveBuffer = new byte[RelayProtocol.MaxDatagramBytes + 64];
    private volatile bool _disposed;

    /// <param name="relayHost">Relay IP address or host name.</param>
    /// <param name="roomId">16-byte room id, see <see cref="RelayRoom.Derive"/>.</param>
    /// <param name="localPort">Local UDP port (0 = any).</param>
    public RelayChannel(string relayHost, int relayPort, ReadOnlySpan<byte> roomId, int localPort = 0)
    {
        if (roomId.Length != RelayProtocol.RoomIdBytes) throw new ArgumentException($"A room id is {RelayProtocol.RoomIdBytes} bytes.", nameof(roomId));
        IPAddress address = DatagramSockets.Resolve(relayHost);
        _socket = DatagramSockets.Bind(address.AddressFamily, localPort);
        _roomId = roomId.ToArray();
        _relay = new IPEndPoint(address, relayPort).Serialize();
        _from = new SocketAddress(address.AddressFamily);
    }

    public int LocalPort => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public void Send(ReadOnlySpan<byte> datagram)
    {
        if (_disposed || datagram.Length > RelayProtocol.MaxPayloadBytes) return;
        Span<byte> frame = stackalloc byte[RelayProtocol.MaxDatagramBytes];
        int header = RelayProtocol.WriteClientHeader(frame, _roomId);
        datagram.CopyTo(frame[header..]);
        try
        {
            _socket.SendTo(frame[..(header + datagram.Length)], SocketFlags.None, _relay);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // Transient failure or concurrent close: the redundancy window re-sends.
        }
    }

    public int Receive(Span<byte> buffer, int timeoutMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            if (!_socket.Poll(System.Math.Max(0, timeoutMilliseconds) * 1000, SelectMode.SelectRead)) return -1;
            int received = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, _from);
            if (!_from.Equals(_relay) || !RelayProtocol.HasHeader(_receiveBuffer.AsSpan(0, received))) return -1;

            int payload = received - RelayProtocol.ServerHeaderBytes;
            if (payload > buffer.Length) return -1;
            _receiveBuffer.AsSpan(RelayProtocol.ServerHeaderBytes, payload).CopyTo(buffer);
            return payload;
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
        _socket.Dispose();
    }
}

/// <summary>Limits of a <see cref="RelayServer"/>; the defaults suit a small public relay.</summary>
public sealed record RelayServerOptions
{
    public int MaxRooms { get; init; } = 1024;

    /// <summary>Rooms one source address may hold at once (stops a single host from filling the table).</summary>
    public int MaxRoomsPerAddress { get; init; } = 8;

    /// <summary>A room with no traffic for this long is dropped.</summary>
    public int IdleTimeoutSeconds { get; init; } = 60;

    /// <summary>Datagrams per second accepted from one player (a match sends about 70).</summary>
    public int MaxPacketsPerSecond { get; init; } = 300;
}

/// <summary>
/// UDP relay: pairs two players that name the same room and forwards each one's datagrams to the other. It keeps no
/// secrets and never sends more bytes than it received (no amplification), forwards only inside a room to its two
/// registered endpoints, and bounds rooms, rooms per address, idle time and per-player packet rate.
/// </summary>
public sealed class RelayServer : IDisposable
{
    private readonly record struct RoomKey(ulong High, ulong Low);

    private sealed class Peer(SocketAddress address, long now)
    {
        public SocketAddress Address { get; } = address;
        public long WindowStart { get; set; } = now;
        public int WindowCount { get; set; }
    }

    private sealed class Room(IPAddress owner, long now)
    {
        public IPAddress Owner { get; } = owner;
        public Peer? A { get; set; }
        public Peer? B { get; set; }
        public long LastSeen { get; set; } = now;
    }

    private readonly Socket _socket;
    private readonly RelayServerOptions _options;
    private readonly Dictionary<RoomKey, Room> _rooms = [];
    private readonly byte[] _buffer = new byte[RelayProtocol.MaxDatagramBytes + 64];
    private readonly byte[] _forward = new byte[RelayProtocol.MaxDatagramBytes];
    private readonly SocketAddress _from;
    private readonly Lock _gate = new();
    private Thread? _thread;
    private long _forwarded;
    private long _dropped;
    private volatile bool _disposed;

    /// <param name="port">UDP port to listen on (0 = any, see <see cref="Port"/>).</param>
    /// <param name="bindAddress">Local address to bind; null for all IPv4 interfaces.</param>
    public RelayServer(int port, RelayServerOptions? options = null, IPAddress? bindAddress = null)
    {
        _options = options ?? new RelayServerOptions();
        bindAddress ??= IPAddress.Any;
        _socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.Bind(new IPEndPoint(bindAddress, port));
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
        _from = new SocketAddress(bindAddress.AddressFamily);
    }

    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    public int ActiveRooms
    {
        get { lock (_gate) return _rooms.Count; }
    }

    public long Forwarded => Interlocked.Read(ref _forwarded);

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Serves on a background thread until disposed.</summary>
    public void Start()
    {
        if (_thread != null) throw new InvalidOperationException("The relay is already running.");
        _thread = new Thread(() => Run(CancellationToken.None)) { IsBackground = true, Name = "FuryRelay" };
        _thread.Start();
    }

    /// <summary>Serves on the calling thread until cancelled or disposed.</summary>
    public void Run(CancellationToken cancellation)
    {
        long nextSweep = Environment.TickCount64 + 1000;
        while (!_disposed && !cancellation.IsCancellationRequested)
        {
            try
            {
                if (_socket.Poll(250_000, SelectMode.SelectRead))
                {
                    int received = _socket.ReceiveFrom(_buffer, SocketFlags.None, _from);
                    lock (_gate) Handle(_buffer.AsSpan(0, received), Environment.TickCount64);
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize && !_disposed)
            {
                Drop(); // larger than any legitimate datagram: the OS discarded the tail
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                if (_disposed) break;
            }

            long now = Environment.TickCount64;
            if (now >= nextSweep)
            {
                SweepExpired(now);
                nextSweep = now + 1000;
            }
        }
    }

    /// <summary>Drops rooms that have been idle longer than the configured timeout.</summary>
    public void SweepExpired(long nowMilliseconds)
    {
        long limit = _options.IdleTimeoutSeconds * 1000L;
        lock (_gate)
        {
            List<RoomKey>? dead = null;
            foreach (var (key, room) in _rooms)
            {
                if (nowMilliseconds - room.LastSeen > limit) (dead ??= []).Add(key);
            }
            if (dead != null) foreach (var key in dead) _rooms.Remove(key);
        }
    }

    private void Handle(ReadOnlySpan<byte> datagram, long now)
    {
        if (datagram.Length < RelayProtocol.ClientHeaderBytes || datagram.Length > RelayProtocol.MaxDatagramBytes || !RelayProtocol.HasHeader(datagram))
        {
            Drop();
            return;
        }

        var key = new RoomKey(
            BinaryPrimitives.ReadUInt64LittleEndian(datagram[5..]),
            BinaryPrimitives.ReadUInt64LittleEndian(datagram[13..]));

        if (!_rooms.TryGetValue(key, out var room))
        {
            room = CreateRoom(key, now);
            if (room == null)
            {
                Drop();
                return;
            }
        }

        Peer? sender = Register(room, now);
        if (sender == null || !WithinRate(sender, now))
        {
            Drop();
            return;
        }

        room.LastSeen = now;
        var payload = datagram[RelayProtocol.ClientHeaderBytes..];
        if (payload.Length == 0) return; // registration / keep-alive only

        Peer? target = ReferenceEquals(sender, room.A) ? room.B : room.A;
        if (target == null)
        {
            Drop(); // the other player has not arrived yet; the sender's redundancy re-sends
            return;
        }

        int header = RelayProtocol.WriteServerHeader(_forward);
        payload.CopyTo(_forward.AsSpan(header));
        try
        {
            _socket.SendTo(_forward.AsSpan(0, header + payload.Length), SocketFlags.None, target.Address);
            Interlocked.Increment(ref _forwarded);
        }
        catch (SocketException)
        {
            Drop();
        }
    }

    private Room? CreateRoom(RoomKey key, long now)
    {
        if (_rooms.Count >= _options.MaxRooms) return null;

        var owner = ((IPEndPoint)new IPEndPoint(IPAddress.Any, 0).Create(_from)).Address;
        int owned = 0;
        foreach (var existing in _rooms.Values)
        {
            if (existing.Owner.Equals(owner)) owned++;
        }
        if (owned >= _options.MaxRoomsPerAddress) return null;

        var room = new Room(owner, now);
        _rooms[key] = room;
        return room;
    }

    /// <summary>Returns the sender's slot, claiming a free one for a new endpoint; null when the room is full.</summary>
    private Peer? Register(Room room, long now)
    {
        if (room.A != null && _from.Equals(room.A.Address)) return room.A;
        if (room.B != null && _from.Equals(room.B.Address)) return room.B;

        if (room.A == null) return room.A = new Peer(Copy(_from), now);
        if (room.B == null) return room.B = new Peer(Copy(_from), now);
        return null;
    }

    private bool WithinRate(Peer peer, long now)
    {
        if (now - peer.WindowStart >= 1000)
        {
            peer.WindowStart = now;
            peer.WindowCount = 0;
        }
        return ++peer.WindowCount <= _options.MaxPacketsPerSecond;
    }

    private static SocketAddress Copy(SocketAddress source)
    {
        var copy = new SocketAddress(source.Family, source.Size);
        source.Buffer.CopyTo(copy.Buffer);
        return copy;
    }

    private void Drop() => Interlocked.Increment(ref _dropped);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
        _thread?.Join(1000);
    }
}
