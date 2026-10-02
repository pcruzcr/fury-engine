using Fury.Core.Math;
using Fury.Core.Observability;
using Xunit;

namespace Fury.Core.Tests.Math;

public class CombatHeatmapTests
{
    [Fact]
    public void Record_QuantizesToRealCells_AndExportsCoordinates()
    {
        var map = new CombatHeatmap();
        map.Record(HeatmapKind.Hit, Fp.FromRatio(5, 2).Raw, Fp.FromRatio(-3, 2).Raw);   // (2.5, -1.5) -> cell (2, -2)
        map.Record(HeatmapKind.Hit, Fp.FromRatio(21, 10).Raw, Fp.FromRatio(-19, 10).Raw); // (2.1, -1.9) -> same cell
        map.Record(HeatmapKind.Block, Fp.FromInt(7).Raw, 0);

        Assert.Equal(2, map.Count(HeatmapKind.Hit, 2, -2));
        Assert.Equal(1, map.Count(HeatmapKind.Block, 7, 0));
        Assert.Equal("{\"hits\":{\"2,-2\":2},\"blocks\":{\"7,0\":1},\"kos\":{}}", map.ExportJson());
    }

    [Fact]
    public void Memory_IsBounded()
    {
        var map = new CombatHeatmap();
        for (int i = 0; i < CombatHeatmap.MaxCells + 500; i++) map.Record(HeatmapKind.Hit, i << 16, 0);
        Assert.Equal(CombatHeatmap.MaxCells, map.Total(HeatmapKind.Hit));
        map.Record(HeatmapKind.Hit, 0, 0); // existing cells still count
        Assert.Equal(CombatHeatmap.MaxCells + 1, map.Total(HeatmapKind.Hit));
    }
}
