// §63: snapshot+hash micro-benchmark. Target <0.5 ms (§33) is a goal, not a fact:
// this test records the measured value and guards against gross regression (<2 ms avg).
using System.Diagnostics;
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class SnapshotBenchmarkTests
{
    private readonly ITestOutputHelper _out;
    public SnapshotBenchmarkTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void CapturePlusHash_AverageUnder2ms()
    {
        var sim = new DeterministicSim(1234);
        for (int f = 0; f < 100; f++)
            sim.Step(InputGen.P1(f), InputGen.P2(f));

        const int iters = 10_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
            _ = sim.Capture().Hash;
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iters;
        _out.WriteLine($"Capture+Hash avg: {avgMs:F4} ms over {iters} iters " +
                       $"(payload {sim.Capture().Payload.Length}B, {Environment.OSVersion}, {Environment.ProcessorCount} cores)");
        Assert.True(avgMs < 2.0, $"Snapshot too slow: {avgMs:F4} ms avg.");
    }
}
