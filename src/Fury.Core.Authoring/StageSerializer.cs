using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fury.Core.Authoring;

internal sealed record ColorDto(float R, float G, float B, float A);

internal sealed record StageDto(
    string Name,
    int Width,
    int Height,
    float Gravity,
    ColorDto BackgroundColor,
    List<PlatformData> Platforms,
    List<HazardData> Hazards)
{
    // Added after the first stage format: older files lack them and keep the defaults.
    public List<BackgroundLayer>? Layers { get; init; }

    public string? Music { get; init; }

    public bool? DefaultFloor { get; init; }

    public bool? PixelArt { get; init; }
}

/// <summary>Source-generated JSON metadata: no reflection, trimming- and AOT-safe (iOS / consoles).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StageDto))]
internal sealed partial class StageJsonContext : JsonSerializerContext;

/// <summary>
/// Pure (engine-free) persistence for <see cref="StageData"/>: JSON round-trip and safe file
/// names. Kept separate from the stage editor UI so it can be unit-tested without a
/// running Godot scene.
/// </summary>
public static class StageSerializer
{
    public const string Extension = ".stage";

    public static string ToJson(StageData stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var c = stage.BackgroundColor;
        var dto = new StageDto(stage.Name, stage.Width, stage.Height, stage.Gravity,
            new ColorDto(c.R, c.G, c.B, c.A), stage.Platforms, stage.Hazards)
        {
            Layers = stage.Layers,
            Music = stage.Music,
            DefaultFloor = stage.DefaultFloor,
            PixelArt = stage.PixelArt,
        };
        return JsonSerializer.Serialize(dto, StageJsonContext.Default.StageDto);
    }

    /// <exception cref="InvalidDataException">Malformed or incomplete stage file.</exception>
    public static StageData FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        StageDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, StageJsonContext.Default.StageDto);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Stage file is not valid JSON: {e.Message}", e);
        }
        if (dto is null || dto.BackgroundColor is null)
        {
            throw new InvalidDataException("Stage file is empty or incomplete.");
        }
        var stage = new StageData
        {
            Name = dto.Name ?? "",
            Width = dto.Width,
            Height = dto.Height,
            Gravity = dto.Gravity,
            BackgroundColor = new ColorRgba(dto.BackgroundColor.R, dto.BackgroundColor.G, dto.BackgroundColor.B, dto.BackgroundColor.A),
            Platforms = dto.Platforms ?? new List<PlatformData>(),
            Hazards = dto.Hazards ?? new List<HazardData>(),
            Layers = dto.Layers ?? new List<BackgroundLayer>(),
            Music = dto.Music ?? "",
            DefaultFloor = dto.DefaultFloor ?? true,
            PixelArt = dto.PixelArt ?? false,
        };
        stage.Layers.RemoveAll(l => l is null);
        foreach (var layer in stage.Layers) layer.Image ??= "";
        stage.Validate();
        return stage;
    }

    /// <summary>
    /// File name for a stage, built only from letters, digits, '-' and '_'. The stage name
    /// is user text: using it raw in Path.Combine allowed "../../x" to escape the folder.
    /// </summary>
    public static string SafeFileName(string stageName)
    {
        var chars = (stageName ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        var name = new string(chars).Trim('_');
        return (name.Length == 0 ? "stage" : name) + Extension;
    }
}
