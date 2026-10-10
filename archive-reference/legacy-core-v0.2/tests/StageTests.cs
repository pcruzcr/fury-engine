// Stage tests: Dojo meditation zones (data → sim → snapshot).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class StageTests
{
    [Fact]
    public void Presets_Validate()
    {
        StageData.Validate(StageData.Arena());
        StageData.Validate(StageData.Dojo());
        Assert.Empty(StageData.Arena().Zones);
        Assert.Single(StageData.Dojo().Zones);
    }

    [Fact]
    public void Validator_RejectsBadZones()
    {
        var overlap = new StageData
        {
            Name = "bad",
            Zones = new[] { new MeditationZone(100000, 60000), new MeditationZone(150000, 60000) },
        };
        Assert.Throws<System.InvalidOperationException>(() => StageData.Validate(overlap));
        var outside = new StageData
        {
            Name = "bad2",
            Zones = new[] { new MeditationZone(5000, 60000) },
        };
        Assert.Throws<System.InvalidOperationException>(() => StageData.Validate(outside));
    }

    [Fact]
    public void Arena_NoZoneMults()
    {
        var s = New(41);
        for (int k = 0; k < 100; k++) FightDeterminismTests.Script(s, k);
        Assert.Equal(1000, s.Fighters[0].ExZonePerMille);
        Assert.Equal(1000, s.Fighters[1].SpeedZonePerMille);
        Assert.Equal(StageData.ArenaNeutral, s.Stage.Id);
    }

    [Fact]
    public void Dojo_ZoneUpdatesByPosition()
    {
        var s = new FightSimulation(42, Glen, Glen, 80 * 60, StageData.Dojo());
        Assert.Equal(1000, s.Fighters[0].ExZonePerMille); // spawn 300000: outside [380000,580000]
        for (int k = 0; k < 60; k++)
            T(s, Buttons.None, 1, 0, Buttons.None, 0, 0); // p0 walks right into the zone
        Assert.True(s.Fighters[0].Pos.X >= 380000);
        Assert.Equal(1050, s.Fighters[0].ExZonePerMille);
        Assert.Equal(900, s.Fighters[0].SpeedZonePerMille);
    }

    [Fact]
    public void Dojo_ExPlus5_Unit()
    {
        var f = new Fighter { ExZonePerMille = 1050 };
        EnergySystem.OnDamageDealt(f, 28, MoveKind.Normal);
        Assert.Equal(28 / 5 * 30 * 1050 / 1000, f.Ex); // 157 vs 150 neutral
        var g = new Fighter { ExZonePerMille = 1000 };
        EnergySystem.OnDamageDealt(g, 28, MoveKind.Normal);
        Assert.Equal(150, g.Ex);
    }

    [Fact]
    public void Dojo_WalkSpeedMinus10_InsideZone()
    {
        var s = new FightSimulation(43, Glen, Glen, 80 * 60, StageData.Dojo());
        for (int k = 0; k < 60 && s.Fighters[0].Pos.X < 400000; k++)
            T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        int x0 = s.Fighters[0].Pos.X;
        for (int k = 0; k < 10; k++)
            T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        Assert.Equal(2500 * 900 / 1000 * 10, s.Fighters[0].Pos.X - x0); // 2250/f
    }

    [Fact]
    public void Dojo_DeterminismAndRestore()
    {
        static ulong Run()
        {
            var s = new FightSimulation(44, Glen, Kira, 80 * 60, StageData.Dojo());
            for (int f = 0; f < 400; f++) FightDeterminismTests.Script(s, f);
            return s.Capture().Hash;
        }
        Assert.Equal(Run(), Run());

        var part = new FightSimulation(44, Glen, Kira, 80 * 60, StageData.Dojo());
        for (int f = 0; f < 150; f++) FightDeterminismTests.Script(part, f);
        var snap = part.Capture();
        var resumed = new FightSimulation(999, Glen, Kira, 80 * 60, StageData.Dojo());
        resumed.Restore(snap);
        for (int f = 150; f < 400; f++) FightDeterminismTests.Script(resumed, f);

        var full = new FightSimulation(44, Glen, Kira, 80 * 60, StageData.Dojo());
        for (int f = 0; f < 400; f++) FightDeterminismTests.Script(full, f);
        Assert.Equal(full.Capture().Hash, resumed.Capture().Hash);
    }

    [Fact]
    public void StageMismatch_Rejected()
    {
        var dojo = new FightSimulation(45, Glen, Glen, 80 * 60, StageData.Dojo());
        for (int f = 0; f < 20; f++) FightDeterminismTests.Script(dojo, f);
        var snap = dojo.Capture();
        var arena = new FightSimulation(45, Glen, Glen);
        var ex = Record.Exception(() => arena.Restore(snap));
        Assert.IsType<SnapshotSchemaException>(ex);
    }
}
