// REFACTOR+HARDEN A3: RingBuffer contract tests (Capacity = SimulationUnits.RingCapacity).
// Covers: empty, one, full, wrap-around, overwrite, index 0, max valid index,
// invalid index, post-wrap read, CopyTo ok/small, TryPeekAt, FrameCount, Push.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class RingBufferContractTests
{
    [Fact]
    public void Empty_CountZero_FrameCountZero()
    {
        var r = new RingBuffer<int>(SimulationUnits.RingCapacity);
        Assert.Equal(0, r.Count);
        Assert.Equal(0, r.FrameCount);
        Assert.Equal(SimulationUnits.RingCapacity, r.Capacity);
    }

    [Fact]
    public void Empty_TryPeekAt_False()
    {
        var r = new RingBuffer<int>(SimulationUnits.RingCapacity);
        Assert.False(r.TryPeekAt(0, out int v));
        Assert.Equal(0, v);
        Assert.False(r.TryPeekAt(-1, out _));
    }

    [Fact]
    public void Empty_GetLatest_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(8).GetLatest(0));

    [Fact]
    public void OneElement_IndexZeroReadsIt()
    {
        var r = new RingBuffer<int>(8);
        r.Push(42);
        Assert.Equal(1, r.Count);
        Assert.Equal(42, r.GetLatest(0));
        Assert.True(r.TryPeekAt(0, out int v));
        Assert.Equal(42, v);
        Assert.Equal(42, r.PeekOldest(0));
    }

    [Fact]
    public void Full_NoWrap_PreservesOrder()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 1; i <= 8; i++) r.Push(i);
        Assert.Equal(8, r.Count);
        Assert.Equal(1, r.PeekOldest(0)); // index 0 = oldest
        Assert.Equal(8, r.PeekOldest(7)); // max valid index
        Assert.Equal(8, r.GetLatest(0));  // latest
        Assert.Equal(1, r.GetLatest(7));  // oldest via latest-offset
    }

    [Fact]
    public void Overwrite_DropsOldest()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 1; i <= 9; i++) r.Push(i); // 9 pushes into capacity 8
        Assert.Equal(8, r.Count);
        Assert.Equal(2, r.PeekOldest(0));
        Assert.Equal(9, r.GetLatest(0));
    }

    [Fact]
    public void WrapAround_MultipleCycles_OrderStable()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 100; i++) r.Push(i);
        Assert.Equal(8, r.Count);
        for (int k = 0; k < 8; k++)
        {
            Assert.Equal(92 + k, r.PeekOldest(k));
            Assert.True(r.TryPeekAt(7 - k, out int v));
            Assert.Equal(92 + k, v);
        }
    }

    [Fact]
    public void InvalidIndex_Throws()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 8; i++) r.Push(i);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetLatest(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetLatest(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.PeekOldest(8));
        Assert.False(r.TryPeekAt(8, out _));
        Assert.False(r.TryPeekAt(-2, out _));
    }

    [Fact]
    public void CopyTo_SufficientBuffer_OldestFirst()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 5; i++) r.Push(10 + i);
        Span<int> dst = stackalloc int[8];
        r.CopyTo(dst);
        for (int i = 0; i < 5; i++)
            Assert.Equal(10 + i, dst[i]);
    }

    [Fact]
    public void CopyTo_InsufficientBuffer_ThrowsPerContract()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 5; i++) r.Push(i);
        Assert.Throws<ArgumentException>(() => r.CopyTo(stackalloc int[4]));
    }

    [Fact]
    public void CopyOldestFirst_ArrayContract_Preserved()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 3; i++) r.Push(i);
        var dst = new int[3];
        r.CopyOldestFirst(dst);
        Assert.Equal(new[] { 0, 1, 2 }, dst);
        Assert.Throws<ArgumentException>(() => r.CopyOldestFirst(new int[2]));
    }

    [Fact]
    public void Clear_ResetsToEmpty()
    {
        var r = new RingBuffer<int>(8);
        for (int i = 0; i < 8; i++) r.Push(i);
        r.Clear();
        Assert.Equal(0, r.Count);
        Assert.False(r.TryPeekAt(0, out _));
        r.Push(7);
        Assert.Equal(7, r.GetLatest(0));
    }

    [Fact]
    public void NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RingBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RingBuffer<int>(-1));
    }
}
