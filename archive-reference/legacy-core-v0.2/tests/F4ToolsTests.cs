// F4 tools tests: .frep roundtrip + frame-data export + fight determinism certificate v2.
using Fighter.Core;
using Xunit.Abstractions;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class ReplayFileTests
{
    [Fact]
    public void Frep_SaveLoad_ResimulatesIdentically()
    {
        const ulong seed = 0xF4E9;
        var live = new FightSimulation(seed, Glen, Kira);
        var frames = new System.Collections.Generic.List<(InputState, InputState)>();
        var hashes = new System.Collections.Generic.List<ulong>();
        for (int f = 0; f < 300; f++)
        {
            FightDeterminismTests.ScriptFighter(live.Frame, out var p1, out var p2);
            live.Step(p1, p2);
            frames.Add((p1, p2));
            hashes.Add(live.Capture().Hash);
        }
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"df-test-{seed:X}.frep");
        try
        {
            FightReplayFile.Save(path, seed, frames);
            var (s2, q1, q2) = FightReplayFile.Load(path);
            Assert.Equal(seed, s2);
            Assert.Equal(300, q1.Length);
            var shadow = new FightSimulation(seed, Glen, Kira);
            for (int k = 0; k < q1.Length; k++)
            {
                shadow.Step(q1[k], q2[k]);
                Assert.Equal(hashes[k], shadow.Capture().Hash);
            }
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    [Fact]
    public void Frep_CorruptMagic_Throws()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "df-test-bad.frep");
        try
        {
            System.IO.File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Throws<ReplayCorruptException>(() => FightReplayFile.Load(path));
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}

public sealed class FrameDataExportTests
{
    private static int CountIds(string json)
    {
        int n = 0, i = 0;
        while ((i = json.IndexOf("\"id\":", i, System.StringComparison.Ordinal)) >= 0) { n++; i += 5; }
        return n;
    }

    [Fact]
    public void GlenExport_Has27Moves_WithExactLp()
    {
        string json = FrameDataExport.ToJson(Glen);
        Assert.Equal(27, CountIds(json));
        Assert.Contains("\"name\":\"Jab ombligo\"", json);
        Assert.Contains("\"startup\":4", json);
        Assert.Contains("\"damage\":28", json);
        Assert.Contains("\"chainTo\":[1,2]", json);
    }

    [Fact]
    public void KiraExport_MarksProvisional_WithGimmick()
    {
        string json = FrameDataExport.ToJson(Kira);
        Assert.Contains("KIRA-CAL", json);
        Assert.Contains("\"gimmick\":\"KiraAcceleration\"", json);
        Assert.Contains("\"maxHp\":900", json);
        Assert.True(CountIds(json) > 15);
    }

    [Fact]
    public void Export_IsByteStable()
    {
        Assert.Equal(FrameDataExport.ToJson(Glen), FrameDataExport.ToJson(GlenData.Build()));
    }
}

public sealed class FightCertificateTests
{
    private readonly ITestOutputHelper _out;
    public FightCertificateTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void DeterminismCertificate_Fight_10k_GlenVsKira()
    {
        const ulong seed = 0xDF2019;
        const int frames = 10_000;
        static ulong Run(ulong seed, int frames)
        {
            var s = new FightSimulation(seed, FightKit.Glen, FightKit.Kira);
            for (int f = 0; f < frames; f++)
            {
                FightDeterminismTests.ScriptFighter(f, out var p1, out var p2);
                s.Step(p1, p2);
            }
            return s.Capture().Hash;
        }
        ulong a = Run(seed, frames);
        ulong b = Run(seed, frames);

        var mid = new FightSimulation(seed, FightKit.Glen, FightKit.Kira);
        for (int f = 0; f < 5000; f++)
        {
            FightDeterminismTests.ScriptFighter(f, out var p1, out var p2);
            mid.Step(p1, p2);
        }
        var snap = mid.Capture();
        var resumed = new FightSimulation(0, FightKit.Glen, FightKit.Kira);
        resumed.Restore(snap);
        for (int f = 5000; f < frames; f++)
        {
            FightDeterminismTests.ScriptFighter(f, out var p1, out var p2);
            resumed.Step(p1, p2);
        }
        ulong c = resumed.Capture().Hash;

        _out.WriteLine("DUAL FURY FIGHT DETERMINISM CERTIFICATE v2");
        _out.WriteLine($"Godot: 4.7.2-stable.mono ed1daf0bf (F2b headless PASS separately)");
        _out.WriteLine($".NET: 9 / net9.0 · Snapshot schema: {FightSnapshot.Schema}");
        _out.WriteLine($"Matchup: Glen vs Kira · Seed: 0x{seed:X} · Frames: {frames}");
        _out.WriteLine($"Run A: 0x{a:X16} · Run B: 0x{b:X16} · Determinism: {(a == b ? "PASS" : "FAIL")}");
        _out.WriteLine($"Snapshot@5000: {snap.Payload.Length}B · Restore+Resim: {(a == c ? "PASS" : "FAIL")} (0x{c:X16})");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }
}
