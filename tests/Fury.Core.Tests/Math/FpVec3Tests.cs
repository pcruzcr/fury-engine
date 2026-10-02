using Fury.Core.Math;
using Xunit;

namespace Fury.Core.Tests.Math;

/// <summary>
/// Tests for FpVec3 (3D vector over fixed-point).
/// </summary>
public class FpVec3Tests
{
    [Fact]
    public void FpVec3_Zero_IsZero()
    {
        var v = FpVec3.Zero;

        Assert.Equal(Fp.Zero, v.X);
        Assert.Equal(Fp.Zero, v.Y);
        Assert.Equal(Fp.Zero, v.Z);
    }

    [Fact]
    public void FpVec3_One_IsOne()
    {
        var v = FpVec3.One;

        Assert.Equal(Fp.One, v.X);
        Assert.Equal(Fp.One, v.Y);
        Assert.Equal(Fp.One, v.Z);
    }

    [Fact]
    public void FpVec3_Constructor_SetsComponents()
    {
        var v = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));

        Assert.Equal(Fp.FromInt(1), v.X);
        Assert.Equal(Fp.FromInt(2), v.Y);
        Assert.Equal(Fp.FromInt(3), v.Z);
    }

    [Fact]
    public void FpVec3_Constructor2D_SetsZToZero()
    {
        var v = new FpVec3(Fp.FromInt(1), Fp.FromInt(2));

        Assert.Equal(Fp.FromInt(1), v.X);
        Assert.Equal(Fp.FromInt(2), v.Y);
        Assert.Equal(Fp.Zero, v.Z);
    }

    [Fact]
    public void FpVec3_LengthSquared_ReturnsCorrectValue()
    {
        var v = new FpVec3(Fp.FromInt(3), Fp.FromInt(4), Fp.Zero);

        Fp result = v.LengthSquared;

        Assert.Equal(Fp.FromInt(25), result);
    }

    [Fact]
    public void FpVec3_Length_ReturnsCorrectValue()
    {
        var v = new FpVec3(Fp.FromInt(3), Fp.FromInt(4), Fp.Zero);

        Fp result = v.Length;

        Assert.Equal(Fp.FromInt(5), result);
    }

    [Fact]
    public void FpVec3_Length_ZeroVector_ReturnsZero()
    {
        var v = FpVec3.Zero;

        Fp result = v.Length;

        Assert.Equal(Fp.Zero, result);
    }

    [Fact]
    public void FpVec3_Normalized_ReturnsUnitVector()
    {
        var v = new FpVec3(Fp.FromInt(3), Fp.FromInt(4), Fp.Zero);

        var result = v.Normalized();

        Assert.Equal(Fp.FromInt(3) / Fp.FromInt(5), result.X);
        Assert.Equal(Fp.FromInt(4) / Fp.FromInt(5), result.Y);
        Assert.Equal(Fp.Zero, result.Z);
    }

    [Fact]
    public void FpVec3_Normalized_ZeroVector_ReturnsZero()
    {
        var v = FpVec3.Zero;

        var result = v.Normalized();

        Assert.Equal(FpVec3.Zero, result);
    }

    [Fact]
    public void FpVec3_Dot_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(5), Fp.FromInt(6));

        Fp result = a.Dot(b);

        Assert.Equal(Fp.FromInt(32), result);
    }

    [Fact]
    public void FpVec3_Cross_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(0), Fp.FromInt(0));
        var b = new FpVec3(Fp.FromInt(0), Fp.FromInt(1), Fp.FromInt(0));

        var result = a.Cross(b);

        Assert.Equal(Fp.FromInt(0), result.X);
        Assert.Equal(Fp.FromInt(0), result.Y);
        Assert.Equal(Fp.FromInt(1), result.Z);
    }

    [Fact]
    public void FpVec3_DistanceSquared_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(6), Fp.FromInt(3));

        Fp result = a.DistanceSquared(b);

        Assert.Equal(Fp.FromInt(25), result);
    }

    [Fact]
    public void FpVec3_Distance_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(0), Fp.FromInt(0), Fp.FromInt(0));
        var b = new FpVec3(Fp.FromInt(3), Fp.FromInt(4), Fp.FromInt(0));

        Fp result = a.Distance(b);

        Assert.Equal(Fp.FromInt(5), result);
    }

    [Fact]
    public void FpVec3_Add_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(5), Fp.FromInt(6));

        var result = a + b;

        Assert.Equal(Fp.FromInt(5), result.X);
        Assert.Equal(Fp.FromInt(7), result.Y);
        Assert.Equal(Fp.FromInt(9), result.Z);
    }

    [Fact]
    public void FpVec3_Subtract_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(4), Fp.FromInt(5), Fp.FromInt(6));
        var b = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));

        var result = a - b;

        Assert.Equal(Fp.FromInt(3), result.X);
        Assert.Equal(Fp.FromInt(3), result.Y);
        Assert.Equal(Fp.FromInt(3), result.Z);
    }

    [Fact]
    public void FpVec3_MultiplyByScalar_ReturnsCorrectValue()
    {
        var v = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var s = Fp.FromInt(2);

        var result = v * s;

        Assert.Equal(Fp.FromInt(2), result.X);
        Assert.Equal(Fp.FromInt(4), result.Y);
        Assert.Equal(Fp.FromInt(6), result.Z);
    }

    [Fact]
    public void FpVec3_DivideByScalar_ReturnsCorrectValue()
    {
        var v = new FpVec3(Fp.FromInt(2), Fp.FromInt(4), Fp.FromInt(6));
        var s = Fp.FromInt(2);

        var result = v / s;

        Assert.Equal(Fp.FromInt(1), result.X);
        Assert.Equal(Fp.FromInt(2), result.Y);
        Assert.Equal(Fp.FromInt(3), result.Z);
    }

    [Fact]
    public void FpVec3_Negate_ReturnsCorrectValue()
    {
        var v = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));

        var result = -v;

        Assert.Equal(Fp.FromInt(-1), result.X);
        Assert.Equal(Fp.FromInt(-2), result.Y);
        Assert.Equal(Fp.FromInt(-3), result.Z);
    }

    [Fact]
    public void FpVec3_Equality_ReturnsTrueForEqualVectors()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));

        Assert.True(a == b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void FpVec3_Equality_ReturnsFalseForDifferentVectors()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(5), Fp.FromInt(6));

        Assert.False(a == b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void FpVec3_Inequality_ReturnsTrueForDifferentVectors()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(5), Fp.FromInt(6));

        Assert.True(a != b);
    }

    [Fact]
    public void FpVec3_Lerp_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(0), Fp.FromInt(0), Fp.FromInt(0));
        var b = new FpVec3(Fp.FromInt(10), Fp.FromInt(10), Fp.FromInt(10));
        var t = Fp.FromRatio(1, 2);

        var result = FpVec3.Lerp(a, b, t);

        Assert.Equal(Fp.FromInt(5), result.X);
        Assert.Equal(Fp.FromInt(5), result.Y);
        Assert.Equal(Fp.FromInt(5), result.Z);
    }

    [Fact]
    public void FpVec3_Min_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(5), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(2), Fp.FromInt(6));

        var result = FpVec3.Min(a, b);

        Assert.Equal(Fp.FromInt(1), result.X);
        Assert.Equal(Fp.FromInt(2), result.Y);
        Assert.Equal(Fp.FromInt(3), result.Z);
    }

    [Fact]
    public void FpVec3_Max_ReturnsCorrectValue()
    {
        var a = new FpVec3(Fp.FromInt(1), Fp.FromInt(5), Fp.FromInt(3));
        var b = new FpVec3(Fp.FromInt(4), Fp.FromInt(2), Fp.FromInt(6));

        var result = FpVec3.Max(a, b);

        Assert.Equal(Fp.FromInt(4), result.X);
        Assert.Equal(Fp.FromInt(5), result.Y);
        Assert.Equal(Fp.FromInt(6), result.Z);
    }

    [Fact]
    public void FpVec3_Clamp_ReturnsCorrectValue()
    {
        var value = new FpVec3(Fp.FromInt(5), Fp.FromInt(-5), Fp.FromInt(15));
        var min = new FpVec3(Fp.FromInt(0), Fp.FromInt(0), Fp.FromInt(0));
        var max = new FpVec3(Fp.FromInt(10), Fp.FromInt(10), Fp.FromInt(10));

        var result = FpVec3.Clamp(value, min, max);

        Assert.Equal(Fp.FromInt(5), result.X);
        Assert.Equal(Fp.FromInt(0), result.Y);
        Assert.Equal(Fp.FromInt(10), result.Z);
    }

    [Fact]
    public void FpVec3_ToString_ReturnsCorrectFormat()
    {
        var v = new FpVec3(Fp.FromInt(1), Fp.FromInt(2), Fp.FromInt(3));

        string result = v.ToString();

        Assert.Contains("1", result);
        Assert.Contains("2", result);
        Assert.Contains("3", result);
    }
}
