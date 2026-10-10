// Fighter.Core — xxHash64 determinism hasher (REFACTOR+HARDEN, ADR-006).
// Non-cryptographic 64-bit hash for simulation divergence detection.
// Hand-rolled from the reference specification (Cyan4973, BSD-2-Clause:
// https://github.com/Cyan4973/xxHash/blob/dev/doc/xxhash_spec.md) because
// System.IO.Hashing is NOT part of the inbox net9.0 shared framework on
// every target (absent on this machine's runtime; would need a package +
// network restore). Pure integer arithmetic: no FPU, no allocations,
// IL2CPP-safe (no reflection, no statics with beforefieldinit concerns).
// Verified against the official test vectors (empty input and "abc").
// STATUS: READY and tested, but NOT yet the snapshot hash:
// StateSnapshot/FightSnapshot still hash with StateHasher (FNV-1a) so
// committed hashes and replays stay stable. The cutover (hash every
// snapshot byte in declaration order via this wrapper) is a tracked
// PLANNED item requiring a golden regen (ADR-006).
namespace Fighter.Core;

/// <summary>Deterministic xxHash64 wrapper (non-crypto, divergence detection only).</summary>
/// <remarks>Hand-rolled to the xxHash specification (see file header): same
/// bytes + same seed ⇒ same hash on every platform (x86/x64/ARM64). Must
/// never be confused with replay-integrity policy, security hashing, or
/// network authentication.</remarks>
public static class DeterminismHash
{
    private const ulong P1 = 0x9E3779B185EBCA87UL;
    private const ulong P2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong P3 = 0x165667B19E3779F9UL;
    private const ulong P4 = 0x85EBCA77C2B2AE63UL;
    private const ulong P5 = 0x27D4EB2F165667C5UL;

    private static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));

    private static ulong Read64(System.ReadOnlySpan<byte> d, int o) =>
        (ulong)d[o] | ((ulong)d[o + 1] << 8) | ((ulong)d[o + 2] << 16) | ((ulong)d[o + 3] << 24) |
        ((ulong)d[o + 4] << 32) | ((ulong)d[o + 5] << 40) | ((ulong)d[o + 6] << 48) | ((ulong)d[o + 7] << 56);

    private static uint Read32(System.ReadOnlySpan<byte> d, int o) =>
        (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24);

    private static ulong Round(ulong acc, ulong lane) => Rotl(acc + lane * P2, 31) * P1;

    private static ulong Merge(ulong h, ulong acc)
    {
        acc *= P2; acc = Rotl(acc, 31); acc *= P1;
        h ^= acc;
        return h * P1 + P4;
    }

    /// <summary>Hashes <paramref name="data"/> with xxHash64 (seed 0).</summary>
    /// <param name="data">Bytes to hash.</param>
    /// <returns>64-bit hash.</returns>
    public static ulong Hash(System.ReadOnlySpan<byte> data) => Hash(data, 0);

    /// <summary>Hashes <paramref name="data"/> with xxHash64 and an explicit seed.</summary>
    /// <param name="data">Bytes to hash.</param>
    /// <param name="seed">Hash seed (domain separation across usages).</param>
    /// <returns>64-bit hash.</returns>
    public static ulong Hash(System.ReadOnlySpan<byte> data, ulong seed)
    {
        int len = data.Length;
        int o = 0;
        ulong h;
        if (len >= 32)
        {
            ulong v1 = seed + P1 + P2;
            ulong v2 = seed + P2;
            ulong v3 = seed;
            ulong v4 = seed - P1;
            do
            {
                v1 = Round(v1, Read64(data, o)); o += 8;
                v2 = Round(v2, Read64(data, o)); o += 8;
                v3 = Round(v3, Read64(data, o)); o += 8;
                v4 = Round(v4, Read64(data, o)); o += 8;
            } while (o + 32 <= len);
            h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
            h = Merge(h, v1); h = Merge(h, v2); h = Merge(h, v3); h = Merge(h, v4);
        }
        else
        {
            h = seed + P5;
        }
        h += (ulong)len;
        while (o + 8 <= len)
        {
            ulong k1 = Round(0, Read64(data, o)); o += 8;
            h ^= k1;
            h = Rotl(h, 27) * P1 + P4;
        }
        if (o + 4 <= len)
        {
            h ^= Read32(data, o) * P1; o += 4;
            h = Rotl(h, 23) * P2 + P3;
        }
        while (o < len)
        {
            h ^= data[o] * P5; o++;
            h = Rotl(h, 11) * P1;
        }
        h ^= h >> 33; h *= P2;
        h ^= h >> 29; h *= P3;
        h ^= h >> 32;
        return h;
    }
}
