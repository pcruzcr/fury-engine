// Primitive unit tests: RNG, ring buffer, fixed math, clock.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class InputStateLayoutTests
{
    [Fact]
    public void InputState_Is16Bytes_RollbackBandwidthPinned()
    {
        // Buttons byte + 3 pad + DirX/DirY/Frame int32 = 16 B. Rollback worst case:
        // 16 B × 8 history × 2 fighters = 256 B/frame — trivial. The 8 B historical
        // target would require packing frame deltas; DEFERRED (see DF-CON-007).
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<InputState>());
    }
}

public sealed class RngTests
{
    [Fact]
    public void SameSeed_SameSequence()
    {
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Restore_ReproducesStream()
    {
        var rng = new DeterministicRng(7);
        _ = rng.NextULong();
        ulong mid = rng.State;
        ulong next = rng.NextULong();
        rng.Restore(mid);
        Assert.Equal(next, rng.NextULong());
    }

    [Fact]
    public void NextBounded_WithinRange()
    {
        var rng = new DeterministicRng(99);
        for (int i = 0; i < 1000; i++)
            Assert.True(rng.NextBounded(8) < 8);
    }
}

public sealed class RingBufferTests
{
    [Fact]
    public void Capacity8_Push9_DropsOldest()
    {
        var rb = new RingBuffer<int>(SimulationUnits.RingCapacity);
        for (int i = 0; i < 9; i++) rb.Push(i);
        Assert.Equal(8, rb.Count);
        Assert.Equal(8, rb.GetLatest(0));
        Assert.Equal(1, rb.GetLatest(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => rb.GetLatest(8));
    }

    [Fact]
    public void CopyOldestFirst_PreservesOrder()
    {
        var rb = new RingBuffer<int>(4);
        rb.Push(10); rb.Push(20); rb.Push(30);
        var dst = new int[3];
        rb.CopyOldestFirst(dst);
        Assert.Equal(new[] { 10, 20, 30 }, dst);
    }
}

public sealed class FixedMathTests
{
    [Fact]
    public void Mul_OneAndHalfTimesTwo_IsThree()
    {
        var a = Fixed.FromInt(1) + Fixed.FromRaw(Fixed.OneRaw / 2); // 1.5
        Assert.Equal(Fixed.FromInt(3), a * Fixed.FromInt(2));
    }

    [Fact]
    public void Div_ThreeByTwo_IsOneAndHalf()
    {
        Assert.Equal(Fixed.FromRaw(Fixed.OneRaw + Fixed.OneRaw / 2),
            Fixed.FromInt(3) / Fixed.FromInt(2));
    }
}

public sealed class ClockTests
{
    [Fact]
    public void OneSecond_Yields60Steps()
    {
        var clock = new SimulationClock();
        Assert.Equal(60, clock.Advance(1_000_000));
        Assert.Equal(60, clock.Frame);
    }

    [Fact]
    public void Remainder_IsPreserved()
    {
        var clock = new SimulationClock();
        Assert.Equal(1, clock.Advance(20_000)); // 1.2 frames -> 1 step
        Assert.Equal(1, clock.Advance(13_334)); // remainder pushes second step
        Assert.Equal(2, clock.Frame);
    }

    [Fact]
    public void Negative_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock().Advance(-1));
}
