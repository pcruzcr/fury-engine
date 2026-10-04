using Fury.Core.Authoring;
using Fury.Core.Math;
using Fury.Core.Sim;
using Fury.Game.Presentation;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Fury.Game.Stages;

/// <summary>A stage ready to fight on: arena rules, geometry module, background colour and the art to draw.</summary>
public sealed record PlayableStage(string Name, FightConfig Config, Func<StageModule?> CreateModule, Color Background, StageArtSet Art);

/// <summary>A stage document together with the folder its art and music are relative to.</summary>
public sealed record LoadedStage(StageData Data, string Folder);

/// <summary>
/// Turns stage documents (pixels, Y up) into what the simulation understands (simulation units, Y down, integer math only).
/// Stages come from <c>user://stages</c> (the editor's output and mods) and <c>res://stages</c> (shipped packs). A stage is either
/// <c>name.stage</c> next to its art, or <c>name/stage.stage</c> with its art inside the folder. Documents are validated on load.
/// Moving platforms are solid surfaces that travel between two points; every other platform kind keeps its own behaviour.
/// </summary>
public static class StageLoader
{
    public const string Folder = "user://stages";
    public const string ShippedFolder = "res://stages";
    public const string PackFile = "stage";

    /// <summary>Pixels per simulation unit: the same scale the presentation uses.</summary>
    public const int PixelsPerUnit = (int)Presentation.WorldProjection.PixelsPerUnit;

    private const int DefaultBounceSpeedPercent = 70;   // 0.70 units per frame (about the jump speed)
    private const int DefaultConveyorSpeedPermille = 50; // 0.05 units per frame

    private static readonly string[] Roots = [Folder, ShippedFolder];

    /// <summary>Names of the stages that can be loaded (file or folder names without extension), sorted and without duplicates.</summary>
    public static IReadOnlyList<string> List()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in Roots)
        {
            if (!DirAccess.DirExistsAbsolute(root)) continue; // nothing installed there yet: not an error

            foreach (string file in DirAccess.GetFilesAt(root))
            {
                if (file.EndsWith(StageSerializer.Extension, StringComparison.OrdinalIgnoreCase)) names.Add(file[..^StageSerializer.Extension.Length]);
            }
            foreach (string directory in DirAccess.GetDirectoriesAt(root))
            {
                if (directory.StartsWith('_') && !Content.ContentLibrary.ShowTemplates) continue;
                if (FileAccess.FileExists($"{root}/{directory}/{PackFile}{StageSerializer.Extension}")) names.Add(directory);
            }
        }
        return [.. names];
    }

    /// <summary>The file a stage name refers to, searching the editor folder before the shipped one; null when there is none.</summary>
    public static string? Find(string name)
    {
        // Not SafeFileName: that trims leading '_', which would hide the '_template' pack. Only path-escaping characters matter here.
        string safe = new string((name ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        if (safe.Length == 0) return null;
        foreach (string root in Roots)
        {
            string flat = $"{root}/{safe}{StageSerializer.Extension}";
            if (FileAccess.FileExists(flat)) return flat;
            string pack = $"{root}/{safe}/{PackFile}{StageSerializer.Extension}";
            if (FileAccess.FileExists(pack)) return pack;
        }
        return null;
    }

    /// <summary>Loads and validates a stage; null (with a warning) when it is missing or invalid.</summary>
    public static LoadedStage? TryLoad(string name)
    {
        string? path = Find(name);
        if (path == null) return null;

        try
        {
            var data = StageSerializer.FromJson(FileAccess.GetFileAsString(path));
            return new LoadedStage(data, path[..path.LastIndexOf('/')]);
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"Stage '{name}' ignored: {e.Message}");
            return null;
        }
    }

    /// <summary>Builds a playable stage from a document on top of the base rules.</summary>
    public static PlayableStage Build(LoadedStage loaded, FightConfig baseConfig)
    {
        var stage = loaded.Data;
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
        var art = new StageArtSet(loaded.Folder, [.. stage.Layers], stage.DefaultFloor, stage.PixelArt, stage.Music);
        return new PlayableStage(
            stage.Name,
            config,
            () => platforms.Count == 0 ? null : new StageModule(platforms),
            new Color(bg.R, bg.G, bg.B, 1f),
            art);
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
