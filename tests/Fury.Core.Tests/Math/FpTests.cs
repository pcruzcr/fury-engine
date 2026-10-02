using Fury.Core.Math;
using Xunit;

namespace Fury.Core.Tests.Math;

public class FpTests
{
    [Fact]
    public void FromInt_RoundtripsExactly()
    {
        Assert.Equal(0x10000, Fp.FromInt(1).Raw);
        Assert.Equal(-0x50000, Fp.FromInt(-5).Raw);
        Assert.Equal(0, Fp.Zero.Raw);
    }

    [Fact]
    public void FromRatio_IsExactForTerminatingDivisions()
    {
        var half = Fp.FromRatio(1, 2);
        Assert.Equal(0x8000, half.Raw);
        Assert.Equal(Fp.Half, half);
    }

    [Fact]
    public void Multiply_IsQ16Dot16()
    {
        var a = Fp.FromRatio(3, 2);
        var b = Fp.FromRatio(2, 1);
        Assert.Equal(Fp.FromInt(3), a * b);
    }

    [Fact]
    public void Divide_Roundtrips()
    {
        var a = Fp.FromRatio(7, 3);
        var b = Fp.FromRatio(11, 5);
        var q = a / b;
        var recovered = q * b;
        Assert.True(Fp.Abs(recovered - a).Raw <= 2);
    }

    [Fact]
    public void Sqrt_OfKnownSquares()
    {
        Assert.Equal(Fp.FromInt(4), Fp.Sqrt(Fp.FromInt(16)));
        Assert.Equal(Fp.One, Fp.Sqrt(Fp.One));
        Assert.Equal(Fp.Zero, Fp.Sqrt(Fp.Zero));
    }

    [Fact]
    public void Overflow_Wraps_ByPolicy()
    {
        var wrapped = Fp.MaxValue + Fp.FromRaw(1);
        Assert.Equal(Fp.MinValue.Raw, wrapped.Raw);
    }
}

public class FpVec2Tests
{
    [Fact]
    public void ManhattanLen_SumsAbsComponents()
    {
        var v = new FpVec2(Fp.FromInt(-3), Fp.FromInt(4));
        Assert.Equal(Fp.FromInt(7), v.ManhattanLen());
    }

    [Fact]
    public void Dot_Product_IsExact()
    {
        var a = new FpVec2(Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec2(Fp.FromInt(4), Fp.FromInt(-1));
        Assert.Equal(Fp.FromInt(5), a.Dot(b));
    }
}

public class XoshiroTests
{
    [Fact]
    public void SameSeed_SameSequence()
    {
        var a = new Xoshiro256StarStar(42);
        var b = new Xoshiro256StarStar(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeed_DifferentSequence()
    {
        var a = new Xoshiro256StarStar(1);
        var b = new Xoshiro256StarStar(2);
        var differs = false;
        for (int i = 0; i < 64; i++) if (a.NextULong() != b.NextULong()) differs = true;
        Assert.True(differs);
    }

    [Fact]
    public void NextInt_RespectsRange()
    {
        var rng = new Xoshiro256StarStar(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            var v = rng.NextInt(10);
            Assert.InRange(v, 0, 9);
            seen.Add(v);
        }
        Assert.Equal(10, seen.Count);
    }
}

public class FpHashTests
{
    [Fact]
    public void HashInts_IsDeterministicAndDistinct()
    {
        var h1 = FpHash.HashInts(stackalloc[] { 1, 2, 3 });
        var h2 = FpHash.HashInts(stackalloc[] { 1, 2, 3 });
        var h3 = FpHash.HashInts(stackalloc[] { 1, 2, 4 });
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
    }
}
