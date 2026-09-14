using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

public class AabbSimdTests
{
    [Fact]
    public void OverlapMask_MatchesScalarSemantics()
    {
        // Layout: 6 test pairs, mixing overlaps, misses and touching edges (half-open).
        int[] aMinX = [0, 10, 0, 0, 0, -50];
        int[] aMinY = [0, 10, 0, 0, 0, 0];
        int[] aMaxX = [10, 20, 10, 10, 10, -40];
        int[] aMaxY = [10, 20, 10, 10, 10, 10];

        int[] bMinX = [5, 21, 10, -5, 100, -45];   // pair 2 (index 2): touching edge -> NOT overlap
        int[] bMinY = [5, 21, 5, -5, 100, 0];
        int[] bMaxX = [15, 31, 20, 5, 110, -35];   // index 4: no overlap; index 5: negative coords overlap
        int[] bMaxY = [15, 31, 15, 5, 110, 10];

        var results = new bool[6];
        AabbSimd.OverlapMask(aMinX, aMinY, aMaxX, aMaxY, bMinX, bMinY, bMaxX, bMaxY, results);

        Assert.Equal([true, false, false, true, false, true], results);
    }

    [Fact]
    public void OverlapMask_VectorPath_Equals_ScalarTail()
    {
        // 9 boxes: 2 full SIMD chunks + 1 scalar tail element; both paths must agree.
        const int n = 9;
        var aMinX = new int[n];
        var aMinY = new int[n];
        var aMaxX = new int[n];
        var aMaxY = new int[n];
        var bMinX = new int[n];
        var bMinY = new int[n];
        var bMaxX = new int[n];
        var bMaxY = new int[n];

        for (int i = 0; i < n; i++)
        {
            aMinX[i] = i * 10; aMinY[i] = 0; aMaxX[i] = i * 10 + 8; aMaxY[i] = 8;
            bMinX[i] = i * 10 + 4; bMinY[i] = 4; bMaxX[i] = i * 10 + 12; bMaxY[i] = 12;
        }

        var simd = new bool[n];
        AabbSimd.OverlapMask(aMinX, aMinY, aMaxX, aMaxY, bMinX, bMinY, bMaxX, bMaxY, simd);

        for (int i = 0; i < n; i++)
        {
            bool scalar = aMinX[i] < bMaxX[i] && aMinY[i] < bMaxY[i]
                       && bMinX[i] < aMaxX[i] && bMinY[i] < aMaxY[i];
            Assert.Equal(scalar, simd[i]);
        }
    }

    [Fact]
    public void OverlapMask_RejectsMismatchedSpans()
    {
        Assert.Throws<ArgumentException>(() => AabbSimd.OverlapMask(
            [0], [0], [1], [1],
            [0], [0], [1], [2, 3],
            new bool[1]));
    }
}
