using Fury.Core.Authoring;
using Fury.Game.Content;
using Fury.Game.Presentation;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Content validator for artists and CI: <c>godot --headless --path . res://scenes/tools/content_check.tscn [-- --fury-templates]</c>.
/// Loads every character pack, checks the manifest and then the files it points to (images exist and have exactly the sizes the
/// manifest promises, scenes have an <c>AnimationPlayer</c> with usable clip names, sounds load) and prints one line per
/// finding. The process exit code is 1 when anything is an error, 0 otherwise (warnings never fail).
/// </summary>
public partial class ContentCheck : Node
{
    public override void _Ready()
    {
        ContentLibrary.Reload();
        var report = new ContentReport();
        report.AddRange(ContentLibrary.Report.Issues);

        int checkedPacks = 0;
        foreach (var entry in ContentLibrary.Characters)
        {
            if (entry.Manifest == null || entry.Folder == null) continue;
            checkedPacks++;
            CheckFiles(entry, entry.Manifest, report);
        }

        Localization.Reload();
        report.AddRange(Localization.Report.Issues);

        int checkedStages = 0;
        foreach (string name in Stages.StageLoader.List())
        {
            checkedStages++;
            CheckStage(name, report);
        }

        foreach (var issue in report.Issues) GD.Print(issue.ToString());
        GD.Print($"{checkedPacks} character pack(s), {checkedStages} stage(s) and {Localization.Languages.Count} language(s) checked: {report.ErrorCount} error(s), {report.WarningCount} warning(s).");
        GetTree().Quit(report.HasErrors ? 1 : 0);
    }

    private static void CheckStage(string name, ContentReport report)
    {
        string? path = Stages.StageLoader.Find(name);
        if (path == null) return;
        string folder = path[..path.LastIndexOf('/')];
        string where = $"stage '{name}' ({path})";

        StageData stage;
        try
        {
            stage = StageSerializer.FromJson(Godot.FileAccess.GetFileAsString(path));
        }
        catch (InvalidDataException e)
        {
            report.Error(where, e.Message);
            return;
        }

        foreach (var layer in stage.Layers)
        {
            string at = $"{where} layer '{layer.Image}'";
            var image = AssetLoader.LoadImageFile($"{folder}/{layer.Image}");
            if (image == null)
            {
                report.Error(at, "image is missing or unreadable.");
                continue;
            }
            if (layer.Repeat && image.GetWidth() < 128) report.Warn(at, $"a repeating layer only {image.GetWidth()} px wide tiles very often; use a wider image.");
            if (!layer.Repeat && layer.Depth > 0 && image.GetWidth() < 1920) report.Warn(at, $"a non-repeating background layer only {image.GetWidth()} px wide may show its edges when the camera pans or zooms out.");
        }

        if (stage.Music.Length > 0 && AssetLoader.LoadAudio($"{folder}/{stage.Music}") == null) report.Error(where, $"music '{stage.Music}' is missing or unreadable.");
        if (stage.Layers.Count == 0 && stage.DefaultFloor && !name.StartsWith('_')) report.Warn(where, "no art layers: the stage uses the plain default look.");
    }

    private static void CheckFiles(CharacterEntry entry, CharacterManifest manifest, ContentReport report)
    {
        string where = entry.Folder!;
        var visual = manifest.Visual;

        if (visual.Sprites != null)
        {
            foreach (var (name, anim) in visual.Sprites.Animations)
            {
                string path = entry.Resolve(anim.File);
                string at = $"{where} animation '{name}'";
                if (!TryLoadImage(path, out var image))
                {
                    report.Error(at, $"image '{anim.File}' is missing or unreadable.");
                    continue;
                }
                ContentChecks.CheckStrip(report, at, visual.Sprites, anim, image.GetWidth(), image.GetHeight());
            }
        }

        if (entry.PortraitPath is { } portrait)
        {
            if (TryLoadImage(portrait, out var image)) ContentChecks.CheckPortrait(report, where, image.GetWidth(), image.GetHeight());
            else report.Error(where, $"portrait '{manifest.Portrait}' is missing or unreadable.");
        }

        CheckScene(entry, manifest, visual.Scene2D, "scene2D", report);
        CheckScene(entry, manifest, visual.Scene3D, "scene3D", report);

        foreach (var (key, file) in manifest.Audio.Sfx)
        {
            if (AssetLoader.LoadAudio(entry.Resolve(file)) == null) report.Error(where, $"sound '{key}' ({file}) is missing or unreadable.");
        }

        if (visual.Sprites == null && visual.Scene2D.Length == 0 && visual.Scene3D.Length == 0)
        {
            report.Warn(where, "no visual art defined: the character is drawn as the procedural fighter.");
        }
    }

    private static void CheckScene(CharacterEntry entry, CharacterManifest manifest, string file, string field, ContentReport report)
    {
        if (file.Length == 0) return;
        string where = $"{entry.Folder} {field}";

        var scene = AssetLoader.LoadScene(entry.Resolve(file));
        if (scene == null)
        {
            report.Error(where, $"scene '{file}' is missing or does not load.");
            return;
        }

        var instance = scene.Instantiate();
        try
        {
            var player = CharacterVisuals.FindPlayer(instance);
            if (player == null)
            {
                report.Error(where, "the scene has no AnimationPlayer.");
                return;
            }
            ContentChecks.CheckPlayerAnimations(report, where, manifest, player.GetAnimationList());
        }
        finally
        {
            instance.Free();
        }
    }

    private static bool TryLoadImage(string path, out Image image)
    {
        image = AssetLoader.LoadImageFile(path)!;
        return image != null;
    }
}
