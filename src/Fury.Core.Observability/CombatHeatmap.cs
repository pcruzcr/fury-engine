using System.Globalization;
using System.Text;

namespace Fury.Core.Observability;

/// <summary>What happened at a stage cell.</summary>
public enum HeatmapKind
{
    Hit = 0,
    Block = 1,
    Ko = 2,
}

/// <summary>
/// Opt-in balance telemetry: counts where on the stage hits, blocks and KOs happen. Positions are
/// quantized to a grid of whole simulation units and stored as REAL cell coordinates (the previous
/// version stored an irreversible spatial hash, so the data could never be drawn as a map).
/// No identifiers or personal data are collected. Memory is bounded by <see cref="MaxCells"/>.
/// </summary>
public sealed class CombatHeatmap
{
    public const int MaxCells = 4096;
    private const int FractionBits = 16; // Q16.16 raw → whole units

    private readonly Dictionary<(int X, int Y), int>[] _cells =
    [
        new(), new(), new(),
    ];

    /// <summary>Records an event at a position given as raw Q16.16 coordinates.</summary>
    public void Record(HeatmapKind kind, int xRaw, int yRaw)
    {
        var cells = _cells[(int)kind];
        var key = (xRaw >> FractionBits, yRaw >> FractionBits);
        if (cells.TryGetValue(key, out int count)) cells[key] = count + 1;
        else if (cells.Count < MaxCells) cells[key] = 1;
    }

    public int Count(HeatmapKind kind, int cellX, int cellY) => _cells[(int)kind].GetValueOrDefault((cellX, cellY));

    public int Total(HeatmapKind kind) => _cells[(int)kind].Values.Sum();

    public bool IsEmpty => _cells.All(c => c.Count == 0);

    /// <summary>Exports the map as deterministic JSON: <c>{"hits":{"x,y":n},"blocks":{...},"kos":{...}}</c>.</summary>
    public string ExportJson()
    {
        var sb = new StringBuilder("{");
        string[] names = ["hits", "blocks", "kos"];
        for (int k = 0; k < _cells.Length; k++)
        {
            if (k > 0) sb.Append(',');
            sb.Append('"').Append(names[k]).Append("\":{");
            bool first = true;
            foreach (var ((x, y), n) in _cells[k].OrderBy(e => e.Key.X).ThenBy(e => e.Key.Y))
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(x.ToString(CultureInfo.InvariantCulture)).Append(',').Append(y.ToString(CultureInfo.InvariantCulture))
                  .Append("\":").Append(n.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
            sb.Append('}');
        }
        return sb.Append('}').ToString();
    }

    public void Clear()
    {
        foreach (var cells in _cells) cells.Clear();
    }
}
