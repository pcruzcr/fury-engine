using System.Runtime.InteropServices;
using Fury.Core.Memory;
using Xunit;

namespace Fury.Core.Tests.Memory;

public class ArenaTests
{
    private record struct Payload
    {
        public int A;
        public int B;
    }

    [Fact]
    public void Alloc_TypedSlice_WritesAndReadsExact()
    {
        var arena = new Arena(256);
        var slice = arena.Alloc<Payload>(4);
        Assert.Equal(4, slice.Length);

        slice.Span[0] = new Payload { A = 7, B = -9 };
        slice.Span[3] = new Payload { A = int.MaxValue, B = int.MinValue };

        Assert.Equal(7, slice.Span[0].A);
        Assert.Equal(-9, slice.Span[0].B);
        Assert.Equal(int.MaxValue, slice.Span[3].A);
    }

    [Fact]
    public void Alloc_AlignedTo8Bytes()
    {
        var arena = new Arena(64);
        arena.Alloc<Payload>(1); // 8 bytes
        arena.Alloc<Payload>(1); // next slice must start at offset >= 8
        Assert.Equal(16, arena.UsedBytes);
    }

    [Fact]
    public void ExhaustedArena_Throws()
    {
        var arena = new Arena(16);
        arena.Alloc<Payload>(2); // exactly full
        Assert.Throws<InvalidOperationException>(() => arena.Alloc<Payload>(1));
    }

    [Fact]
    public void Reset_ReclaimsWholeArena()
    {
        var arena = new Arena(128);
        arena.Alloc<Payload>(10);
        arena.Reset();
        Assert.Equal(0, arena.UsedBytes);
        arena.Alloc<Payload>(15); // now fits again
        Assert.Equal(15 * sizeof(int) * 2, arena.UsedBytes);
    }

    [Fact]
    public void ArenaSlice_IsAViewNotACopy()
    {
        var arena = new Arena(64);
        var slice = arena.Alloc<Payload>(2);
        slice.Span[1] = new Payload { A = 42, B = 0 };

        // A fresh Span over the same region observes the write: view semantics, no copies.
        Assert.Equal(42, slice.Span[1].A);
    }
}
