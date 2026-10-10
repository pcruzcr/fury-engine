// REFACTOR+HARDEN A3: RingBuffer extras (TryPeekAt / CopyTo(Span) / FrameCount).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class RingExtrasTests
{
    [Fact]
    public void FrameCount_MirrorsCount()
    {
        var rb = new RingBuffer<int>(8);
        Assert.Equal(0, rb.FrameCount);
        rb.Push(1); rb.Push(2);
        Assert.Equal(2, rb.FrameCount);
        Assert.Equal(SimulationUnits.RingCapacity, new RingBuffer<int>(SimulationUnits.RingCapacity).Capacity);
    }

    [Fact]
    public void TryPeekAt_HitAndMiss()
    {
        var rb = new RingBuffer<int>(4);
        Assert.False(rb.TryPeekAt(0, out int empty));
        Assert.Equal(0, empty);
        rb.Push(10); rb.Push(20); rb.Push(30);
        Assert.True(rb.TryPeekAt(0, out int latest));
        Assert.Equal(30, latest);
        Assert.True(rb.TryPeekAt(2, out int oldest));
        Assert.Equal(10, oldest);
        Assert.False(rb.TryPeekAt(3, out _));
        Assert.False(rb.TryPeekAt(-1, out _));
    }

    [Fact]
    public void TryPeekAt_WrappedBuffer_CorrectAfterOverflow()
    {
        var rb = new RingBuffer<int>(4);
        for (int i = 0; i < 10; i++) rb.Push(i); // head moved twice around
        Assert.True(rb.TryPeekAt(0, out int latest));
        Assert.Equal(9, latest);
        Assert.True(rb.TryPeekAt(3, out int oldest));
        Assert.Equal(6, oldest);
        Assert.False(rb.TryPeekAt(4, out _));
    }

    [Fact]
    public void CopyToSpan_OldestFirst_Exact()
    {
        var rb = new RingBuffer<int>(4);
        rb.Push(1); rb.Push(2); rb.Push(3);
        Span<int> dst = stackalloc int[3];
        rb.CopyTo(dst);
        Assert.Equal(new[] { 1, 2, 3 }, dst.ToArray());
    }

    [Fact]
    public void CopyToSpan_WrappedBuffer_OrderPreserved()
    {
        var rb = new RingBuffer<int>(3);
        for (int i = 0; i < 7; i++) rb.Push(i);
        Span<int> dst = stackalloc int[3];
        rb.CopyTo(dst);
        Assert.Equal(new[] { 4, 5, 6 }, dst.ToArray());
    }

    [Fact]
    public void CopyToSpan_TooShort_Throws()
    {
        var rb = new RingBuffer<int>(4);
        rb.Push(1); rb.Push(2);
        var dst = new int[1];
        Assert.Throws<ArgumentException>(() => rb.CopyTo(dst));
    }

    [Fact]
    public void CopyToSpan_Empty_NoThrow()
    {
        var rb = new RingBuffer<int>(4);
        Span<int> dst = stackalloc int[4];
        rb.CopyTo(dst); // Count 0: fills nothing, throws nothing
    }
}
