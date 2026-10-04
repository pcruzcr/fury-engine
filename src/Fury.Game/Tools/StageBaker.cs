using Fury.Core.Authoring;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Scaffold generator for stage artists: writes a working stage pack (a <c>stage.stage</c> plus placeholder art layers with sensible
/// sizes, depths and anchors) that the artist then repaints. It only manipulates images in memory, so it also runs headless:
/// <c>godot --headless --path . res://scenes/tools/stage_baker.tscn -- --out=C:/art/dojo --name=Dojo</c>.
/// </summary>
public partial class StageBaker : Node
{
    public override void _Ready()
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 2) args[arg[2..eq]] = arg[(eq + 1)..];
        }

        if (!args.TryGetValue("out", out string? outDir) || string.IsNullOrWhiteSpace(outDir))
        {
            GD.PrintErr("usage: stage_baker.tscn -- --out=<folder> [--name=Stage name]");
            GetTree().Quit(2);
            return;
        }

        try
        {
            Directory.CreateDirectory(outDir);
            Save(Sky(2800, 1100), outDir, "sky.png");
            Save(Silhouette(1024, 340, new Color(0.22f, 0.15f, 0.33f), 150, [(3, 60, 0f), (5, 40, 1f), (9, 18, 2f)]), outDir, "mountains.png");
            Save(Silhouette(1024, 200, new Color(0.14f, 0.1f, 0.22f), 90, [(2, 40, 0.5f), (4, 25, 2f), (8, 10, 1f)]), outDir, "hills.png");
            Save(Floor(512, 110), outDir, "floor.png");
            Save(Posts(512, 150), outDir, "foreground.png");

            var stage = new StageData
            {
                Name = args.GetValueOrDefault("name", "Template"),
                Width = 2400,
                Height = 900,
                Gravity = 1f,
                BackgroundColor = new ColorRgba(0.08f, 0.06f, 0.16f),
                DefaultFloor = false,
                Layers =
                [
                    new BackgroundLayer { Image = "sky.png", Depth = 1, Repeat = true },
                    new BackgroundLayer { Image = "mountains.png", Depth = 0.75, Repeat = true, OffsetY = 60 },
                    new BackgroundLayer { Image = "hills.png", Depth = 0.4, Repeat = true },
                    new BackgroundLayer { Image = "floor.png", Depth = 0, Repeat = true, OffsetY = -110 },
                    new BackgroundLayer { Image = "foreground.png", Depth = 0, Repeat = true, OffsetY = -60, Foreground = true },
                ],
            };
            stage.Validate();
            File.WriteAllText(Path.Combine(outDir, $"{Stages.StageLoader.PackFile}{StageSerializer.Extension}"), StageSerializer.ToJson(stage));
            GD.Print($"Stage pack written to {outDir}");
            GetTree().Quit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PrintErr($"Could not write the stage pack: {e.Message}");
            GetTree().Quit(1);
        }
    }


    private static void Save(Image image, string folder, string name)
    {
        var error = image.SavePng(Path.Combine(folder, name));
        if (error != Error.Ok) throw new IOException($"{name}: {error}");
        GD.Print($"wrote {name} ({image.GetWidth()}x{image.GetHeight()})");
    }

    private static Image Sky(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var top = new Color(0.05f, 0.06f, 0.2f);
        var bottom = new Color(0.62f, 0.3f, 0.35f);
        for (int y = 0; y < height; y++)
        {
            var row = top.Lerp(bottom, Mathf.Pow(y / (float)(height - 1), 1.6f));
            image.FillRect(new Rect2I(0, y, width, 1), row);
        }

        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 160; i++)
        {
            int x = rng.RandiRange(0, width - 1);
            int y = rng.RandiRange(0, height / 2);
            float a = rng.RandfRange(0.4f, 1f);
            image.SetPixel(x, y, new Color(1f, 1f, 0.9f, a));
        }
        return image;
    }

    /// <summary>A tileable silhouette: sums of sines whose periods divide the width, so the right edge meets the left edge.</summary>
    private static Image Silhouette(int width, int height, Color color, int baseHeight, (int Cycles, int Amplitude, float Phase)[] waves)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (int x = 0; x < width; x++)
        {
            float top = baseHeight;
            foreach (var (cycles, amplitude, phase) in waves) top += amplitude * Mathf.Sin((Mathf.Tau * cycles * x / width) + phase);
            int h = Mathf.Clamp((int)top, 4, height);
            image.FillRect(new Rect2I(x, height - h, 1, h), color);
        }
        return image;
    }

    private static Image Floor(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0.16f, 0.12f, 0.15f));
        image.FillRect(new Rect2I(0, 0, width, 4), new Color(0.85f, 0.7f, 0.45f)); // the floor line the fighters stand on
        for (int x = 0; x < width; x += 128) image.FillRect(new Rect2I(x, 4, 2, height - 4), new Color(0.1f, 0.07f, 0.1f));
        for (int y = 36; y < height; y += 36) image.FillRect(new Rect2I(0, y, width, 1), new Color(0.12f, 0.09f, 0.12f));
        return image;
    }

    private static Image Posts(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (int x = 40; x < width; x += 256) image.FillRect(new Rect2I(x, 0, 18, height), new Color(0.05f, 0.04f, 0.07f, 0.9f));
        return image;
    }
}
