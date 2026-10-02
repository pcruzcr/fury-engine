using System.Buffers.Binary;

namespace Fury.Core.Net;

/// <summary>
/// Wire format of one input datagram (the payload inside the authenticated envelope).
/// Every packet carries a redundant window of the sender's recent inputs plus the highest
/// remote frame it has received contiguously (ack): a lost datagram self-heals on the next send
/// and the sender knows exactly where to resume, so packet loss never stalls the match.
/// Layout (LE): [version:1][count:1][firstFrame:4][ackFrame:4][checkFrame:4][checkHash:8][bits:2 x count].
/// The (checkFrame, checkHash) pair is a confirmed-state fingerprint used for desync detection.
/// </summary>
public static class InputPacket
{
    public const byte Version = 1;
    public const int MaxFrames = 32;
    public const int HeaderBytes = 22;

    public static int EncodedLength(int count) => HeaderBytes + (count * sizeof(ushort));

    /// <summary>Encodes a window starting at <paramref name="firstFrame"/>; returns bytes written.</summary>
    public static int Encode(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame, ulong checkHash, Span<byte> dest)
    {
        if (bits.Length is < 1 or > MaxFrames) throw new ArgumentOutOfRangeException(nameof(bits), $"1..{MaxFrames} frames per packet.");
        int length = EncodedLength(bits.Length);
        if (dest.Length < length) throw new ArgumentException("Destination too small.", nameof(dest));

        dest[0] = Version;
        dest[1] = (byte)bits.Length;
        BinaryPrimitives.WriteInt32LittleEndian(dest[2..], firstFrame);
        BinaryPrimitives.WriteInt32LittleEndian(dest[6..], ackFrame);
        BinaryPrimitives.WriteInt32LittleEndian(dest[10..], checkFrame);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[14..], checkHash);
        for (int i = 0; i < bits.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dest[(HeaderBytes + (i * sizeof(ushort)))..], bits[i]);
        }
        return length;
    }

    /// <summary>Strict decode: any structural deviation (version, length, negative frames) is rejected.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> payload, Span<ushort> bitsOut, out int firstFrame, out int ackFrame, out int checkFrame, out ulong checkHash, out int count)
    {
        firstFrame = 0;
        ackFrame = -1;
        checkFrame = -1;
        checkHash = 0;
        count = 0;
        if (payload.Length < HeaderBytes || payload[0] != Version) return false;

        int n = payload[1];
        if (n is < 1 or > MaxFrames || payload.Length != EncodedLength(n) || bitsOut.Length < n) return false;

        firstFrame = BinaryPrimitives.ReadInt32LittleEndian(payload[2..]);
        ackFrame = BinaryPrimitives.ReadInt32LittleEndian(payload[6..]);
        checkFrame = BinaryPrimitives.ReadInt32LittleEndian(payload[10..]);
        checkHash = BinaryPrimitives.ReadUInt64LittleEndian(payload[14..]);
        if (firstFrame < 0 || ackFrame < -1 || checkFrame < -1 || firstFrame > int.MaxValue - MaxFrames) return false;

        for (int i = 0; i < n; i++)
        {
            bitsOut[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload[(HeaderBytes + (i * sizeof(ushort)))..]);
        }
        count = n;
        return true;
    }
}
