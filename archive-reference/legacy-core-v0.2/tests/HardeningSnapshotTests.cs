// REFACTOR+HARDEN A4/A7/D5/D6/D8: schema-5 snapshot (GimmickData, intent,
// Diff), replay v2 integrity, size budgets.
using Fighter.Core;
using Xunit.Abstractions;

namespace Fighter.Core.Tests;

public sealed class GimmickDataTests
{
    [Fact]
    public void Size_Is128Bytes_Blittable()
    {
        Assert.Equal(128, System.Runtime.InteropServices.Marshal.SizeOf<GimmickData>());
    }

    [Fact]
    public void Default_IsEmpty_AllZero()
    {
        Assert.True(default(GimmickData).IsEmpty());
    }

    [Fact]
    public void SetWord_RoundTrips_TagPreserved()
    {
        var g = default(GimmickData);
        g.Tag = 7;
        g.SetWord(0, 0xDEADBEEFCAFEBABEUL);
        g.SetWord(14, 0x0102030405060708UL);
        Assert.False(g.IsEmpty());
        Assert.Equal(7, g.Tag);
        Assert.Equal(0xDEADBEEFCAFEBABEUL, g.GetWord(0));
        Assert.Equal(0x0102030405060708UL, g.GetWord(14));
        Assert.True(g.Equals(g));
        Assert.True(g.Equals((object)g));
        Assert.False(g.Equals("nope"));
    }

    [Fact]
    public void WordIndex_OutOfRange_Throws()
    {
        var g = default(GimmickData);
        Assert.Throws<ArgumentOutOfRangeException>(() => g.GetWord(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.GetWord(15));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.SetWord(15, 1));
    }
}

public sealed class Schema5SnapshotTests
{
    private readonly ITestOutputHelper _out;
    public Schema5SnapshotTests(ITestOutputHelper @out) => _out = @out;

    [Fact]
    public void RoundTrip_PreservesGimmickBlob_AndIntent()
    {
        var s = FightKit.New(4242);
        s.Fighters[0].GimmickState.Tag = 3;
        s.Fighters[0].GimmickState.SetWord(7, 0xA5A5A5A5A5A5A5A5UL);
        FightKit.T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        var snap = s.Capture();
        var resumed = FightKit.New(1);
        resumed.Restore(snap);
        Assert.Equal(3, resumed.Fighters[0].GimmickState.Tag);
        Assert.Equal(0xA5A5A5A5A5A5A5A5UL, resumed.Fighters[0].GimmickState.GetWord(7));
        Assert.True(resumed.Fighters[1].GimmickState.IsEmpty());
        Assert.Equal(s.Fighters[0].LastCommandNormal, resumed.Fighters[0].LastCommandNormal);
        Assert.Equal(snap.Hash, resumed.Capture().Hash);
    }

    [Fact]
    public void TamperedSchema_ThrowsTypedException()
    {
        var s = FightKit.New(5);
        FightKit.Neutrals(s, 5);
        byte[] tampered = (byte[])s.Capture().Payload.Clone();
        tampered[0] ^= 0xFF; // corrupt the schema int
        var bad = new FightSnapshot(tampered, bypassValidation: true);
        var ex = Assert.Throws<SnapshotSchemaException>(() => FightKit.New(5).Restore(bad));
        _out.WriteLine($"tampered restore: {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public void SnapshotRoundTrip_500Plus500_Equals1000_WithDiffOnMismatch()
    {
        // D5: interrupted 1000f run must equal the uninterrupted run, else FAIL
        // with field-level diff output.
        var straight = FightKit.NewVsKira(0xBEEF);
        for (int f = 0; f < 1000 && !straight.Over; f++)
            FightKit.T(straight, Buttons.None, 0, 0, Buttons.None, 0, 0);
        var straightSnap = straight.Capture();

        var split = FightKit.NewVsKira(0xBEEF);
        for (int f = 0; f < 500 && !split.Over; f++)
            FightKit.T(split, Buttons.None, 0, 0, Buttons.None, 0, 0);
        var mid = split.Capture();
        var resumed = FightKit.NewVsKira(0x1234); // different seed proves restore overwrites
        resumed.Restore(mid);
        for (int f = 500; f < 1000 && !resumed.Over; f++)
            FightKit.T(resumed, Buttons.None, 0, 0, Buttons.None, 0, 0);
        var resumedSnap = resumed.Capture();

        string diff = SnapshotDiff.Diff(straightSnap, resumedSnap);
        Assert.True(string.IsNullOrEmpty(diff), $"Round-trip divergence:\n{diff}");
        Assert.Equal(straightSnap.Hash, resumedSnap.Hash);
    }

    [Fact]
    public void Diff_Identical_IsEmpty()
    {
        var s = FightKit.New(9);
        FightKit.Neutrals(s, 30);
        Assert.Equal(string.Empty, SnapshotDiff.Diff(s.Capture(), s.Capture()));
    }

    [Fact]
    public void Diff_NamesDifferingFields()
    {
        var a = FightKit.New(11);
        FightKit.Neutrals(a, 30);
        var b = FightKit.New(11);
        FightKit.Neutrals(b, 30);
        FightKit.Tap(b, 0, Buttons.LP); // exactly one frame of divergence
        string diff = SnapshotDiff.Diff(a.Capture(), b.Capture());
        _out.WriteLine(diff);
        Assert.Contains("Frame:", diff);
        Assert.Contains("P0.LastInput.Buttons:", diff);
    }

    [Fact]
    public void SubstrateDiff_Works()
    {
        var a = new DeterministicSim(3);
        var b = new DeterministicSim(3);
        a.Step(InputGen.P1(0), InputGen.P2(0));
        Assert.Contains("Frame:", SnapshotDiff.Diff(a.Capture(), b.Capture()));
        b.Step(InputGen.P1(0), InputGen.P2(0));
        Assert.Equal(string.Empty, SnapshotDiff.Diff(a.Capture(), b.Capture()));
    }

    [Fact]
    public void PayloadSize_RegressionGuard()
    {
        var s = FightKit.NewVsKira(13);
        for (int f = 0; f < 200; f++) FightDeterminismTests.Script(s, f);
        int bytes = s.Capture().Payload.Length;
        _out.WriteLine($"FightSnapshot schema-5 payload: {bytes}B (full histories)");
        // Regression guard, NOT the 512B mobile budget: the full fight snapshot
        // (~1.1KB with 8-deep histories) exceeds it by design — see KNOWN ISSUES
        // (delta compression is roadmap F5). This fails only on unexpected growth.
        Assert.True(bytes < 2048, $"Snapshot payload blew past 2KB: {bytes}B.");
        Assert.True(bytes > 512, "Sanity: schema-5 payload must exceed the 512B budget (documented gap).");
    }

    [Fact]
    public void InputStateSize_Documented()
    {
        int size = System.Runtime.InteropServices.Marshal.SizeOf<InputState>();
        _out.WriteLine($"InputState managed size: {size}B (wire: 16B; 8B budget is OPEN, packed v2 frames only)");
        Assert.Equal(16, size);
    }
}
