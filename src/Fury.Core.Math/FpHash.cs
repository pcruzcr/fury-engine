using System.IO.Hashing;

namespace Fury.Core.Math;

/// <summary>
/// xxHash64 facade over System.IO.Hashing: the canonical state-hash of the engine.
/// Used for rollback verification, replay integrity chains and parity harnesses.
/// </summary>
public static class FpHash
{
    /// <summary>Hash a span of raw ints (e.g. Fp bits, entity fields) deterministically.</summary>
    public static ulong HashInts(ReadOnlySpan<int> values)
    {
        var bytes = MemoryMarshalAsBytes(values);
        return XxHash64.HashToUInt64(bytes);
    }

    private static ReadOnlySpan<byte> MemoryMarshalAsBytes(ReadOnlySpan<int> values)
    {
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(values);
    }
}
