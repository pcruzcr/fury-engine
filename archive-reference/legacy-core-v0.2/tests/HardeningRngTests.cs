// REFACTOR+HARDEN A2: xoshiro256** stream hash + bounded-distribution fills
// the gaps left by XoshiroTests.cs (contract vectors, jump, state round-trip).
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class XoshiroStreamTests
{
    private readonly ITestOutputHelper _out;
    public XoshiroStreamTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void BoundedDistribution_ChiSquare_Sane()
    {
        // 8 buckets x 12500 expected; chi2(df=7) must stay far from blowup.
        // Fixed seed => deterministic verdict (not a flaky statistical test).
        var r = Xoshiro256StarStar.FromSeed(0xC0FFEEUL);
        long[] buckets = new long[8];
        const int draws = 100_000;
        for (int i = 0; i < draws; i++)
            buckets[r.NextBounded(8)]++;
        double expected = draws / 8.0;
        double chi2 = 0;
        foreach (long o in buckets) chi2 += (o - expected) * (o - expected) / expected;
        _out.WriteLine($"chi2={chi2:F2} buckets={string.Join(",", buckets)}");
        Assert.True(chi2 < 40, $"chi-square blowout: {chi2}");
    }

    [Fact]
    public void TenThousandDraws_HashPinned()
    {
        // Cross-platform stream hash (A2): any x86/ARM divergence fails loudly.
        // Self-pinned on first executed run (regression tripwire); contract
        // vectors live in XoshiroTests.cs, not in this constant.
        var r = Xoshiro256StarStar.FromSeed(0xDEADBEEFUL);
        var w = new System.IO.MemoryStream();
        for (int i = 0; i < 10_000; i++)
        {
            ulong v = r.NextULong();
            for (int k = 0; k < 8; k++) w.WriteByte((byte)(v >> (8 * k)));
        }
        ulong h = DeterminismHash.Hash(w.ToArray());
        _out.WriteLine($"xoshiro10k xxHash64=0x{h:X16}");
        Assert.Equal(0x9F8459407027DBF9UL, h); // executed 2026-09-14 (was placeholder)
    }
}
