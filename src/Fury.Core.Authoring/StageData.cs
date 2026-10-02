namespace Fury.Core.Authoring;

/// <summary>Engine-free RGBA color (0..1 floats) used by authoring documents.</summary>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1f);

/// <summary>Stage authoring document: stage bounds, gravity and static elements.</summary>
public class StageData
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public float Gravity { get; set; }
    public ColorRgba BackgroundColor { get; set; }
    public List<PlatformData> Platforms { get; set; } = new();
    public List<HazardData> Hazards { get; set; } = new();
}

public class PlatformData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class HazardData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Damage { get; set; }
}
