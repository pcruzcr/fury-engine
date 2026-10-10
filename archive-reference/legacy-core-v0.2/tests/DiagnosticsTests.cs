// REFACTOR+HARDEN: diagnostics contract tests (typed exceptions, probe, enforcer).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void TypedExceptions_AreInvalidOperationExceptions()
    {
        // Backwards compatibility: existing catch (InvalidOperationException) sites keep working.
        Assert.IsAssignableFrom<InvalidOperationException>(new SnapshotSchemaException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new ReplayCorruptException("x"));
        Assert.IsAssignableFrom<InvalidOperationException>(new DeterminismViolationException(3, 1, 2));
    }

    [Fact]
    public void DeterminismViolation_CarriesFrameAndHashes()
    {
        var ex = new DeterminismViolationException(77, 0xAAAABBBBCCCCDDDDUL, 0x1111222233334444UL);
        Assert.Equal(77, ex.Frame);
        Assert.Equal(0xAAAABBBBCCCCDDDDUL, ex.Expected);
        Assert.Equal(0x1111222233334444UL, ex.Actual);
        Assert.Contains("77", ex.Message);
    }

    [Fact]
    public void Probe_Assert_PassesOnEqual_ThrowsOnMismatch()
    {
        var probe = new DeterminismProbe();
        probe.RecordFrame(0x1234UL, 10);
        probe.Assert(0x1234UL, 0x1234UL, 10);
        var ex = Assert.Throws<DeterminismViolationException>(() => probe.Assert(0x1234UL, 0x5678UL, 10));
        Assert.Equal(10, ex.Frame);
        Assert.Equal(1, probe.Count);
        Assert.Equal((10, 0x1234UL), probe.GetRecord(0));
    }

    [Fact]
    public void SchemaMismatch_ThrowsTyped()
    {
        var sim = new DeterministicSim(1);
        var snap = sim.Capture();
        // Corrupt the payload schema id in place (little-endian first int).
        snap.Payload[0] = 0xFF; snap.Payload[1] = 0xFF; snap.Payload[2] = 0xFF; snap.Payload[3] = 0xFF;
        Assert.Throws<SnapshotSchemaException>(() => sim.Restore(snap));
    }

    [Fact]
    public void ReplayCorrupt_ThrowsTyped_Substrate()
    {
        var rec = new ReplayRecorder(1);
        rec.Record(new InputState(Buttons.None, 0, 0, 0), new InputState(Buttons.None, 0, 0, 0));
        var bytes = rec.ToBytes();
        bytes[0] ^= 0xFF; // corrupt magic
        Assert.Throws<ReplayCorruptException>(() => ReplayRecorder.Load(bytes));
    }

    [Fact]
    public void ReplayCorrupt_ThrowsTyped_FightFile()
    {
        var frames = new System.Collections.Generic.List<(InputState P1, InputState P2)>
            { (new InputState(Buttons.None, 0, 0, 0), new InputState(Buttons.None, 0, 0, 0)) };
        var w = new SnapshotWriter();
        w.Write(unchecked((int)0x44464652)); w.Write(FightReplayFile.FormatVersion);
        w.Write(1UL); w.Write(frames.Count);
        foreach (var (p1, p2) in frames) { w.Write(p1); w.Write(p2); }
        var bytes = w.ToArray();
        bytes[16] ^= 0xFF; // corrupt exactly one COUNT byte (1 -> 254 frames claimed,
        // buffer holds 1): the loader must fail loudly on truncation, never misread.
        Assert.Throws<ReplayCorruptException>(() => FightReplayFile.LoadBytes(bytes));
    }

    [Fact]
    public void Enforcer_AcceptsOfficialOrder_RejectsSkips()
    {
        var e = new SimulationLoopEnforcer();
        e.Enter(SimulationLoopEnforcer.Phase.InputRead);
        e.Enter(SimulationLoopEnforcer.Phase.InputBuffer);
#if DEBUG
        Assert.Throws<InvalidOperationException>(() => e.Enter(SimulationLoopEnforcer.Phase.HitDetection));
#else
        e.Enter(SimulationLoopEnforcer.Phase.HitDetection); // RELEASE: zero-cost no-op
#endif
        e.ResetFrame();
        e.Enter(SimulationLoopEnforcer.Phase.InputRead);
    }

    [Fact]
    public void SingleThreaded_MarksSimulationTypes()
    {
        // Contract marker exists and is applicable to the intended targets.
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(SingleThreadedAttribute), typeof(AttributeUsageAttribute))!;
        Assert.True((usage.ValidOn & AttributeTargets.Class) != 0);
    }
}
