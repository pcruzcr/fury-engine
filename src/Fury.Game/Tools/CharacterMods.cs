using Fury.Core.Data;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Loads character move tables saved by the frame data editor (<c>user://characters/&lt;id&gt;.fmt2</c>) and registers them as
/// overrides of the built-in roster, so edits become playable without recompiling. A broken file is skipped with a
/// warning (the built-in table stays); the parser validates magic, version and exact length.
/// </summary>
public static class CharacterMods
{
    public const string Folder = "user://characters";
    public const string Extension = ".fmt2";

    /// <summary>Applies every valid override found on disk. Returns how many were loaded.</summary>
    public static int LoadAll()
    {
        Content.ContentLibrary.EnsureLoaded(); // packs first, so their ids exist when overrides are matched below
        int loaded = 0;
        string directory = ProjectSettings.GlobalizePath(Folder);
        foreach (string id in CharacterRoster.Ids)
        {
            string path = Path.Combine(directory, id + Extension);
            if (!File.Exists(path))
            {
                CharacterRoster.SetMoveTableOverride(id, null);
                continue;
            }

            try
            {
                CharacterRoster.SetMoveTableOverride(id, BinaryMoveTable.Parse(File.ReadAllBytes(path)));
                loaded++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                GD.PushWarning($"Ignoring character mod {path}: {e.Message}");
                CharacterRoster.SetMoveTableOverride(id, null);
            }
        }
        return loaded;
    }

    public static void Remove(string id) => CharacterRoster.SetMoveTableOverride(id, null);
}
