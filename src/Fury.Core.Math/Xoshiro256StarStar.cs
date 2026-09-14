using System.Numerics;

namespace Fury.Core.Math;

/// <summary>
/// xoshiro256** — the canonical simulation RNG. One instance per fight, seeded
/// deterministically; advancing it is part of the sim contract (never reseed mid-fight).
/// SplitMix64 is used for seeding (canonical pairing).
/// </summary>
public sealed class Xoshiro256StarStar
{
    private readonly ulong[] _s = new ulong[4];

    public Xoshiro256StarStar(ulong seed)
    {
        // SplitMix64 seeding, four streams.
        ulong z = seed;
        for (int i = 0; i < 4; i++)
        {
            z += 0x9E3779B97F4A7C15UL;
            ulong x = z;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            _s[i] = x ^ (x >> 31);
        }
        // All-zero state is degenerate; nudge.
        if ((_s[0] | _s[1] | _s[2] | _s[3]) == 0) _s[0] = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>Next 64 uniform bits. Wrap semantics on overflow.</summary>
    public ulong NextULong()
    {
        ulong result = BitOperations.RotateLeft(_s[1] * 5, 7) * 9;
        ulong t = _s[1] << 17;
        _s[2] ^= _s[0];
        _s[3] ^= _s[1];
        _s[1] ^= _s[2];
        _s[0] ^= _s[3];
        _s[2] ^= t;
        _s[3] = BitOperations.RotateLeft(_s[3], 45);
        return result;
    }

    /// <summary>Uniform in [0, n) with rejection sampling (no modulo bias).</summary>
    public int NextInt(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        ulong buckets = (ulong)n;
        ulong limit = ulong.MaxValue - (ulong.MaxValue % buckets);
        ulong r;
        do { r = NextULong(); } while (r >= limit);
        return (int)(r % buckets);
    }

    /// <summary>Copies the 4-word internal state for snapshot serialization.</summary>
    public void GetState(Span<ulong> dest)
    {
        if (dest.Length < 4) throw new ArgumentException("Destination needs 4 words.");
        dest[0] = _s[0];
        dest[1] = _s[1];
        dest[2] = _s[2];
        dest[3] = _s[3];
    }

    /// <summary>Restores a previously captured state (rollback save/restore contract).</summary>
    public void SetState(ReadOnlySpan<ulong> source)
    {
        if (source.Length < 4) throw new ArgumentException("Source needs 4 words.");
        _s[0] = source[0];
        _s[1] = source[1];
        _s[2] = source[2];
        _s[3] = source[3];
    }
}
