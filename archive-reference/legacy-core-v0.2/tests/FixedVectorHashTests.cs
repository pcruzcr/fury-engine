// REFACTOR+HARDEN: Vector2Fixed + xxHash64 wrapper tests.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class Vector2FixedContractTests
{
    [Fact]
    public void Add_Sub_Mul_Exact()
    {
        var a = Vector2Fixed.FromInts(2, 3);
        var b = Vector2Fixed.FromInts(5, -1);
        Assert.Equal(Vector2Fixed.FromInts(7, 2), a + b);
        Assert.Equal(Vector2Fixed.FromInts(-3, 4), a - b);
        Assert.Equal(Vector2Fixed.FromInts(4, 6), a * Fixed.FromInt(2));
    }

    [Fact]
    public void LengthSquared_ThreeFour_IsTwentyFive()
    {
        var v = Vector2Fixed.FromInts(3, 4);
        Assert.Equal(Fixed.FromInt(25), v.LengthSquared());
    }

    [Fact]
    public void Zero_IsAdditiveIdentity() =>
        Assert.Equal(Vector2Fixed.FromInts(9, -4), Vector2Fixed.FromInts(9, -4) + Vector2Fixed.Zero);

    [Fact]
    public void Equality_Operators()
    {
        Assert.True(Vector2Fixed.FromInts(1, 2) == Vector2Fixed.FromInts(1, 2));
        Assert.True(Vector2Fixed.FromInts(1, 2) != Vector2Fixed.FromInts(1, 3));
    }
}

public sealed class DeterminismHashTests
{
    [Fact]
    public void EmptyInput_MatchesCanonicalVector()
    {
        // Canonical xxHash64("", seed 0) = 0xef46db3751d8e999 (published test vector).
        Assert.Equal(0xef46db3751d8e999UL, DeterminismHash.Hash(System.ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void SameBytes_SameHash_DifferentBytes_Differ()
    {
        byte[] a = { 1, 2, 3, 4 };
        byte[] b = { 1, 2, 3, 4 };
        byte[] c = { 1, 2, 3, 5 };
        Assert.Equal(DeterminismHash.Hash(a), DeterminismHash.Hash(b));
        Assert.NotEqual(DeterminismHash.Hash(a), DeterminismHash.Hash(c));
    }

    [Fact]
    public void Seed_SeparatesDomains()
    {
        byte[] a = { 9, 8, 7 };
        Assert.NotEqual(DeterminismHash.Hash(a, 0), DeterminismHash.Hash(a, 1));
        Assert.Equal(DeterminismHash.Hash(a, 42), DeterminismHash.Hash(a, 42));
    }

    [Fact]
    public void CoversEverySnapshotByte_OrderSensitive()
    {
        var sim = new DeterministicSim(5);
        for (int f = 0; f < 10; f++) sim.Step(InputGen.P1(f), InputGen.P2(f));
        var payload = sim.Capture().Payload;
        ulong h1 = DeterminismHash.Hash(payload);
        var tampered = (byte[])payload.Clone();
        tampered[tampered.Length - 1] ^= 0x01; // flip the very last byte
        Assert.NotEqual(h1, DeterminismHash.Hash(tampered));
    }
}
