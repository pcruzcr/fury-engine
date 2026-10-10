// REFACTOR+HARDEN A2: xoshiro256** contract tests (ADR-005).
// Reference: Blackman & Vigna xoshiro256starstar.c (fetched 2026-09-14).
// Hand-verified vectors: state (1,2,3,4) → first draw rotl(2*5,7)*9 = 11520,
// state update zeroes s1 → second draw 0. Computed from the reference formula,
// not copied from any output.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class XoshiroContractTests
{
    [Fact]
    public void FirstDraw_MatchesReferenceFormula() =>
        Assert.Equal(11520UL, new Xoshiro256StarStar(1, 2, 3, 4).NextULong());

    [Fact]
    public void SecondDraw_MatchesReferenceTransition()
    {
        var rng = new Xoshiro256StarStar(1, 2, 3, 4);
        _ = rng.NextULong();
        Assert.Equal(0UL, rng.NextULong());
    }

    [Fact]
    public void AllZeroState_Throws()
    {
        Assert.Throws<ArgumentException>(() => _ = new Xoshiro256StarStar(0, 0, 0, 0));
        var rng = Xoshiro256StarStar.FromSeed(42);
        Assert.Throws<ArgumentException>(() => rng.RestoreState(0, 0, 0, 0));
    }

    [Fact]
    public void FromSeed_IsReproducible()
    {
        var a = Xoshiro256StarStar.FromSeed(0xDEADBEEF);
        var b = Xoshiro256StarStar.FromSeed(0xDEADBEEF);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void FromSeed_Zero_IsLegal() => _ = Xoshiro256StarStar.FromSeed(0).NextULong();

    [Fact]
    public void Sequence_10kDraws_AllDistinct()
    {
        var rng = Xoshiro256StarStar.FromSeed(1234);
        var seen = new System.Collections.Generic.HashSet<ulong>();
        for (int i = 0; i < 10_000; i++)
            Assert.True(seen.Add(rng.NextULong()), $"Collision at draw {i}.");
    }

    [Fact]
    public void State_RoundTrips_ThroughCopyAndRestore()
    {
        var rng = Xoshiro256StarStar.FromSeed(99);
        for (int i = 0; i < 500; i++) _ = rng.NextULong();
        Span<ulong> state = stackalloc ulong[4];
        rng.CopyStateTo(state);
        var clone = Xoshiro256StarStar.FromSeed(1);
        clone.RestoreState(state[0], state[1], state[2], state[3]);
        for (int i = 0; i < 500; i++)
            Assert.Equal(rng.NextULong(), clone.NextULong());
    }

    [Fact]
    public void CopyStateTo_WrongLength_Throws()
    {
        var rng = Xoshiro256StarStar.FromSeed(1);
        Assert.Throws<ArgumentException>(() => rng.CopyStateTo(stackalloc ulong[3]));
    }

    [Fact]
    public void Jump_IsReproducible_AndAdvances()
    {
        var a = Xoshiro256StarStar.FromSeed(7);
        var b = Xoshiro256StarStar.FromSeed(7);
        a.Jump(); b.Jump();
        for (int i = 0; i < 500; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Jump_LeavesUnjumpedStream()
    {
        var jumped = Xoshiro256StarStar.FromSeed(7);
        jumped.Jump();
        var plain = Xoshiro256StarStar.FromSeed(7);
        bool anyDifferent = false;
        for (int i = 0; i < 1000; i++)
            if (jumped.NextULong() != plain.NextULong()) { anyDifferent = true; break; }
        Assert.True(anyDifferent, "Jumped stream must diverge from the unjumped stream.");
    }

    [Fact]
    public void LongJump_DivergesFromSingleJump()
    {
        var j = Xoshiro256StarStar.FromSeed(7);
        j.Jump();
        var lj = Xoshiro256StarStar.FromSeed(7);
        lj.LongJump();
        bool anyDifferent = false;
        for (int i = 0; i < 1000; i++)
            if (j.NextULong() != lj.NextULong()) { anyDifferent = true; break; }
        Assert.True(anyDifferent, "LongJump stream must diverge from the Jump stream.");
    }

    [Fact]
    public void NextUInt_IsUpperHalfOfNextULong()
    {
        var a = Xoshiro256StarStar.FromSeed(5);
        var b = Xoshiro256StarStar.FromSeed(5);
        for (int i = 0; i < 200; i++)
            Assert.Equal((uint)(b.NextULong() >> 32), a.NextUInt());
    }

    [Fact]
    public void NextBounded_StaysInRange()
    {
        var rng = Xoshiro256StarStar.FromSeed(11);
        for (int i = 0; i < 5000; i++)
            Assert.True(rng.NextBounded(6) < 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = rng.NextBounded(0));
    }

    [Fact]
    public void ChiSquare_LowByte_Uniform()
    {
        var rng = Xoshiro256StarStar.FromSeed(0xC0FFEE);
        const int draws = 65536;
        const int bins = 256;
        var counts = new int[bins];
        for (int i = 0; i < draws; i++)
            counts[(int)(rng.NextULong() & 0xFF)]++;
        double expected = (double)draws / bins;
        double chi = 0;
        for (int i = 0; i < bins; i++)
        {
            double d = counts[i] - expected;
            chi += d * d / expected;
        }
        // Mean 255, sd ~22.6 for 255 df: 400 is >6 sigma above the mean.
        Assert.True(chi < 400, $"Chi-square too high: {chi:F1} (non-uniform stream suspected).");
    }

    [Fact]
    public void NoImplicitState_TwoInstances_Independent()
    {
        var a = Xoshiro256StarStar.FromSeed(1);
        var b = Xoshiro256StarStar.FromSeed(2);
        _ = a.NextULong();
        Assert.Equal(
            Xoshiro256StarStar.FromSeed(2).NextULong(),
            b.NextULong());
    }
}
