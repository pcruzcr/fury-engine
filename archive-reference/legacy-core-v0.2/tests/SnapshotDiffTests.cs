// REFACTOR+HARDEN A4: SnapshotDiff tests (field-level mismatch reporting).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class SnapshotDiffTests
{
    [Fact]
    public void IdenticalSnapshots_DiffEmpty_Fight()
    {
        var s = FightKit.New(3);
        FightKit.Neutrals(s, 30);
        var a = s.Capture();
        Assert.Equal(string.Empty, SnapshotDiff.Diff(a, a));
    }

    [Fact]
    public void IdenticalSnapshots_DiffEmpty_Substrate()
    {
        var sim = new DeterministicSim(3);
        for (int f = 0; f < 30; f++) sim.Step(InputGen.P1(f), InputGen.P2(f));
        var a = sim.Capture();
        Assert.Equal(string.Empty, SnapshotDiff.Diff(a, a));
    }

    [Fact]
    public void DivergedSnapshots_NamesEveryDifferingField()
    {
        var a = FightKit.New(3);
        FightKit.Neutrals(a, 30);
        var snapA = a.Capture();

        var b = FightKit.New(3);
        FightKit.Neutrals(b, 30);
        // Diverge: walk P1 toward P2 for 20 frames (position + state change).
        for (int k = 0; k < 20; k++)
            FightKit.T(b, Buttons.None, b.Fighters[0].Facing, 0, Buttons.None, 0, 0);
        var snapB = b.Capture();

        string diff = SnapshotDiff.Diff(snapA, snapB);
        Assert.Contains("P0.PosX", diff);
        Assert.Contains("P0.State", diff);
        Assert.Contains("Frame", diff);
        Assert.DoesNotContain("P1.Hp", diff); // untouched: must NOT be reported
    }

    [Fact]
    public void HpDifference_ReportsExactPathAndValues()
    {
        var a = FightKit.New(3);
        FightKit.Neutrals(a, 10);
        var snapA = a.Capture();

        var b = FightKit.New(3);
        FightKit.Neutrals(b, 10);
        b.Fighters[1].Hp -= 2; // direct state divergence (diagnostics-only mutation)
        var snapB = b.Capture();

        string diff = SnapshotDiff.Diff(snapA, snapB);
        Assert.Contains($"P1.Hp: expected={a.Fighters[1].Hp} actual={b.Fighters[1].Hp}", diff);
    }

    [Fact]
    public void TruncatedPayload_ThrowsTyped()
    {
        var s = FightKit.New(3);
        FightKit.Neutrals(s, 5);
        var good = s.Capture();
        var goodPayload = good.Payload;
        var badPayload = new byte[goodPayload.Length / 2];
        System.Array.Copy(goodPayload, badPayload, badPayload.Length);
        var bad = new FightSnapshot(badPayload, true);
        Assert.Throws<SnapshotSchemaException>(() => SnapshotDiff.Diff(bad, good));
    }
}
