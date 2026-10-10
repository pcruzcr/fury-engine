// REFACTOR+HARDEN A1/D10: Fixed Q16.16 exhaustive arithmetic (ADR-004 documents
// the WRAP overflow policy tested here).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class FixedArithmeticTests
{
    [Fact]
    public void AddSub_Exact()
    {
        Assert.Equal(Fixed.FromInt(5), Fixed.FromInt(2) + Fixed.FromInt(3));
        Assert.Equal(Fixed.FromInt(-1), Fixed.FromInt(2) - Fixed.FromInt(3));
        Assert.Equal(Fixed.Zero, Fixed.FromInt(7) - Fixed.FromInt(7));
    }

    [Fact]
    public void MulDiv_Fractions()
    {
        var half = Fixed.FromInt(1) / Fixed.FromInt(2);
        Assert.Equal(Fixed.FromRaw(Fixed.OneRaw / 2), half);
        Assert.Equal(Fixed.One, half + half);
        Assert.Equal(Fixed.FromInt(6), Fixed.FromInt(4) * Fixed.FromInt(1) + Fixed.FromInt(2));
        var neg = Fixed.FromInt(-3) / Fixed.FromInt(2); // truncates toward zero
        Assert.Equal(Fixed.FromRaw(-(Fixed.OneRaw + Fixed.OneRaw / 2)), neg);
    }

    [Fact]
    public void Div_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => Fixed.FromInt(1) / Fixed.Zero);
    }

    [Fact]
    public void Overflow_Wraps_TwosComplement()
    {
        Assert.Equal(Fixed.MinValue, Fixed.MaxValue + Fixed.FromRaw(1));
        Assert.Equal(Fixed.MaxValue, Fixed.MinValue - Fixed.FromRaw(1));
        Assert.Equal(Fixed.FromRaw(-2), Fixed.MaxValue + Fixed.MaxValue); // raw wrap, documented
    }

    [Fact]
    public void ZeroAndNegative_Identities()
    {
        Assert.Equal(Fixed.Zero, Fixed.Zero * Fixed.FromInt(12345));
        Assert.Equal(Fixed.Zero, Fixed.Zero / Fixed.FromInt(-7));
        Assert.Equal(Fixed.FromInt(-4), -Fixed.FromInt(4));
        Assert.Equal(Fixed.MinValue, -Fixed.MinValue); // wraps per WRAP policy
        Assert.True(Fixed.FromInt(-1) < Fixed.Zero);
        Assert.True(Fixed.FromInt(-1) <= Fixed.FromInt(-1));
    }

    [Fact]
    public void Abs_Works_ExceptMinValueWraps()
    {
        Assert.Equal(Fixed.FromInt(3), Fixed.Abs(Fixed.FromInt(-3)));
        Assert.Equal(Fixed.FromInt(3), Fixed.Abs(Fixed.FromInt(3)));
        Assert.Equal(Fixed.MinValue, Fixed.Abs(Fixed.MinValue));
    }

    [Fact]
    public void ToInt_FloorsNegatives()
    {
        Assert.Equal(1, (Fixed.FromInt(3) / Fixed.FromInt(2)).ToInt());
        Assert.Equal(-2, (Fixed.FromInt(-3) / Fixed.FromInt(2)).ToInt()); // arithmetic shift = floor
    }

    [Fact]
    public void Sqrt_PerfectSquares()
    {
        Assert.Equal(Fixed.FromInt(2), Fixed.Sqrt(Fixed.FromInt(4)));
        Assert.Equal(Fixed.FromInt(3), Fixed.Sqrt(Fixed.FromInt(9)));
        Assert.Equal(Fixed.Zero, Fixed.Sqrt(Fixed.Zero));
        Assert.Equal(Fixed.One, Fixed.Sqrt(Fixed.One));
    }

    [Fact]
    public void Sqrt_Two_IsFloor()
    {
        // floor(sqrt(2) * 65536) = floor(92681.5) = 92681.
        Assert.Equal(Fixed.FromRaw(92681), Fixed.Sqrt(Fixed.FromInt(2)));
    }

    [Fact]
    public void Sqrt_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.Sqrt(Fixed.FromInt(-1)));
    }

    [Fact]
    public void Sqrt_MaxValue_DoesNotOverflow()
    {
        Fixed r = Fixed.Sqrt(Fixed.MaxValue);
        Assert.True(r > Fixed.Zero);
        Assert.True(r * r <= Fixed.MaxValue); // floor property: r^2 <= input
    }

    [Fact]
    public void Comparisons_AndEquality_Raw()
    {
        Assert.True(Fixed.FromRaw(5) == Fixed.FromRaw(5));
        Assert.True(Fixed.FromRaw(5) != Fixed.FromRaw(6));
        Assert.True(Fixed.FromRaw(5) < Fixed.FromRaw(6));
        Assert.True(Fixed.FromRaw(6) > Fixed.FromRaw(5));
        Assert.True(Fixed.FromRaw(5) >= Fixed.FromRaw(5));
        Assert.False(Fixed.FromRaw(5).Equals("not a fixed"));
        Assert.Equal(5, Fixed.FromRaw(5).GetHashCode());
        Assert.Equal(0, Fixed.FromRaw(5).CompareTo(Fixed.FromRaw(5)));
    }
}

public sealed class Vector2FixedTests
{
    [Fact]
    public void AddSubMul_Exact()
    {
        var a = Vector2Fixed.FromInts(1, 2);
        var b = Vector2Fixed.FromInts(3, -1);
        Assert.Equal(Vector2Fixed.FromInts(4, 1), a + b);
        Assert.Equal(Vector2Fixed.FromInts(-2, 3), a - b);
        Assert.Equal(Vector2Fixed.FromInts(2, 4), a * Fixed.FromInt(2));
        Assert.Equal(Vector2Fixed.Zero, Vector2Fixed.Zero);
    }

    [Fact]
    public void LengthSquared_AvoidsSqrt()
    {
        Assert.Equal(Fixed.FromInt(25), Vector2Fixed.FromInts(3, 4).LengthSquared());
    }

    [Fact]
    public void Equality_ValueSemantics()
    {
        Assert.True(Vector2Fixed.FromInts(1, 1) == Vector2Fixed.FromInts(1, 1));
        Assert.True(Vector2Fixed.FromInts(1, 1) != Vector2Fixed.FromInts(1, 2));
        Assert.False(Vector2Fixed.FromInts(1, 1).Equals("x"));
    }
}
