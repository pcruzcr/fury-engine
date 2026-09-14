namespace Fury.Core.Rollback;

/// <summary>
/// Delta compression between consecutive sim snapshots laid out as int arrays (raw state
/// words). Only modified runs cross the wire: [startIndex, runLength, values…]. Merge
/// threshold packs gaps of unmodified words shorter than <see cref="MinGap"/> into the
/// current run to bound header overhead. Byte budget per frame drops from the full
/// snapshot (esquema 5, ~1068 B) to tens of bytes in steady state (ADR-004).
/// </summary>
public static class SnapshotDiffer
{
    /// <summary>Gaps of unmodified words up to this size are folded into runs.</summary>
    public const int MinGap = 3;

    /// <summary>
    /// Writes the delta of <paramref name="after"/> against <paramref name="before"/> into
    /// <paramref name="dest"/>. Returns the number of ints written (0 when identical).
    /// Both snapshots must have the same fixed length.
    /// </summary>
    public static int Compute(ReadOnlySpan<int> before, ReadOnlySpan<int> after, Span<int> dest)
    {
        if (before.Length != after.Length) throw new ArgumentException("Snapshot layouts must match.");
        int written = 0;
        int i = 0;
        while (i < after.Length)
        {
            if (before[i] == after[i])
            {
                i++;
                continue;
            }

            int start = i;

            // Extend the run: keep consuming through modified words and small gaps.
            int end = i;
            while (end < after.Length)
            {
                if (before[end] != after[end])
                {
                    end++;
                    continue;
                }
                bool gapWorthFolding = after.Length - end >= MinGap && IsModifiedSoon(before, after, end, MinGap);
                if (gapWorthFolding) { end++; continue; }
                break;
            }

            int count = end - start;
            if (written + 2 + count > dest.Length) throw new ArgumentException("Delta destination too small.");
            dest[written++] = start;
            dest[written++] = count;
            after.Slice(start, count).CopyTo(dest[written..]);
            written += count;
            i = end;
        }
        return written;
    }

    private static bool IsModifiedSoon(ReadOnlySpan<int> before, ReadOnlySpan<int> after, int from, int window)
    {
        int to = System.Math.Min(from + window, before.Length);
        for (int i = from; i < to; i++)
        {
            if (before[i] != after[i]) return true;
        }
        return false;
    }

    /// <summary>
    /// Reconstructs the snapshot: copies <paramref name="before"/> into
    /// <paramref name="dest"/> then applies the delta runs. Deterministic and exact.
    /// </summary>
    public static void Apply(ReadOnlySpan<int> before, ReadOnlySpan<int> delta, Span<int> dest)
    {
        if (dest.Length < before.Length) throw new ArgumentException("Destination smaller than snapshot.");
        before.CopyTo(dest);
        int i = 0;
        while (i < delta.Length)
        {
            int start = delta[i];
            int count = delta[i + 1];
            if (start < 0 || count < 0 || start + count > dest.Length || i + 2 + count > delta.Length)
            {
                throw new InvalidDataException("Corrupt delta: run out of bounds.");
            }
            delta.Slice(i + 2, count).CopyTo(dest[start..]);
            i += 2 + count;
        }
    }
}
