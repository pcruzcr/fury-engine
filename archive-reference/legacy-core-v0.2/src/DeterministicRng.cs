// Fighter.Core — deterministic RNG (§36). SplitMix64. Seeded, snapshot-able, replay-able.
// No System.Random, no Guid, no Environment.TickCount anywhere in the simulation path.
namespace Fighter.Core;

/// <summary>Deterministic 64-bit RNG. State is a single ulong: trivially snapshot-able.</summary>
public sealed class DeterministicRng
{
    private ulong _state;
    /// <summary>Full internal state: snapshot/restore the RNG with one ulong.</summary>
    public ulong State => _state;

    /// <summary>Creates the RNG with an explicit <paramref name="seed"/>.</summary>
    public DeterministicRng(ulong seed) => _state = seed;
    /// <summary>Restores a previously captured <paramref name="state"/>.</summary>
    public void Restore(ulong state) => _state = state;

    /// <summary>Next 64-bit draw (SplitMix64).</summary>
    public ulong NextULong()
    {
        ulong z = (_state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Next 32 bits (upper half of <see cref="NextULong"/>).</summary>
    public uint NextUInt() => (uint)(NextULong() >> 32);

    /// <summary>Uniform uint in [0, bound). Rejection sampling: unbiased, still deterministic.</summary>
    public uint NextBounded(uint bound)
    {
        if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
        // 2^32 % bound threshold; standard Lemire-free rejection on 32 bits.
        uint threshold = (uint)(0x1_0000_0000UL % bound);
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return r % bound;
        }
    }
}
