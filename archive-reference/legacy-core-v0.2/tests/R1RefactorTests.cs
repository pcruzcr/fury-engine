// R1 tests: observability (Observer trace), robustness (truncation/caps), measurement.
using Fighter.Core;
using Xunit.Abstractions;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

/// <summary>In-memory observer: exact event/frame trace for auditability tests.</summary>
internal sealed class RecordingObserver : IFightObserver
{
    public readonly System.Collections.Generic.List<int> Frames = new();
    public readonly System.Collections.Generic.List<SimEvent> Events = new();
    public void OnFrame(FightSimulation sim, int frame) => Frames.Add(frame);
    public void OnSimEvent(FightSimulation sim, SimEvent e) => Events.Add(e);
}

public sealed class ObserverTests
{
    [Fact]
    public void Observer_SeesEveryFrameExactlyOnce_InOrder()
    {
        var s = New(11);
        var rec = new RecordingObserver();
        s.Observer = rec;
        for (int f = 0; f < 200; f++) FightDeterminismTests.Script(s, f);
        Assert.Equal(200, rec.Frames.Count);
        for (int k = 0; k < 200; k++) Assert.Equal(k, rec.Frames[k]);
    }

    [Fact]
    public void Observer_EventTrace_MatchesLastSimEventsSemantics()
    {
        var s = New(12);
        WalkIntoRange(s);
        var rec = new RecordingObserver();
        s.Observer = rec;
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 30; k++) Neutrals(s, 1);
        Assert.Contains(rec.Events, e => e.Type == SimEventType.Hit && e.A == 0 && e.Value == 28);
        // Every observed event belongs to a frame the observer also saw.
        foreach (var e in rec.Events)
            Assert.Contains(e.Frame, rec.Frames);
    }

    [Fact]
    public void NullObserver_ZeroBehavioralDifference()
    {
        static ulong Run(bool observe)
        {
            var s = New(13);
            RecordingObserver? rec = observe ? new RecordingObserver() : null;
            s.Observer = rec;
            for (int f = 0; f < 500; f++) FightDeterminismTests.Script(s, f);
            return s.Capture().Hash;
        }
        Assert.Equal(Run(false), Run(true)); // observing never perturbs simulation
    }
}

public sealed class RobustnessTests
{
    [Fact]
    public void TruncatedSnapshot_ThrowsInvalidOperation_NotIndexOutOfRange()
    {
        var s = New(21);
        for (int f = 0; f < 50; f++) FightDeterminismTests.Script(s, f);
        byte[] full = s.Capture().Payload;
        foreach (int len in new[] { 0, 1, 7, full.Length / 2, full.Length - 1 })
        {
            byte[] cut = new byte[len];
            System.Array.Copy(full, cut, len);
            var ex = Record.Exception(() =>
            {
                var r = new FightSimulation(99, Glen, Glen);
                r.Restore(new FightSnapshot(cut, bypassValidation: true));
            });
            Assert.NotNull(ex);
            Assert.IsNotType<System.IndexOutOfRangeException>(ex);
            Assert.IsType<System.InvalidOperationException>(ex);
        }
    }

    [Fact]
    public void AbsurdHistoryCount_RejectedBeforeAllocation()
    {
        // Walk a valid payload with the real codec until the first history count
        // (offset derived from the codec itself — no hardcoded layout), splice
        // int.MaxValue there, and assert Restore rejects it before any allocation loop.
        var s = New(22);
        for (int f = 0; f < 10; f++) FightDeterminismTests.Script(s, f);
        byte[] full = s.Capture().Payload;
        var probe = new SnapshotReader(full);
        _ = probe.ReadInt(); // schema
        for (int k = 0; k < 5; k++) _ = probe.ReadInt(); // frame/round/timer/over/winner
        _ = probe.ReadInt(); // stage id (schema ≥ 4)
        _ = probe.ReadULong(); // rng
        for (int i = 0; i < 2; i++)
            for (int k = 0; k < FightSnapshot.FighterBlockInts; k++) _ = probe.ReadInt();
        // Self-check: we must be standing on a sane history count (≤ ring capacity).
        // If the codec layout ever drifts, this fails LOUDLY instead of testing garbage.
        int sane = probe.ReadHistoryCount();
        Assert.InRange(sane, 0, SimulationUnits.RingCapacity);
        int countOffset = probe.Position - 4;
        byte[] hostile = (byte[])full.Clone();
        hostile[countOffset] = 0xFF; hostile[countOffset + 1] = 0xFF;
        hostile[countOffset + 2] = 0xFF; hostile[countOffset + 3] = 0x7F; // int.MaxValue LE
        var ex = Record.Exception(() =>
        {
            var r = new FightSimulation(99, Glen, Glen);
            r.Restore(new FightSnapshot(hostile, bypassValidation: true));
        });
        Assert.IsType<System.InvalidOperationException>(ex);
    }

    [Fact]
    public void OversizedPayload_RejectedByCap()
    {
        byte[] big = new byte[SnapshotLimits.MaxSnapshotBytes + 1];
        var ex = Record.Exception(() => SnapshotReader.CheckLength(big));
        Assert.IsType<System.InvalidOperationException>(ex);
    }

    [Fact]
    public void Validator_RejectsBadFighter()
    {
        var bad = new FighterData { Name = "", MaxHp = 0 };
        Assert.Throws<System.InvalidOperationException>(() => DataValidator.ValidateFighter(bad));
        var badMap = GlenData.Build();
        badMap.DoubleQcfMove = 424242;
        Assert.Throws<System.InvalidOperationException>(() => DataValidator.ValidateFighter(badMap));
    }
}

public sealed class MeasurementTests
{
    private readonly ITestOutputHelper _out;
    public MeasurementTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void StepThroughput_5kScriptedSteps_Measured()
    {
        var s = New(31);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 0; f < 5000; f++) FightDeterminismTests.Script(s, f);
        sw.Stop();
        double stepsPerSec = 5000 / sw.Elapsed.TotalSeconds;
        double avgUs = sw.Elapsed.TotalMicroseconds / 5000;
        _out.WriteLine($"FightSimulation.Step: {stepsPerSec:F0} steps/s, {avgUs:F2} us/step (budget @60Hz: 16666 us).");
        Assert.True(avgUs < 1000, $"Step too slow: {avgUs:F2} us avg (budget 1000 us guard).");
    }

    [Fact]
    public void Fuzz_RestoreAtMultiplePoints_ResimulatesEqual()
    {
        const ulong seed = 0xF422;
        var full = New(seed);
        var snaps = new System.Collections.Generic.Dictionary<int, ulong>();
        for (int f = 0; f <= 600; f++)
        {
            if (f is 100 or 250 or 400 or 550) snaps[f] = full.Capture().Hash;
            if (f < 600) FightDeterminismTests.Script(full, f);
        }
        ulong expected = full.Capture().Hash;
        foreach (var (at, hash) in snaps)
        {
            var branch = New(seed);
            for (int f = 0; f < at; f++) FightDeterminismTests.Script(branch, f);
            Assert.Equal(hash, branch.Capture().Hash);
            var snap = branch.Capture();
            var resumed = New(999);
            resumed.Restore(snap);
            for (int f = at; f < 600; f++) FightDeterminismTests.Script(resumed, f);
            Assert.Equal(expected, resumed.Capture().Hash);
        }
    }
}
