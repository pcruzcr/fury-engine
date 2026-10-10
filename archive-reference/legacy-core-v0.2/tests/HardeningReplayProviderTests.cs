// REFACTOR+HARDEN A7/D6: replay v2 (header/body/footer, tamper rejection).
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class ReplayV2Tests
{
    private readonly ITestOutputHelper _out;
    public ReplayV2Tests(ITestOutputHelper @out) => _out = @out;

    private static (FightSnapshot Initial, InputState[] P1, InputState[] P2) Record300(ulong seed)
    {
        var live = new FightSimulation(seed, GlenData.Build(), KiraData.Build());
        var initial = live.Capture();
        var p1 = new InputState[300];
        var p2 = new InputState[300];
        for (int f = 0; f < 300; f++)
        {
            p1[f] = new InputState(Buttons.None, 0, 0, live.Frame);
            p2[f] = new InputState(Buttons.None, 0, 0, live.Frame);
            live.Step(p1[f], p2[f]);
        }
        return (initial, p1, p2);
    }

    [Fact]
    public void WriteRead_Resimulate_FinalHashMatches()
    {
        const ulong seed = 0xF2F2;
        var (initial, p1, p2) = Record300(seed);
        byte[] bytes = ReplayV2.SaveBytes(seed, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2, ReplayPlatform.Win64);

        var loaded = ReplayV2.LoadBytes(bytes);
        Assert.Equal(seed, loaded.Seed);
        Assert.Equal(300, loaded.Count);
        Assert.Equal(ReplayPlatform.Win64, loaded.Platform);

        // Live hashes vs resimulated hashes, frame by frame.
        var live = new FightSimulation(seed, GlenData.Build(), KiraData.Build());
        ulong[] resim = loaded.PlayAll();
        for (int f = 0; f < 300; f++)
        {
            live.Step(p1[f], p2[f]);
            Assert.Equal(live.Capture().Hash, resim[f]);
        }
        _out.WriteLine($"replay v2: {bytes.Length}B for 300 frames (uncompressed; LZ4 is OPEN)");
    }

    [Fact]
    public void SaveLoad_File_RoundTrips()
    {
        var (initial, p1, p2) = Record300(0xAB);
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"df_test_{System.Guid.NewGuid():N}.fr2p");
        try
        {
            ReplayV2.Save(path, 0xAB, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
                initial, p1, p2);
            var loaded = ReplayV2.Load(path);
            Assert.Equal(300, loaded.Count);
            Assert.Equal(0xABUL, loaded.Seed);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void CorruptOneBodyByte_ThrowsReplayCorrupt()
    {
        var (initial, p1, p2) = Record300(0xCD);
        byte[] bytes = ReplayV2.SaveBytes(0xCD, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2);
        bytes[100] ^= 0xFF; // adversarial single-byte corruption in the body
        var ex = Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bytes));
        _out.WriteLine($"body corruption: {ex.Message}");
    }

    [Fact]
    public void CorruptFooter_ThrowsReplayCorrupt()
    {
        var (initial, p1, p2) = Record300(0xEF);
        byte[] bytes = ReplayV2.SaveBytes(0xEF, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2);
        bytes[^1] ^= 0x01;
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bytes));
    }

    [Fact]
    public void BadMagic_ThrowsReplayCorrupt()
    {
        var (initial, p1, p2) = Record300(0x12);
        byte[] bytes = ReplayV2.SaveBytes(0x12, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2);
        bytes[0] = 0x00;
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bytes));
    }

    [Fact]
    public void Truncated_ThrowsReplayCorrupt_NotRawIndexError()
    {
        var (initial, p1, p2) = Record300(0x34);
        byte[] bytes = ReplayV2.SaveBytes(0x34, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2);
        byte[] cut = new byte[bytes.Length / 2];
        System.Array.Copy(bytes, cut, cut.Length);
        var ex = Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(cut));
        _out.WriteLine($"truncation: {ex.Message}");
    }

    [Fact]
    public void UnknownCast_ThrowsReplayCorrupt()
    {
        Assert.Throws<ReplayCorruptException>(() => ReplayCast.DataFor(99));
        Assert.Throws<ReplayCorruptException>(() => ReplayCast.StageFor(99));
    }

    [Fact]
    public void MismatchedStreams_Rejected()
    {
        var (initial, p1, _) = Record300(0x56);
        Assert.Throws<ArgumentException>(() => ReplayV2.SaveBytes(0x56, StageData.ArenaNeutral,
            ReplayCast.Glen, ReplayCast.Kira, initial, p1, new InputState[10]));
    }
}

public sealed class InputProviderTests
{
    [Fact]
    public void Scripted_RoundTrips_ThenEnds()
    {
        var p1 = new[] { new InputState(Buttons.LP, 0, 0, 0), new InputState(Buttons.HP, 1, 0, 1) };
        var p2 = new[] { new InputState(Buttons.None, 0, 0, 0), new InputState(Buttons.None, 0, 0, 1) };
        var prov = new ScriptedInputProvider(p1, p2);
        Assert.Equal(2, prov.Count);
        Assert.True(prov.TryGetInputs(0, out InputState a0, out _));
        Assert.Equal(Buttons.LP, a0.Buttons);
        Assert.True(prov.TryGetInputs(1, out _, out InputState b1));
        Assert.Equal(1, b1.Frame);
        Assert.False(prov.TryGetInputs(2, out _, out _));
        Assert.False(prov.TryGetInputs(-1, out _, out _));
    }

    [Fact]
    public void Scripted_Validation()
    {
        var ok = new[] { new InputState(Buttons.None, 0, 0, 0) };
        Assert.Throws<ArgumentNullException>(() => new ScriptedInputProvider(null!, ok));
        Assert.Throws<ArgumentNullException>(() => new ScriptedInputProvider(ok, null!));
        Assert.Throws<ArgumentException>(() => new ScriptedInputProvider(ok,
            new[] { new InputState(Buttons.None, 0, 0, 0), new InputState(Buttons.None, 0, 0, 1) }));
    }

    [Fact]
    public void Provider_DrivesSim_Deterministically()
    {
        var p1 = new InputState[60];
        var p2 = new InputState[60];
        for (int f = 0; f < 60; f++)
        {
            p1[f] = InputGen.P1(f);
            p2[f] = InputGen.P2(f);
        }
        static ulong Run(InputState[] x, InputState[] y)
        {
            var prov = new ScriptedInputProvider(x, y);
            var sim = new FightSimulation(7, GlenData.Build(), GlenData.Build());
            int f = 0;
            while (prov.TryGetInputs(f, out InputState a, out InputState b))
            {
                sim.Step(a, b);
                f++;
            }
            return sim.Capture().Hash;
        }
        Assert.Equal(Run(p1, p2), Run(p1, p2));
    }
}
