using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Fury.Core.Sim;

/// <summary>
/// Broadphase AABB overlap filter using 128-bit integer SIMD over raw Q16.16 bits.
/// Integer SIMD is bit-identical across x86/ARM, so batching four boxes per instruction
/// does not threaten cross-platform determinism (ADR-002). Policy: this filter is a
/// conservative gate — any box pair it reports as overlapping must still pass the exact
/// Fp AABB test in the narrowphase; borderline rejects fall back to the scalar path with
/// identical integer semantics.
/// Input layout is SoA (Structure of Arrays): one span per coordinate field.
/// </summary>
public static class AabbSimd
{
    /// <summary>
    /// For each i, computes results[i] = overlap(A_i, B_i) with half-open intervals
    /// [min, max). All eight input spans must have the same length.
    /// </summary>
    public static void OverlapMask(
        ReadOnlySpan<int> aMinX, ReadOnlySpan<int> aMinY, ReadOnlySpan<int> aMaxX, ReadOnlySpan<int> aMaxY,
        ReadOnlySpan<int> bMinX, ReadOnlySpan<int> bMinY, ReadOnlySpan<int> bMaxX, ReadOnlySpan<int> bMaxY,
        Span<bool> results)
    {
        int length = aMinX.Length;
        if (aMinY.Length != length || aMaxX.Length != length || aMaxY.Length != length
            || bMinX.Length != length || bMinY.Length != length || bMaxX.Length != length || bMaxY.Length != length
            || results.Length < length)
        {
            throw new ArgumentException("SoA spans must be same length and results must have room.");
        }

        int vectorLimit = length - (length % 4);
        if (Vector128.IsHardwareAccelerated)
        {
            for (int i = 0; i < vectorLimit; i += 4)
            {
                var aMinXv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(aMinX), (nuint)i);
                var aMinYv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(aMinY), (nuint)i);
                var aMaxXv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(aMaxX), (nuint)i);
                var aMaxYv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(aMaxY), (nuint)i);
                var bMinXv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(bMinX), (nuint)i);
                var bMinYv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(bMinY), (nuint)i);
                var bMaxXv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(bMaxX), (nuint)i);
                var bMaxYv = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(bMaxY), (nuint)i);

                var mask = Vector128.LessThan(aMinXv, bMaxXv) & Vector128.LessThan(aMinYv, bMaxYv)
                         & Vector128.LessThan(bMinXv, aMaxXv) & Vector128.LessThan(bMinYv, aMaxYv);
                int bits = (int)Vector128.ExtractMostSignificantBits(mask);

                ref var outRef = ref MemoryMarshal.GetReference(results);
                for (int lane = 0; lane < 4; lane++)
                {
                    Unsafe.Add(ref outRef, i + lane) = ((bits >> lane) & 1) != 0;
                }
            }
        }

        for (int i = vectorLimit; i < length; i++)
        {
            results[i] = aMinX[i] < bMaxX[i] && aMinY[i] < bMaxY[i]
                      && bMinX[i] < aMaxX[i] && bMinY[i] < aMaxY[i];
        }
    }
}
