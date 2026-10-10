// REFACTOR+HARDEN A1: Fixed Q16.16 exhaustive contract tests (ADR-004, WRAP policy).
// Every assertion uses exact raw values or proved properties — no invented vectors.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class FixedHardeningTests
{
    [Fact]
    public void Add_Exact() =>
        Assert.Equal(Fixed.FromInt(5), Fixed.FromInt(2) + Fixed.FromInt(3));

    [Fact]
    public void Subtract_Exact() =>
        Assert.Equal(Fixed.FromInt(1), Fixed.FromInt(3) - Fixed.FromInt(2));

    [Fact]
    public void Multiply_HalfTimesHalf_IsQuarter() =>
        Assert.Equal(Fixed.FromRaw(16384), Fixed.FromRaw(32768) * Fixed.FromRaw(32768));

    [Fact]
    public void Multiply_NegativeTimesPositive() =>
        Assert.Equal(Fixed.FromInt(-6), Fixed.FromInt(-2) * Fixed.FromInt(3));

    [Fact]
    public void Divide_OneByTwo_IsHalf() =>
        Assert.Equal(Fixed.FromRaw(32768), Fixed.One / Fixed.FromInt(2));

    [Fact]
    public void Divide_Negative_TruncatesTowardZero() =>
        Assert.Equal(Fixed.FromRaw(-32768), Fixed.FromInt(-1) / Fixed.FromInt(2));

    [Fact]
    public void Divide_ByZero_Throws() =>
        Assert.Throws<DivideByZeroException>(() => _ = Fixed.One / Fixed.Zero);

    [Fact]
    public void Sqrt_Zero_IsZero() => Assert.Equal(Fixed.Zero, Fixed.Sqrt(Fixed.Zero));

    [Fact]
    public void Sqrt_One_IsOne() => Assert.Equal(Fixed.One, Fixed.Sqrt(Fixed.One));

    [Fact]
    public void Sqrt_Four_IsTwo() => Assert.Equal(Fixed.FromInt(2), Fixed.Sqrt(Fixed.FromInt(4)));

    [Fact]
    public void Sqrt_Nine_IsThree() => Assert.Equal(Fixed.FromInt(3), Fixed.Sqrt(Fixed.FromInt(9)));

    [Fact]
    public void Sqrt_Two_Squared_RoundTrips()
    {
        // Error bound: floor loses < 1 LSB on sqrt; squaring amplifies to
        // < (2t+1)/65536 raw units ≈ 2.83 for t≈92681 → tolerance 4 is rigorous.
        var s = Fixed.Sqrt(Fixed.FromInt(2));
        Assert.Equal(Fixed.FromRaw(92681), s);
        var back = s * s;
        Assert.True(System.Math.Abs(back.Raw - Fixed.FromInt(2).Raw) <= 4,
            $"sqrt(2)^2 raw={back.Raw}, want ~131072");
    }

    [Fact]
    public void Sqrt_Negative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = Fixed.Sqrt(Fixed.MinusOne));

    [Fact]
    public void Zero_AdditiveIdentity() =>
        Assert.Equal(Fixed.FromInt(7), Fixed.FromInt(7) + Fixed.Zero);

    [Fact]
    public void Negative_Ordering() =>
        Assert.True(Fixed.FromInt(-1) < Fixed.Zero && Fixed.MinusOne == Fixed.FromInt(-1));

    [Fact]
    public void Max_Min_AreRawExtremes()
    {
        Assert.Equal(int.MaxValue, Fixed.MaxValue.Raw);
        Assert.Equal(int.MinValue, Fixed.MinValue.Raw);
    }

    [Fact]
    public void Overflow_Add_WrapsPerPolicy() =>
        Assert.Equal(Fixed.FromRaw(unchecked(int.MaxValue + Fixed.OneRaw)), Fixed.MaxValue + Fixed.One);

    [Fact]
    public void Overflow_Sub_WrapsPerPolicy() =>
        Assert.Equal(Fixed.FromRaw(unchecked(int.MinValue - Fixed.OneRaw)), Fixed.MinValue - Fixed.One);

    [Fact]
    public void Overflow_Mul_WrapsWithoutThrow()
    {
        var ex = Record.Exception(() => _ = Fixed.MaxValue * Fixed.FromInt(2));
        Assert.Null(ex);
    }

    [Fact]
    public void Boundary_FromInt_RoundTrip()
    {
        for (int v = -1000; v <= 1000; v++)
            Assert.Equal(v, Fixed.FromInt(v).ToInt());
    }

    [Fact]
    public void Boundary_Half_LosesFractionOn_ToInt() =>
        Assert.Equal(0, new Fixed(32767).ToInt());

    [Fact]
    public void Abs_Negative_IsPositive() =>
        Assert.Equal(Fixed.FromInt(3), Fixed.Abs(Fixed.FromInt(-3)));

    [Fact]
    public void Negation_DoubleNegation_IsIdentity() =>
        Assert.Equal(Fixed.FromInt(42), -(-Fixed.FromInt(42)));
}
