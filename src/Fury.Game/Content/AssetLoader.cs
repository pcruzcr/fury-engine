using Godot;
using FileAccess = Godot.FileAccess;

namespace Fury.Game.Content;

/// <summary>
/// Loads art and audio for content packs from either place they can live: <c>res://</c> (shipped with the game, imported by the
/// editor) or <c>user://</c> (mods and work-in-progress art dropped next to the game, never imported). Results are cached;
/// a missing or unreadable file yields null and a single warning instead of an exception.
/// </summary>
public static class AssetLoader
{
    private static readonly Dictionary<string, Texture2D?> Textures = [];
    private static readonly Dictionary<string, AudioStream?> Sounds = [];
    private static readonly HashSet<string> Warned = [];

    /// <summary>Forgets cached assets (after the content was reloaded or art was edited).</summary>
    public static void ClearCache()
    {
        Textures.Clear();
        Sounds.Clear();
        Warned.Clear();
    }

    public static bool Exists(string path) => ResourceLoader.Exists(path) || FileAccess.FileExists(path);

    /// <summary>A texture from an imported resource, or straight from a PNG / WebP / JPG file.</summary>
    public static Texture2D? LoadTexture(string path)
    {
        if (Textures.TryGetValue(path, out var cached)) return cached;

        Texture2D? texture = null;
        if (ResourceLoader.Exists(path)) texture = ResourceLoader.Load<Texture2D>(path);
        if (texture == null && FileAccess.FileExists(path))
        {
            var image = LoadImageFile(path);
            if (image != null) texture = ImageTexture.CreateFromImage(image);
        }

        if (texture == null) WarnOnce(path, "texture not found or unreadable");
        Textures[path] = texture;
        return texture;
    }

    /// <summary>
    /// Reads an image file directly (no editor import). Used for mods in <c>user://</c> and, during development, for art in
    /// <c>res://</c> that the editor has not imported yet; the path is globalised so Godot does not warn about "export".
    /// </summary>
    public static Image? LoadImageFile(string path)
    {
        string file = path.StartsWith("res://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
        if (!FileAccess.FileExists(path)) return null;
        var image = Image.LoadFromFile(file);
        return image == null || image.IsEmpty() ? null : image;
    }

    /// <summary>An audio stream from an imported resource, or from an .ogg / .mp3 file (WAV needs the editor import).</summary>
    public static AudioStream? LoadAudio(string path)
    {
        if (Sounds.TryGetValue(path, out var cached)) return cached;

        AudioStream? stream = null;
        if (ResourceLoader.Exists(path)) stream = ResourceLoader.Load<AudioStream>(path);
        if (stream == null && FileAccess.FileExists(path))
        {
            if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) stream = AudioStreamOggVorbis.LoadFromFile(path);
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) stream = AudioStreamMP3.LoadFromFile(path);
        }

        if (stream == null) WarnOnce(path, "audio not found or unreadable");
        Sounds[path] = stream;
        return stream;
    }

    /// <summary>A packed scene (<c>.tscn</c> / <c>.scn</c> / glTF import).</summary>
    public static PackedScene? LoadScene(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            WarnOnce(path, "scene not found");
            return null;
        }
        return ResourceLoader.Load<PackedScene>(path);
    }

    /// <summary>Raw bytes of a file in either location; null when it does not exist.</summary>
    public static byte[]? ReadBytes(string path) => FileAccess.FileExists(path) ? FileAccess.GetFileAsBytes(path) : null;

    private static void WarnOnce(string path, string why)
    {
        if (Warned.Add(path)) GD.PushWarning($"{path}: {why}.");
    }
}
