using System.Security.Cryptography;

namespace Fury.Core.Net;

/// <summary>
/// Per-packet authentication for the P2P netcode: each datagram carries its sequence
/// (Little-Endian) and an HMAC-SHA256 over payload || sequence. Verification uses constant
/// time comparison. Session keys are 256-bit, derived per session during handshake
/// (HKDF, F2) — never a static key embedded in the binary (ADR-004).
/// Wire layout: [payload…][seq:4 LE][mac:32].
/// </summary>
public sealed class PacketAuthenticator : IDisposable
{
    public const int SequenceBytes = 4;
    public const int MacBytes = 32;
    public const int MinKeyBytes = 32;

    private readonly HMACSHA256 _hmac;

    public PacketAuthenticator(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length < MinKeyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionKey), $"Session keys must be >= {MinKeyBytes} bytes.");
        }
        _hmac = new HMACSHA256(sessionKey.ToArray());
    }

    public static int EncodedLength(int payloadLength) => payloadLength + SequenceBytes + MacBytes;

    /// <summary>Stamps sequence and MAC. Dest must be <see cref="EncodedLength"/> of the payload.</summary>
    public void Stamp(ReadOnlySpan<byte> payload, uint sequence, Span<byte> dest)
    {
        if (dest.Length < EncodedLength(payload.Length)) throw new ArgumentException("Destination too small.");
        payload.CopyTo(dest);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dest[payload.Length..], sequence);
        var macSpan = dest[(payload.Length + SequenceBytes)..];
        _hmac.TryComputeHash(dest[..(payload.Length + SequenceBytes)], macSpan, out _);
    }

    /// <summary>
    /// Verifies MAC and extracts the payload + sequence. Returns false on any tamper,
    /// truncation or malformed packet — callers must drop the datagram without touching sim state.
    /// </summary>
    public bool Verify(ReadOnlySpan<byte> packet, Span<byte> payloadOut, out uint sequence)
    {
        sequence = 0;
        if (packet.Length < SequenceBytes + MacBytes || payloadOut.Length < packet.Length - SequenceBytes - MacBytes)
        {
            return false;
        }

        int payloadLength = packet.Length - SequenceBytes - MacBytes;
        sequence = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(packet[payloadLength..]);

        Span<byte> expected = stackalloc byte[MacBytes];
        _hmac.TryComputeHash(packet[..(payloadLength + SequenceBytes)], expected, out _);
        if (!CryptographicOperations.FixedTimeEquals(expected, packet[(payloadLength + SequenceBytes)..]))
        {
            return false;
        }

        packet[..payloadLength].CopyTo(payloadOut);
        return true;
    }

    public void Dispose()
    {
        _hmac.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Sliding replay window (64 sequences) around the highest in-order sequence seen.
/// Drops duplicates and packets older than the window before they can reach the sim.
/// </summary>
public sealed class PacketReplayGuard
{
    private const int WindowBits = 64;
    private ulong _bitfield;
    private uint _highest;

    public PacketReplayGuard() => _highest = 0; // first accepted sequence is 1

    /// <summary>True when the sequence is fresh (never seen, within window). Records it on success.</summary>
    public bool Accept(uint sequence)
    {
        if (sequence == 0) return false;
        if (sequence > _highest)
        {
            ulong gap = sequence - _highest;
            _bitfield = gap >= WindowBits ? 0 : _bitfield << (int)gap;
            _bitfield |= 1UL;
            _highest = sequence;
            return true;
        }

        ulong distance = _highest - sequence;
        if (distance >= WindowBits) return false;
        ulong bit = 1UL << (int)distance;
        if ((_bitfield & bit) != 0) return false;
        _bitfield |= bit;
        return true;
    }
}
