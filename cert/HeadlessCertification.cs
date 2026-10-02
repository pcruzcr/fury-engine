using System.Diagnostics;
using System.Globalization;
using System.Text;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Net;
using Fury.Core.Replay;
using Fury.Core.Sim;

namespace Fury.Certification;

/// <summary>
/// F5 gate (ADR-006): headless certification for CI and release builds. Four independent checks, any failure
/// returns a non-zero exit code:
///  1. DETERMINISM: seeded AI-vs-AI matches hash identically on every run AND match the hashes committed in
///     <c>expected_hashes.txt</c>, so a platform / compiler / library difference is caught (a run-twice comparison
///     inside one process cannot see that).
///  2. ROLLBACK: two real netplay peers over a jittery, lossy, reordering link converge to the offline hashes.
///  3. REPLAY: record, serialize, parse and verify by resimulation.
///  4. SNAPSHOTS: restoring at a random frame reproduces the exact same future.
/// Usage: <c>dotnet run --project cert -- [outputDir] [--record]</c> (<c>--record</c> rewrites the expected hashes).
/// </summary>
public static class HeadlessCertification
{
    private const int Frames = 900;
    private const int Matches = 6;

    public static int Main(string[] args)
    {
        string outputDir = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "certification_results";
        bool record = args.Contains("--record");
        Directory.CreateDirectory(outputDir);

        var report = new StringBuilder();
        report.AppendLine("Fury Engine Certification Report");
        report.AppendLine("================================");
        report.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.UtcNow:O}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Runtime: {Environment.Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Snapshot words: {FightState.WordCount}");
        report.AppendLine();

        bool ok = true;
        ok &= Check("Determinism vs committed hashes", DeterminismAgainstExpected(record, report), report);
        ok &= Check("Rollback convergence (2 peers, jitter + loss)", RollbackConverges(report), report);
        ok &= Check("Replay record / parse / verify", ReplayRoundTrip(report), report);
        ok &= Check("Snapshot restore reproduces the future", SnapshotRestore(report), report);
        PerformanceSummary(report);

        report.AppendLine();
        report.AppendLine(ok ? "CERTIFICATION PASSED" : "CERTIFICATION FAILED");
        string path = Path.Combine(outputDir, "certification_report.txt");
        File.WriteAllText(path, report.ToString());

        Console.WriteLine(report.ToString());
        Console.WriteLine($"Report saved to: {path}");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, bool passed, StringBuilder report)
    {
        string line = $"[{(passed ? "PASS" : "FAIL")}] {name}";
        report.AppendLine(line);
        return passed;
    }

    private static string ExpectedPath()
    {
        // Works from the repo root, from the cert folder and from bin output (walks up to find the file).
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.Combine(dir, "cert", "expected_hashes.txt");
            if (File.Exists(candidate) || File.Exists(Path.Combine(dir, "FuryEngine.sln"))) return candidate;
            string? parent = Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "cert", "expected_hashes.txt");
    }

    // ------------------------------------------------------------------ 1. determinism

    private static ulong RunBlindMatch(int seed)
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var a = new CpuBrain((ulong)((seed * 1000) + 1), aggression: 8);
        var b = new CpuBrain((ulong)((seed * 1000) + 2), aggression: 8);
        ulong chain = 0;
        for (int f = 0; f < Frames; f++)
        {
            sim.StepWithProviders(a, b);
            chain = FpHash.Combine(chain, sim.SnapshotHash); // hash chain over EVERY frame, not just the last
        }
        return chain;
    }

    private static bool DeterminismAgainstExpected(bool record, StringBuilder report)
    {
        var hashes = new ulong[Matches];
        bool ok = true;
        for (int m = 0; m < Matches; m++)
        {
            hashes[m] = RunBlindMatch(m);
            if (RunBlindMatch(m) != hashes[m])
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  match {m}: NOT reproducible within one process");
                ok = false;
            }
        }

        string path = ExpectedPath();
        if (record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, hashes.Select(h => h.ToString("x16", CultureInfo.InvariantCulture)));
            report.AppendLine($"  recorded {Matches} hashes to {path}");
            return ok;
        }

        if (!File.Exists(path))
        {
            report.AppendLine($"  expected hashes missing: {path} (run with --record once and commit the file)");
            return false;
        }

        var expected = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => Convert.ToUInt64(l.Trim(), 16)).ToArray();
        if (expected.Length != Matches)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  expected {Matches} hashes, file has {expected.Length}");
            return false;
        }

        for (int m = 0; m < Matches; m++)
        {
            bool same = hashes[m] == expected[m];
            report.AppendLine(CultureInfo.InvariantCulture, $"  match {m}: {hashes[m]:x16} {(same ? "==" : "!=")} expected {expected[m]:x16}");
            ok &= same;
        }
        return ok;
    }

    // ------------------------------------------------------------------ 2. rollback

    private static bool RollbackConverges(StringBuilder report)
    {
        const int frames = 600;
        var rngA = new Xoshiro256StarStar(101);
        var rngB = new Xoshiro256StarStar(202);
        var s1 = new ushort[frames + 64];
        var s2 = new ushort[frames + 64];
        for (int i = 0; i < s1.Length; i++)
        {
            s1[i] = (ushort)(rngA.NextULong() & 0x0FFF);
            s2[i] = (ushort)(rngB.NextULong() & 0x0FFF);
        }

        const int delay = 2;
        ushort Applied(ushort[] s, int f) => f >= delay ? s[f - delay] : (ushort)0;

        var offline = new FightSimulation(GlenData.Create(), KiraData.Create());
        var expected = new ulong[frames];
        for (int f = 0; f < frames; f++)
        {
            offline.Step(new InputState(Applied(s1, f)), new InputState(Applied(s2, f)));
            expected[f] = offline.SnapshotHash;
        }

        var link = new LoopbackPair(seed: 7, maxDelayTicks: 5, dropRatePermyriad: 2000);
        var simA = new FightSimulation(GlenData.Create(), KiraData.Create());
        var simB = new FightSimulation(GlenData.Create(), KiraData.Create());
        var a = new NetplaySession(simA, link.A, f => new InputState(s1[f]), 0, delay);
        var b = new NetplaySession(simB, link.B, f => new InputState(s2[f]), 1, delay);

        for (int guard = 0; (a.Frame < frames || b.Frame < frames) && guard < 50_000; guard++)
        {
            if (a.Frame < frames) a.Tick(); else a.Pump();
            if (b.Frame < frames) b.Tick(); else b.Pump();
            link.Tick();
        }
        for (int i = 0; i < 5000 && (a.Runner.Session.ConfirmedThrough < frames - 1 || b.Runner.Session.ConfirmedThrough < frames - 1); i++)
        {
            a.Pump();
            b.Pump();
            link.Tick();
        }

        bool ok = a.Status != NetplayStatus.Desynced && b.Status != NetplayStatus.Desynced;
        for (int f = frames - 40; f < frames && ok; f++) ok = a.Runner.ConfirmedHash(f) == expected[f] && b.Runner.ConfirmedHash(f) == expected[f];
        report.AppendLine(CultureInfo.InvariantCulture, $"  rollbacks A/B: {simA.Stats.Rollbacks}/{simB.Stats.Rollbacks}, resimulated frames A/B: {simA.Stats.ResimulatedFrames}/{simB.Stats.ResimulatedFrames}");
        return ok;
    }

    // ------------------------------------------------------------------ 3. replay

    private static bool ReplayRoundTrip(StringBuilder report)
    {
        var cfg = FightConfig.Default();
        FightSimulation Make() => new(GlenData.Create(), KiraData.Create(), cfg);
        var sim = Make();
        var recorder = new ReplayRecorder(sim, ReplayHeader.ForSimulation(sim));
        var a = new CpuBrain(55, 8);
        var b = new CpuBrain(56, 8);
        for (int f = 0; f < Frames; f++)
        {
            var in1 = a.Poll(f, sim.ViewFor(0));
            var in2 = b.Poll(f, sim.ViewFor(1));
            recorder.Step(in1, in2);
        }

        byte[] bytes = recorder.Finish().ToBytes();
        var parsed = ReplayFile.Parse(bytes);
        var result = parsed.Verify(Make);
        report.AppendLine(CultureInfo.InvariantCulture, $"  {bytes.Length} bytes for {parsed.FrameCount} frames, {parsed.Checkpoints.Count} checkpoints");
        return result.Valid;
    }

    // ------------------------------------------------------------------ 4. snapshots

    private static bool SnapshotRestore(StringBuilder report)
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var a = new CpuBrain(77, 9);
        var b = new CpuBrain(78, 9);
        for (int f = 0; f < 400; f++) sim.StepWithProviders(a, b);

        int[] saved = sim.SnapshotSpan.ToArray();
        ulong atSave = sim.SnapshotHash;
        var inputs = new InputState[2 * 300];
        var rng = new Xoshiro256StarStar(9);
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputState((ushort)(rng.NextULong() & 0x0FFF));

        for (int f = 0; f < 300; f++) sim.Step(inputs[2 * f], inputs[(2 * f) + 1]);
        ulong future = sim.SnapshotHash;

        sim.Restore(saved);
        bool same = sim.SnapshotHash == atSave;
        for (int f = 0; f < 300; f++) sim.Step(inputs[2 * f], inputs[(2 * f) + 1]);
        same &= sim.SnapshotHash == future;
        report.AppendLine(CultureInfo.InvariantCulture, $"  restored at frame 400, replayed 300 frames: {(same ? "identical" : "DIVERGED")}");
        return same;
    }

    // ------------------------------------------------------------------ perf (informational)

    private static void PerformanceSummary(StringBuilder report)
    {
        var sim = new FightSimulation(GlenData.Create(), KiraData.Create());
        var a = new CpuBrain(1, 8);
        var b = new CpuBrain(2, 8);
        for (int f = 0; f < 300; f++) sim.StepWithProviders(a, b); // warm-up (JIT)

        const int measured = 3000;
        var sw = Stopwatch.StartNew();
        for (int f = 0; f < measured; f++)
        {
            sim.StepWithProviders(a, b);
            if (sim.State.RoundState != 0)
            {
                sim.State.SetHealth(0, 1000);
                sim.State.SetHealth(1, 1000);
            }
        }
        sw.Stop();
        double micros = sw.Elapsed.TotalMilliseconds * 1000.0 / measured;
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Simulation cost: {micros:F1} us per frame ({micros / 16667.0 * 100.0:F2}% of a 60 Hz budget)");
    }
}
