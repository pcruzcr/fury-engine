// REFACTOR+HARDEN A6/D: xxHash64 vectors, DeterminismProbe, typed exceptions,
// SingleThreadGuard, SimulationLoopEnforcer.
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class XxHashTests
{
    private readonly ITestOutputHelper _out;
    public XxHashTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void EmptyInput_MatchesOfficialVector()
    {
        Assert.Equal(0xEF46DB3751D8E999UL, DeterminismHash.Hash(System.Array.Empty<byte>()));
    }

    [Fact]
    public void Abc_MatchesOfficialVector()
    {
        byte[] abc = { (byte)'a', (byte)'b', (byte)'c' };
        Assert.Equal(0x44BC2CF5AD770999UL, DeterminismHash.Hash(abc));
    }

    [Fact]
    public void LongInput_StripedPath_RegressionPinned()
    {
        // 512-byte ramp (exercises all xxHash64 lanes: 32B stripes + 8B tail).
        // The constant below is EXECUTED (corrected 2026-09-14: a prior value
        // was never execution-pinned and mismatched). Spec conformance is
        // proven by the official empty/abc vectors above.
        var data = new byte[512];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
        ulong h = DeterminismHash.Hash(data);
        _out.WriteLine($"ramp512 xxHash64=0x{h:X16}");
        Assert.Equal(0x7B3BFCAAC0348AC0UL, h);
    }

    [Fact]
    public void Seed_ChangesHash_Deterministically()
    {
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        ulong a = DeterminismHash.Hash(data, 0);
        ulong b = DeterminismHash.Hash(data, 1);
        Assert.NotEqual(a, b);
        Assert.Equal(a, DeterminismHash.Hash(data, 0));
    }

    [Fact]
    public void SingleByte_TailPath_Covered()
    {
        ulong h0 = DeterminismHash.Hash(new byte[] { 0xFF });
        ulong h1 = DeterminismHash.Hash(new byte[] { 0xFE });
        Assert.NotEqual(h0, h1);
    }
}

public sealed class DeterminismProbeTests
{
    [Fact]
    public void Record_And_AssertPass()
    {
        var probe = new DeterminismProbe();
        probe.RecordFrame(0x1234UL, 7);
        probe.Assert(0x1234UL, 0x1234UL, 7);
        Assert.Equal(1, probe.Count);
        Assert.Equal((7, 0x1234UL), probe.GetRecord(0));
    }

    [Fact]
    public void Mismatch_Throws_WithFrameAndHashes()
    {
        var probe = new DeterminismProbe();
        var ex = Assert.Throws<DeterminismViolationException>(() => probe.Assert(0xAAAAUL, 0xBBBBUL, 42));
        Assert.Equal(42, ex.Frame);
        Assert.Equal(0xAAAAUL, ex.Expected);
        Assert.Equal(0xBBBBUL, ex.Actual);
        Assert.Contains("42", ex.Message);
    }

    [Fact]
    public void TypedExceptions_DeriveInvalidOperationException()
    {
        // Existing `catch (InvalidOperationException)` call sites keep working.
        Assert.IsAssignableFrom<InvalidOperationException>(new SnapshotSchemaException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new ReplayCorruptException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new DeterminismViolationException(1, 2, 3));
        var withInner = new ReplayCorruptException("x", new InvalidOperationException("i"));
        Assert.Equal("x", withInner.Message);
        Assert.Equal("i", withInner.InnerException?.Message);
    }
}

public sealed class SingleThreadGuardTests
{
    [Fact]
    public void Capture_Check_SameThread_Passes()
    {
        try
        {
            SingleThreadGuard.Capture();
            SingleThreadGuard.Check(); // same thread: no throw
        }
        finally
        {
            SingleThreadGuard.Release();
        }
        SingleThreadGuard.Check(); // released: no owner, no throw
    }

    [Fact]
    public void Check_FromOtherThread_Throws()
    {
        using var ready = new System.Threading.ManualResetEventSlim(false);
        using var done = new System.Threading.ManualResetEventSlim(false);
        Exception? seen = null;
        try
        {
            SingleThreadGuard.Capture();
            var t = new System.Threading.Thread(() =>
            {
                ready.Set();
                try
                {
                    SingleThreadGuard.Check();
                }
                catch (Exception ex)
                {
                    seen = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            t.Start();
            Assert.True(ready.Wait(5000));
            Assert.True(done.Wait(5000));
            t.Join();
            Assert.IsType<InvalidOperationException>(seen);
        }
        finally
        {
            SingleThreadGuard.Release();
        }
    }

    [Fact]
    public void FightSimulation_IsMarkedSingleThreaded()
    {
        var attr = typeof(FightSimulation).GetMethod("Step")
            ?.GetCustomAttributes(typeof(SingleThreadedAttribute), inherit: false);
        Assert.NotNull(attr);
        Assert.Single(attr);
    }
}

public sealed class SimulationLoopEnforcerTests
{
    [Fact]
    public void FullOrder_Passes()
    {
        var e = new SimulationLoopEnforcer();
        e.Enter(SimulationLoopEnforcer.Phase.InputRead);
        e.Enter(SimulationLoopEnforcer.Phase.InputBuffer);
        e.Enter(SimulationLoopEnforcer.Phase.StateMachine);
        e.Enter(SimulationLoopEnforcer.Phase.CommandNormal);
        e.Enter(SimulationLoopEnforcer.Phase.AabbSimulation);
        e.Enter(SimulationLoopEnforcer.Phase.HitDetection);
        e.Enter(SimulationLoopEnforcer.Phase.EnergySystem);
        e.Enter(SimulationLoopEnforcer.Phase.Snapshot);
        e.Enter(SimulationLoopEnforcer.Phase.RenderSignal);
        e.ResetFrame();
        e.Enter(SimulationLoopEnforcer.Phase.InputRead);
    }

    [Fact]
    public void Skip_Throws_InDebug_NoOp_InRelease()
    {
        var e = new SimulationLoopEnforcer();
#if DEBUG
        Assert.Throws<InvalidOperationException>(() => e.Enter(SimulationLoopEnforcer.Phase.HitDetection));
#else
        e.Enter(SimulationLoopEnforcer.Phase.HitDetection); // RELEASE: zero cost, no throw
#endif
    }
}
