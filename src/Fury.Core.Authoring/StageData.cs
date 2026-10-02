namespace Fury.Core.Authoring;

/// <summary>Engine-free RGBA color (0..1 floats) used by authoring documents.</summary>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1f);

/// <summary>How a platform behaves when a fighter lands on it.</summary>
public enum PlatformKind
{
    /// <summary>Land from above; jump up through it.</summary>
    Solid = 0,

    /// <summary>Like solid, but a fighter can drop through it by holding Down.</summary>
    PassThrough = 1,

    /// <summary>Trampoline: landing throws the fighter back up (strength = launch speed x 100, per frame).</summary>
    Bounce = 2,

    /// <summary>Pushes standing fighters sideways (strength = speed x 1000 units per frame; negative = left).</summary>
    Conveyor = 3,
}

/// <summary>
/// Stage authoring document. Coordinates are pixels at the reference scale (48 pixels = 1 simulation unit); the origin is the
/// centre of the stage ON THE FLOOR, X grows right and Y grows UP (a platform at Y = 150 hangs about 3 units above the
/// floor). Width / Height / Gravity define the arena, platforms and hazards its static geometry.
/// </summary>
public class StageData
{
    public string Name { get; set; } = "";

    /// <summary>Arena width in pixels (the playable stage is centred; half of it on each side).</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Gravity multiplier (1 = the default arena gravity).</summary>
    public float Gravity { get; set; }

    public ColorRgba BackgroundColor { get; set; }

    public List<PlatformData> Platforms { get; set; } = new();

    public List<HazardData> Hazards { get; set; } = new();

    /// <summary>The simulation holds at most this many platforms plus hazards.</summary>
    public const int MaxElements = 8;

    /// <summary>Checks every value a stage file can carry so a hand-edited file can never produce an unplayable arena.</summary>
    /// <exception cref="InvalidDataException">The stage is out of range.</exception>
    public void Validate()
    {
        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidDataException(message);
        }

        Require(Width is >= 480 and <= 20000, "Stage width must be 480..20000 pixels.");
        Require(Height is >= 100 and <= 20000, "Stage height must be 100..20000 pixels.");
        Require(Gravity is >= 0.1f and <= 5f, "Gravity must be 0.1..5.");
        Require(Platforms.Count + Hazards.Count <= MaxElements, $"A stage holds at most {MaxElements} platforms and hazards in total.");

        foreach (var p in Platforms)
        {
            Require(p.Width is >= 8 and <= 20000 && p.Height is >= 4 and <= 2000, "Platform size out of range.");
            Require(System.Math.Abs(p.X) <= 20000 && System.Math.Abs(p.Y) <= 20000 && System.Math.Abs(p.MoveToX) <= 20000 && System.Math.Abs(p.MoveToY) <= 20000, "Platform position out of range.");
            Require(Enum.IsDefined(p.Kind), "Unknown platform kind.");
            Require(p.MoveFrames == 0 || p.MoveFrames is >= 2 and <= 36000, "MoveFrames must be 0 (static) or 2..36000.");
            Require(p.Strength is >= -100000 and <= 100000, "Platform strength out of range.");
        }

        foreach (var h in Hazards)
        {
            Require(h.Width is >= 4 and <= 20000 && h.Height is >= 4 and <= 20000, "Hazard size out of range.");
            Require(System.Math.Abs(h.X) <= 20000 && System.Math.Abs(h.Y) <= 20000, "Hazard position out of range.");
            Require(h.Damage is >= 0 and <= 500, "Hazard damage must be 0..500.");
        }
    }
}

/// <summary>A landing surface. Centre (<see cref="X"/>, <see cref="Y"/>) and size in pixels; optionally moves to (<see cref="MoveToX"/>, <see cref="MoveToY"/>) and back every <see cref="MoveFrames"/> frames.</summary>
public class PlatformData
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public PlatformKind Kind { get; set; }

    /// <summary>Meaning depends on <see cref="Kind"/> (bounce speed, conveyor speed); 0 = sensible default.</summary>
    public int Strength { get; set; }

    public int MoveToX { get; set; }

    public int MoveToY { get; set; }

    /// <summary>Period in frames of the back-and-forth movement; 0 = static.</summary>
    public int MoveFrames { get; set; }
}

/// <summary>A damage zone: fighters overlapping it lose <see cref="Damage"/> health every half second.</summary>
public class HazardData
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Damage { get; set; }
}
