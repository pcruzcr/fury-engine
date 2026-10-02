using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;

namespace Fury.Core.Math;

/// <summary>
/// xxHash64 facade over System.IO.Hashing: the canonical state-hash of the engine.
/// Used for rollback verification, replay integrity chains and parity harnesses.
/// The wire/hash byte order is always Little-Endian, so hashes match across platforms.
/// </summary>
public static class FpHash
{
    private const int StackChunkBytes = 512;

    /// <summary>
    /// Hash a span of raw ints (e.g. Fp bits, entity fields) deterministically.
    /// Allocation-free on little-endian hosts (every platform .NET ships on): the ints are hashed
    /// in place. The previous implementation allocated a byte[] per call, which broke the
    /// zero-GC contract of <c>FightSimulation.Step</c> whenever a tracer asked for the hash.
    /// </summary>
    public static ulong HashInts(ReadOnlySpan<int> values)
    {
        if (BitConverter.IsLittleEndian)
        {
            return XxHash64.HashToUInt64(MemoryMarshal.AsBytes(values));
        }

        // Big-endian fallback: stream through a small stack buffer, swapping each word.
        var hasher = new XxHash64();
        Span<byte> chunk = stackalloc byte[StackChunkBytes];
        int perChunk = StackChunkBytes / sizeof(int);
        for (int offset = 0; offset < values.Length; offset += perChunk)
        {
            int n = System.Math.Min(perChunk, values.Length - offset);
            for (int i = 0; i < n; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(chunk[(i * sizeof(int))..], values[offset + i]);
            }
            hasher.Append(chunk[..(n * sizeof(int))]);
        }
        return hasher.GetCurrentHashAsUInt64();
    }

    /// <summary>xxHash64 of raw bytes (replay chains, packet digests).</summary>
    public static ulong HashBytes(ReadOnlySpan<byte> bytes, ulong seed = 0) => XxHash64.HashToUInt64(bytes, (long)seed);

    /// <summary>Order-dependent combination of two hashes (hash chains, composite fingerprints).</summary>
    public static ulong Combine(ulong previous, ulong next)
    {
        Span<byte> buf = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, previous);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[8..], next);
        return XxHash64.HashToUInt64(buf);
    }

    /// <summary>
    /// Stable 32-bit identifier of a move/asset name. UTF-8 bytes are zero-padded to a multiple of
    /// four and hashed as little-endian words, so identifiers are identical on every platform and
    /// compatible with every table authored so far. Authoring-time only (allocates for long names).
    /// </summary>
    public static int HashName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int byteCount = Encoding.UTF8.GetByteCount(name);
        int padded = (byteCount + 3) & ~3;
        Span<byte> buffer = padded <= StackChunkBytes ? stackalloc byte[padded] : new byte[padded];
        buffer.Clear();
        Encoding.UTF8.GetBytes(name, buffer);
        return unchecked((int)XxHash64.HashToUInt64(buffer));
    }
}
