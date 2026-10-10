// Fighter.Core — FNV-1a 64 hash (§35).
// Fast, deterministic, byte-order explicit. NOT cryptographic; divergence detection only.
namespace Fighter.Core;

/// <summary>Deterministic 64-bit FNV-1a hasher for state payloads.</summary>
public static class StateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>Hashes <paramref name="data"/> (FNV-1a 64). Same bytes → same hash, every platform.</summary>
    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        ulong h = OffsetBasis;
        foreach (byte b in data)
        {
            h ^= b;
            h *= Prime;
        }
        return h;
    }
}
