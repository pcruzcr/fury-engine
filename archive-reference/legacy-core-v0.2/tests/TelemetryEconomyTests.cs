// Telemetry (§38) + economy simulator (§18/§32) as DEV INSTRUMENTS (tests assembly).
// Telemetry piggybacks IFightObserver: zero sim impact, CSV-exportable, byte-reproducible.
using Fighter.Core;
using Xunit.Abstractions;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

/// <summary>Records one CSV row per sim event with full resource state.</summary>
internal sealed class TelemetryObserver : IFightObserver
{
    public readonly string MatchId;
    public readonly ulong Seed;
    public readonly System.Collections.Generic.List<string> Rows = new();

    public TelemetryObserver(string matchId, ulong seed)
    {
        MatchId = matchId;
        Seed = seed;
    }

    public void OnFrame(FightSimulation sim, int frame) { }

    public void OnSimEvent(FightSimulation sim, SimEvent e)
    {
        var a = sim.Fighters[0];
        var b = sim.Fighters[1];
        Rows.Add($"{MatchId},{Seed},{e.Frame},{e.Type},{e.A},{e.B},{e.Value}," +
            $"{a.Hp},{b.Hp},{a.Ex},{b.Ex},{a.Res},{b.Res}," +
            $"{(a.RageActive ? 1 : 0)},{(b.RageActive ? 1 : 0)}," +
            $"{a.ComboGiven},{b.ComboGiven},{sim.RoundNumber}");
    }

    /// <summary>CSV with header; byte-stable for identical runs (no wall clock, no floats).</summary>
    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder(4096);
        sb.AppendLine("match,seed,frame,type,a,b,value,hp0,hp1,ex0,ex1,res0,res1,rage0,rage1,combo0,combo1,round");
        foreach (var r in Rows) sb.AppendLine(r);
        return sb.ToString();
    }
}

public sealed class TelemetryTests
{
    [Fact]
    public void Telemetry_ByteIdenticalAcrossRuns_AndHasHeader()
    {
        static string Run()
        {
            var s = New(9101);
            var t = new TelemetryObserver("m1", 9101);
            s.Observer = t;
            for (int f = 0; f < 400; f++) FightDeterminismTests.Script(s, f);
            return t.ToCsv();
        }
        string a = Run(), b = Run();
        Assert.Equal(a, b);
        Assert.StartsWith("match,seed,frame,type", a);
        Assert.Contains(",Hit,", a);
    }

    [Fact]
    public void Telemetry_WritesCsvFile()
    {
        var s = New(9102);
        WalkIntoRange(s); // script jabs need range; otherwise the trace is vacuous
        var t = new TelemetryObserver("m2", 9102);
        s.Observer = t;
        for (int f = 0; f < 200; f++) FightDeterminismTests.Script(s, f);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "df-telemetry-m2.csv");
        System.IO.File.WriteAllText(path, t.ToCsv());
        try
        {
            Assert.True(new System.IO.FileInfo(path).Length > 100);
        }
        finally { System.IO.File.Delete(path); }
    }
}

public sealed class EconomySimTests
{
    private readonly ITestOutputHelper _out;
    public EconomySimTests(ITestOutputHelper o) => _out = o;

    private sealed class EconObserver : IFightObserver
    {
        public int RageOn0, RageOn1, ResOn, Hits, Blocks, Parrys, Rounds;
        public void OnFrame(FightSimulation sim, int frame) { }
        public void OnSimEvent(FightSimulation sim, SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.RageOn: if (e.A == 0) RageOn0++; else RageOn1++; break;
                case SimEventType.ResOn:
                case SimEventType.ResAuto: ResOn++; break;
                case SimEventType.Hit:
                case SimEventType.CounterHit: Hits++; break;
                case SimEventType.Block: Blocks++; break;
                case SimEventType.Parry: Parrys++; break;
                case SimEventType.RoundEnd: Rounds++; break;
            }
        }
    }

    private void Probe(string name, FighterData a, FighterData b, StageData stage)
    {
        var s = new FightSimulation(9200, a, b, 80 * 60, stage);
        var o = new EconObserver();
        s.Observer = o;
        int frames = 0, exGen0 = 0, exGen1 = 0, pex0 = 0, pex1 = 0;
        for (int f = 0; f < 2400 && !s.Over; f++)
        {
            FightDeterminismTests.Script(s, f);
            frames++;
            // EX deltas per frame (generation - spend), bounded by construction.
            exGen0 += s.Fighters[0].Ex - pex0 > 0 ? s.Fighters[0].Ex - pex0 : 0;
            exGen1 += s.Fighters[1].Ex - pex1 > 0 ? s.Fighters[1].Ex - pex1 : 0;
            pex0 = s.Fighters[0].Ex; pex1 = s.Fighters[1].Ex;
            Assert.InRange(s.Fighters[0].Ex, 0, 3000);
            Assert.InRange(s.Fighters[1].Ex, 0, 3000);
        }
        _out.WriteLine($"{name}: frames={frames} hits={o.Hits} blocks={o.Blocks} parrys={o.Parrys} " +
            $"rage0={o.RageOn0} rage1={o.RageOn1} res={o.ResOn} exGen0={exGen0} exGen1={exGen1} " +
            $"finalEX=({s.Fighters[0].Ex},{s.Fighters[1].Ex}) over={s.Over}");
        Assert.True(o.Hits > 0, "Script produced no hits: probe is vacuous.");
        Assert.True(exGen0 + exGen1 > 0, "No EX generated in 2400 aggressive frames: starvation.");
    }

    [Fact]
    public void EconomyMatrix_ReportsMetrics()
    {
        Probe("Glen/Glen/Arena", Glen, Glen, StageData.Arena());
        Probe("Glen/Kira/Arena", Glen, Kira, StageData.Arena());
        Probe("Kira/Glen/Arena", Kira, Glen, StageData.Arena());
        Probe("Glen/Glen/Dojo", Glen, Glen, StageData.Dojo());
    }
}
