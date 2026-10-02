using Fury.Core.Authoring;
using Fury.Core.Math;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Stages;

/// <summary>A stage ready to fight on: arena rules, geometry module and background.</summary>
public sealed record PlayableStage(string Name, FightConfig Config, Func<StageModule?> CreateModule, Color Background);

/// <summary>
/// Turns stage documents (pixels, Y up) into what the simulation understands (simulation units, Y down, integer math only).
/// Stages live in <c>user://stages/*.stage</c>, are produced by the stage editor and validated on load. Moving platforms are
/// solid surfaces that travel between two points; every other platform kind keeps its own behaviour.
/// </summary>
public static class StageLoader
{
    public const string Folder = "user://stages";

    /// <summary>Pixels per simulation unit: the same scale the presentation uses.</summary>
    public const int PixelsPerUnit = (int)Presentation.WorldProjection.PixelsPerUnit;

    private const int DefaultBounceSpeedPercent = 70;   // 0.70 units per frame (about the jump speed)
    private const int DefaultConveyorSpeedPermille = 50; // 0.05 units per frame

    /// <summary>Names of the stages saved on disk (file names without extension), sorted.</summary>
    public static IReadOnlyList<string> List()
    {
        string directory = ProjectSettings.GlobalizePath(Folder);
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*" + StageSerializer.Extension)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Loads and validates a saved stage; null (with a warning) when it is missing or invalid.</summary>
    public static StageData? TryLoad(string name)
    {
        try
        {
            string path = Path.Combine(ProjectSettings.GlobalizePath(Folder), StageSerializer.SafeFileName(name));
            return File.Exists(path) ? StageSerializer.FromJson(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PushWarning($"Stage '{name}' ignored: {e.Message}");
            return null;
        }
    }

    /// <summary>Builds a playable stage from a document on top of the base rules.</summary>
    public static PlayableStage Build(StageData stage, FightConfig baseConfig)
    {
        stage.Validate();

        // Half of the arena width, never narrower than 8 units so both fighters fit with room to move.
        Fp half = Fp.FromRatio(System.Math.Max(8 * PixelsPerUnit, stage.Width / 2), PixelsPerUnit);
        int permille = (int)System.Math.Round(stage.Gravity * 1000f);
        var gravity = Fp.FromRaw((int)((long)baseConfig.GravityRaw.Raw * permille / 1000));
        var config = baseConfig with { StageHalfWidth = half, GravityRaw = gravity };

        var platforms = new List<PlatformSystem.PlatformData>();
        foreach (var p in stage.Platforms) platforms.Add(ConvertPlatform(p));
        foreach (var h in stage.Hazards) platforms.Add(ConvertHazard(h));

        var bg = stage.BackgroundColor;
        return new PlayableStage(
            stage.Name,
            config,
            () => platforms.Count == 0 ? null : new StageModule(platforms),
            new Color(bg.R, bg.G, bg.B, 1f));
    }

    private static Fp Units(int pixels) => Fp.FromRatio(pixels, PixelsPerUnit);

    /// <summary>Document Y grows up from the floor; the simulation grows down, with the floor at 0.</summary>
    private static Fp SimY(int pixelsUp) => Fp.FromRatio(-pixelsUp, PixelsPerUnit);

    private static PlatformSystem.PlatformData ConvertPlatform(PlatformData p)
    {
        bool moving = p.MoveFrames > 0;
        var type = moving ? PlatformSystem.PlatformType.Moving : p.Kind switch
        {
            PlatformKind.PassThrough => PlatformSystem.PlatformType.PassThrough,
            PlatformKind.Bounce => PlatformSystem.PlatformType.Bounce,
            PlatformKind.Conveyor => PlatformSystem.PlatformType.Conveyor,
            _ => PlatformSystem.PlatformType.Solid,
        };

        return new PlatformSystem.PlatformData
        {
            Type = type,
            StartX = Units(p.X),
            StartY = SimY(p.Y),
            EndX = Units(p.MoveToX),
            EndY = SimY(p.MoveToY),
            Width = Units(p.Width),
            Height = Units(p.Height),
            MoveDuration = p.MoveFrames,
            BounceForce = Fp.FromRatio(p.Strength != 0 ? p.Strength : DefaultBounceSpeedPercent, 100),
            ConveyorSpeed = Fp.FromRatio(p.Strength != 0 ? p.Strength : DefaultConveyorSpeedPermille, 1000),
        };
    }

    private static PlatformSystem.PlatformData ConvertHazard(HazardData h) => new()
    {
        Type = PlatformSystem.PlatformType.Hazard,
        StartX = Units(h.X),
        StartY = SimY(h.Y),
        Width = Units(h.Width),
        Height = Units(h.Height),
        Damage = h.Damage,
    };
}
