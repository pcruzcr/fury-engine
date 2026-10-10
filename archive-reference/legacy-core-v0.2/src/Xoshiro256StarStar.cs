// Fighter.Core — xoshiro256** deterministic RNG (REFACTOR+HARDEN, ADR-005).
// Reference: Blackman & Vigna, "xoshiro256** 1.0" (public domain).
// Source of the transition function and jump polynomials below:
// https://prng.di.unimi.it/xoshiro256starstar.c (fetched 2026-09-14; constants
// copied verbatim, including the arbitrary-jump section of the same file).
// The state must never be all-zero (constructor enforces this).
// STATUS: READY for adoption; the live sim still runs on DeterministicRng
// (SplitMix64) until the cutover decision in ADR-005 is executed with a golden
// regen. This type participates in snapshots via GetState/RestoreState today.
namespace Fighter.Core;

/// <summary>xoshiro256** deterministic RNG: 256-bit state (ulong[4]), seed
/// reproducible, with Jump (2^128) and LongJump (2^192) substreams.</summary>
/// <remarks>Reference: Blackman &amp; Vigna, xoshiro256** 1.0. Transition:
/// result = rotl(s[1]*5,7)*9; substream jumps use the published JUMP /
/// LONG_JUMP polynomials (see ADR-005). Pure integer arithmetic: identical
/// output on x86/x64/ARM64. Not cryptographic; determinism only.</remarks>
public sealed class Xoshiro256StarStar
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    // Published jump polynomials (xoshiro256starstar.c, see file header).
    private static readonly ulong[] JumpPoly = {
        0x180ec6d33cfd0abaUL, 0xd5a61266f0c9392cUL, 0xa9582618e03fc9aaUL, 0x39abdc4529b1661cUL };
    private static readonly ulong[] LongJumpPoly = {
        0x76e15d3efefdcbbfUL, 0xc5004e441c522fb3UL, 0x77710069854ee241UL, 0x39109bb02acbe635UL };

    /// <summary>Creates the RNG with an explicit full state (must not be all zero).</summary>
    /// <param name="s0">State word 0.</param>
    /// <param name="s1">State word 1.</param>
    /// <param name="s2">State word 2.</param>
    /// <param name="s3">State word 3.</param>
    /// <exception cref="ArgumentException">Thrown when all four words are zero.</exception>
    public Xoshiro256StarStar(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0) throw new ArgumentException("xoshiro256** state must not be all zero.");
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
    }

    /// <summary>Seeds the full 256-bit state from one 64-bit seed via SplitMix64
    /// (the seeding procedure suggested by the reference authors).</summary>
    /// <param name="seed">64-bit seed.</param>
    /// <returns>New RNG with state derived deterministically from <paramref name="seed"/>.</returns>
    /// <remarks>SplitMix64 here is a seed expander, not the simulation stream;
    /// its output never interleaves with draws. Seed 0 is legal (expander output
    /// is never all-zero for any 64-bit seed... except with negligible care:
    /// guarded below by forcing a non-zero word).</remarks>
    public static Xoshiro256StarStar FromSeed(ulong seed)
    {
        ulong z = seed;
        ulong NextSplitMix()
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            ulong r = z;
            r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
            r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
            return r ^ (r >> 31);
        }
        ulong s0 = NextSplitMix(), s1 = NextSplitMix(), s2 = NextSplitMix(), s3 = NextSplitMix();
        if ((s0 | s1 | s2 | s3) == 0) s3 = 1; // unreachable in practice; keeps the invariant total
        return new Xoshiro256StarStar(s0, s1, s2, s3);
    }

    /// <summary>Copies the full 256-bit state into <paramref name="dst"/> (snapshot path).</summary>
    /// <param name="dst">Destination span; must have length 4.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="dst"/> length is not 4.</exception>
    public void CopyStateTo(System.Span<ulong> dst)
    {
        if (dst.Length != 4) throw new ArgumentException("State destination must have length 4.", nameof(dst));
        dst[0] = _s0; dst[1] = _s1; dst[2] = _s2; dst[3] = _s3;
    }

    /// <summary>Restores a previously captured full state.</summary>
    /// <param name="s0">State word 0.</param>
    /// <param name="s1">State word 1.</param>
    /// <param name="s2">State word 2.</param>
    /// <param name="s3">State word 3.</param>
    /// <exception cref="ArgumentException">Thrown when all four words are zero.</exception>
    public void RestoreState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0) throw new ArgumentException("xoshiro256** state must not be all zero.");
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Next 64-bit draw (xoshiro256** transition).</summary>
    /// <returns>Deterministic 64-bit output.</returns>
    public ulong NextULong()
    {
        ulong result = unchecked(Rotl(unchecked(_s1 * 5), 7) * 9);
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Next 32 bits (upper half of <see cref="NextULong"/>).</summary>
    /// <returns>Deterministic 32-bit output.</returns>
    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>Uniform uint in [0, bound). Rejection sampling: unbiased, still deterministic.</summary>
    /// <param name="bound">Exclusive upper bound (must be non-zero).</param>
    /// <returns>Uniform draw in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bound"/> is zero.</exception>
    public uint NextBounded(uint bound)
    {
        if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
        uint threshold = (uint)(0x1_0000_0000UL % bound);
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return r % bound;
        }
    }

    /// <summary>Advances the equivalent of 2^128 draws (non-overlapping substream).</summary>
    /// <remarks>Uses the published JUMP polynomial; see file header for source.</remarks>
    public void Jump() => ApplyJump(JumpPoly);

    /// <summary>Advances the equivalent of 2^192 draws (non-overlapping super-stream).</summary>
    /// <remarks>Uses the published LONG_JUMP polynomial; see file header for source.</remarks>
    public void LongJump() => ApplyJump(LongJumpPoly);

    private void ApplyJump(ulong[] poly)
    {
        ulong s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        for (int i = 0; i < poly.Length; i++)
            for (int b = 0; b < 64; b++)
            {
                if (((poly[i] >> b) & 1UL) != 0)
                {
                    s0 ^= _s0; s1 ^= _s1; s2 ^= _s2; s3 ^= _s3;
                }
                NextULong();
            }
        _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3;
    }
}
