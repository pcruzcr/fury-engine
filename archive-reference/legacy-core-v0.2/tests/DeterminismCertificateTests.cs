// §56 — living determinism certificate. Values below are EXECUTED evidence
// (written to test output on every run), never hand-filled.
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class DeterminismCertificateTests
{
    private readonly ITestOutputHelper _out;
    public DeterminismCertificateTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void DeterminismCertificate_10kFrames()
    {
        const ulong seed = 0xD0A1_F00D_2026_0913UL;
        const int frames = 10_000;

        static ulong Run(ulong seed, int frames)
        {
            var sim = new DeterministicSim(seed);
            for (int f = 0; f < frames; f++)
                sim.Step(InputGen.P1(f), InputGen.P2(f));
            return sim.Capture().Hash;
        }

        ulong a = Run(seed, frames);
        ulong b = Run(seed, frames);

        // Snapshot @ frame 5000, restore, resimulate to 10k.
        var mid = new DeterministicSim(seed);
        for (int f = 0; f < 5000; f++)
            mid.Step(InputGen.P1(f), InputGen.P2(f));
        var snap = mid.Capture();
        var resumed = new DeterministicSim(0);
        resumed.Restore(snap);
        for (int f = 5000; f < frames; f++)
            resumed.Step(InputGen.P1(f), InputGen.P2(f));
        ulong c = resumed.Capture().Hash;

        _out.WriteLine("DUAL FURY DETERMINISM CERTIFICATE");
        _out.WriteLine($"Godot: 4.7.2-stable (config authored, editor binary NOT TESTED)");
        _out.WriteLine($".NET: 9.0.318 / net9.0");
        _out.WriteLine($"Simulation: 60Hz, F0 substrate (no combat yet)");
        _out.WriteLine($"Seed: 0x{seed:X16}");
        _out.WriteLine($"Frames: {frames}");
        _out.WriteLine($"Run A Hash: 0x{a:X16}");
        _out.WriteLine($"Run B Hash: 0x{b:X16}");
        _out.WriteLine($"Determinism: {(a == b ? "PASS" : "FAIL")}");
        _out.WriteLine($"Snapshot Frame: 5000, Payload: {snap.Payload.Length}B, SnapHash: 0x{snap.Hash:X16}");
        _out.WriteLine($"Restoration+Resimulation: {(a == c ? "PASS" : "FAIL")} (0x{c:X16})");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }
}
