using Fury.Core.Rollback;
using Xunit;

namespace Fury.Core.Tests.Rollback;

public class SnapshotDifferTests
{
    [Fact]
    public void IdenticalSnapshots_ProduceEmptyDelta()
    {
        int[] before = [1, 2, 3, 4, 5];
        int[] after = [1, 2, 3, 4, 5];
        var dest = new int[16];
        Assert.Equal(0, SnapshotDiffer.Compute(before, after, dest));
    }

    [Fact]
    public void ModifiedWords_FormRuns_AndRoundTrip()
    {
        int[] before = [10, 20, 30, 40, 50, 60, 70];
        int[] after = [10, 21, 30, 40, 50, 60, 70];

        var delta = new int[32];
        int len = SnapshotDiffer.Compute(before, after, delta);
        Assert.True(len > 0 && len < before.Length);

        var rebuilt = new int[before.Length];
        SnapshotDiffer.Apply(before, delta.AsSpan(0, len), rebuilt);
        Assert.Equal(after, rebuilt);
    }

    [Fact]
    public void GapFolding_PacksRunsAcrossShortUnmodifiedStretches()
    {
        int[] before = [0, 0, 0, 0, 0];
        int[] after = [7, 0, 0, 9, 0];

        var delta = new int[32];
        int len = SnapshotDiffer.Compute(before, after, delta);

        Assert.Equal(0, delta[0]);
        int runLength = delta[1];
        Assert.True(runLength >= 4);

        var rebuilt = new int[5];
        SnapshotDiffer.Apply(before, delta.AsSpan(0, len), rebuilt);
        Assert.Equal(after, rebuilt);
    }

    [Fact]
    public void Apply_CorruptDelta_Throws()
    {
        int[] before = [1, 2, 3];
        int[] corrupt = [0, 99];
        Assert.Throws<InvalidDataException>(() => SnapshotDiffer.Apply(before, corrupt, new int[3]));
    }
}
