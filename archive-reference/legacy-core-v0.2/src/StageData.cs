// Fighter.Core — stage data: meditation zones (Dojo Central, GDD §7.1).
// Stages are DATA: only the stage id travels in snapshots; geometry is static per id.
// Zone geometry not specified by GDD → [CAL], centralized here, flagged for design.
namespace Fighter.Core;

/// <summary>Meditation zone: inside it, EX generation +5% and locomotion −10% (GDD §7.1).</summary>
public readonly struct MeditationZone
{
    /// <summary>Center X, millipixels.</summary>
    public readonly int CenterX;
    /// <summary>Half-width, millipixels (zone = CenterX ± HalfWidth).</summary>
    public readonly int HalfWidth;
    /// <summary>Constructs a zone (validated by <see cref="StageData"/>).</summary>
    public MeditationZone(int centerX, int halfWidth) { CenterX = centerX; HalfWidth = halfWidth; }

    /// <summary>True when feet X is inside.</summary>
    public bool Contains(int x) => x >= CenterX - HalfWidth && x <= CenterX + HalfWidth;
}

/// <summary>Stage definition: id, name, meditation zones (possibly none).</summary>
public sealed class StageData
{
    /// <summary>Arena Neutral: competitive, no modifiers (GDD §7.1).</summary>
    public const int ArenaNeutral = 0;
    /// <summary>Dojo Central: meditation zones (GDD §7.1).</summary>
    public const int DojoCentral = 1;

    /// <summary>EX generation inside a zone: +5% (GDD §7.1, specified).</summary>
    public const int ExZonePerMille = 1050;
    /// <summary>Locomotion inside a zone: −10% walk/backdash (GDD §7.1, specified).</summary>
    public const int SpeedZonePerMille = 900;

    /// <summary>Stage id.</summary>
    public int Id;
    /// <summary>Display name.</summary>
    public string Name = "";
    /// <summary>Zones (empty = neutral).</summary>
    public MeditationZone[] Zones = System.Array.Empty<MeditationZone>();

    /// <summary>Arena Neutral (no zones).</summary>
    public static StageData Arena() => new() { Id = ArenaNeutral, Name = "Arena Neutral" };

    /// <summary>Dojo Central: one central zone [380000, 580000] [CAL].</summary>
    public static StageData Dojo() => new()
    {
        Id = DojoCentral,
        Name = "Dojo Central",
        Zones = new[] { new MeditationZone(480000, 100000) },
    };

    /// <summary>Validates: bounds, positive widths, no overlaps (loud, §52).</summary>
    public static void Validate(StageData d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new InvalidOperationException("Stage needs a name.");
        foreach (var z in d.Zones)
        {
            if (z.HalfWidth <= 0) throw new InvalidOperationException($"{d.Name}: zone half-width must be > 0.");
            if (z.CenterX - z.HalfWidth < Calibration.StageMinX || z.CenterX + z.HalfWidth > Calibration.StageMaxX)
                throw new InvalidOperationException($"{d.Name}: zone outside stage bounds.");
        }
        var ordered = new System.Collections.Generic.List<MeditationZone>(d.Zones);
        ordered.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].CenterX - ordered[i].HalfWidth <= ordered[i - 1].CenterX + ordered[i - 1].HalfWidth)
                throw new InvalidOperationException($"{d.Name}: overlapping zones.");
    }
}
