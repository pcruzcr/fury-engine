namespace Fury.Core.Authoring;

/// <summary>
/// The sound vocabulary an artist can override. A key names a moment of the fight; the engine looks it up in the character
/// pack first (<c>audio.sfx</c>), then in the shared <c>audio/sfx</c> folders, and only then falls back to its built-in tone, so a
/// game can ship with none, some or all of them.
/// </summary>
public static class AudioKeys
{
    /// <summary>Sounds with a built-in fallback tone.</summary>
    public static IReadOnlyList<string> Global { get; } =
    [
        "hit_light", "hit_heavy", "block", "ko", "rage", "round_start", "ui_move", "ui_confirm",
        "whoosh", "jump", "dash", "projectile", "knockdown", "win",
    ];

    /// <summary>Character-only sounds with no fallback: voices stay silent until someone records them.</summary>
    public static IReadOnlyList<string> Voices { get; } = ["voice_hurt", "voice_ko", "voice_win"];

    public const string MovePrefixWhoosh = "whoosh_";
    public const string MovePrefixVoice = "voice_";

    /// <summary>Whether the engine can ever ask for <paramref name="key"/> for this character (global, voice, or per-move).</summary>
    public static bool IsKnown(string key, CharacterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (Global.Contains(key) || Voices.Contains(key)) return true;
        foreach (string prefix in new[] { MovePrefixWhoosh, MovePrefixVoice })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && manifest.IsKnownAnimation(key[prefix.Length..])) return true;
        }
        return false;
    }
}
