// SCENARIO-018 (§48): CPU convergence — AI through the common input path.
// Proves: deterministic across runs + real action coverage (lands hits, blocks).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class CpuConvergenceTests
{
    private static void CpuMatch(FightSimulation s, int frames)
    {
        for (int f = 0; f < frames && !s.Over; f++)
        {
            var p0 = FightDeterminismScript.P0(s.Frame);
            s.Step(p0, CpuBrain.ForP2(s, s.Frame));
        }
    }

    [Fact]
    public void CpuVsScripted_Deterministic()
    {
        static ulong Run()
        {
            var s = NewVsKira(9001);
            CpuMatch(s, 800);
            return s.Capture().Hash;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Cpu_HitsPassiveDummy()
    {
        var s = NewVsKira(9004);
        WalkIntoRange(s);
        bool cpuHit = false;
        for (int f = 0; f < 1200 && !s.Over; f++)
        {
            s.Step(new InputState(Buttons.None, 0, 0, s.Frame), CpuBrain.ForP2(s, s.Frame));
            foreach (var e in s.LastSimEvents)
                if ((e.Type is SimEventType.Hit or SimEventType.CounterHit) && e.A == 1) cpuHit = true;
            if (cpuHit) break;
        }
        Assert.True(cpuHit, "CPU never landed a hit on a passive dummy in 1200 frames.");
    }

    [Fact]
    public void Cpu_BlocksSpacedJabs()
    {
        // P1 is ALWAYS driven by CpuBrain (a neutral-fed dummy could never block;
        // that harness bug cost a full debug cycle, 2026-09-13 — hence this comment).
        static void Both(FightSimulation s, InputState p0) =>
            s.Step(p0, CpuBrain.ForP2(s, s.Frame));
        static InputState N(FightSimulation s) =>
            new(Buttons.None, 0, 0, s.Frame);

        var s = NewVsKira(9002);
        WalkIntoRange(s);
        int blocks = 0;
        // Spaced single jabs, phase-aligned: impact lands on frame%5==0 so the CPU's
        // f%5<3 block gate is open (a fixed spacing would phase-lock it shut).
        for (int r = 0; r < 10; r++)
        {
            for (int k = 0; k < 60 && s.Frame % 5 != 1; k++) Both(s, N(s));
            Both(s, new InputState(Buttons.LP, 0, 0, s.Frame)); // tap LP
            for (int k = 0; k < 30; k++)
            {
                Both(s, N(s));
                foreach (var e in s.LastSimEvents)
                    if (e.Type == SimEventType.Block && e.B == 1) blocks++;
            }
        }
        Assert.True(blocks > 0, "CPU never blocked.");
    }
}

/// <summary>Deterministic P0 sparring script (shared by CPU convergence + economy probes).</summary>
internal static class FightDeterminismScript
{
    internal static InputState P0(int f)
    {
        int t = f % 100;
        if (t < 25) return new InputState(Buttons.None, 1, 0, f);
        if (t == 26) return new InputState(Buttons.LP, 0, 0, f);
        if (t == 30) return new InputState(Buttons.MP, 0, 0, f);
        if (t is 31 or 32) return new InputState(Buttons.None, -1, 0, f);
        if (t == 45) return new InputState(Buttons.HK, 0, 0, f);
        if (t == 60) return new InputState(Buttons.LP | Buttons.MP, 0, 0, f);
        return new InputState(Buttons.None, 0, 0, f);
    }
}
