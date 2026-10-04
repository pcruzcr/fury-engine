using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fury.Core.Authoring;

/// <summary>Movement and health stats a pack can override. Zero keeps the engine's derived default (see <c>CharacterConfig.Effective*</c>).</summary>
public sealed class CharacterStats
{
    /// <summary>Hit points (the rules default is 1000).</summary>
    public int MaxHealth { get; set; } = 1000;

    /// <summary>Forward walk speed in simulation units per frame (a fighter is 4 units tall; 0.11 is brisk).</summary>
    public double WalkForward { get; set; } = 0.1;

    public double WalkBack { get; set; }

    public double DashSpeed { get; set; }

    public int DashFrames { get; set; }

    public double BackdashSpeed { get; set; }

    public int BackdashFrames { get; set; }

    public int BackdashInvulnFrames { get; set; }
}

/// <summary>Colours used by the procedural fighter and UI accents; <c>#rrggbb</c> or <c>#rrggbbaa</c>.</summary>
public sealed class CharacterColors
{
    public string Primary { get; set; } = "#4080ff";

    public string Accent { get; set; } = "";
}

/// <summary>One strip of frames laid out left to right in a single image.</summary>
public sealed class SpriteAnimationDef
{
    /// <summary>Image file inside the pack, e.g. <c>sprites/idle.png</c>. Width = frames x the sheet's frame width.</summary>
    public string File { get; set; } = "";

    public int Frames { get; set; } = 1;

    /// <summary>Playback speed for looping and natural-speed animations. Attacks are stretched over their frame data instead.</summary>
    public double Fps { get; set; } = 12;

    /// <summary>Loop with the global clock. Omitted = by name (idle and walks loop, the rest plays once and holds).</summary>
    public bool? Loop { get; set; }
}

/// <summary>Sprite-strip art: every animation is a strip of equally sized frames; pivot is where the feet centre sits in a frame.</summary>
public sealed class SpriteSheetDef
{
    public int FrameWidth { get; set; } = 256;

    public int FrameHeight { get; set; } = 256;

    /// <summary>Pixel inside a frame (from its top-left) that is placed on the fighter's floor position.</summary>
    public double PivotX { get; set; } = 128;

    public double PivotY { get; set; } = 240;

    public Dictionary<string, SpriteAnimationDef> Animations { get; set; } = [];
}

/// <summary>Everything about how the character looks. None of it affects the simulation, so peers may have different art.</summary>
public sealed class CharacterVisual
{
    /// <summary>Multiplier on the art's size (1 = one art pixel is one screen pixel at the 48 px / unit reference).</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Nearest-neighbour filtering for pixel art; off = smooth.</summary>
    public bool PixelArt { get; set; }

    /// <summary>Sprite strips (used by the 2D view and as billboards in 2.5D / 3D when there is no 3D scene).</summary>
    public SpriteSheetDef? Sprites { get; set; }

    /// <summary>2D scene with an <c>AnimationPlayer</c> (file inside the pack); wins over <see cref="Sprites"/> in the 2D view.</summary>
    public string Scene2D { get; set; } = "";

    /// <summary>3D scene with an <c>AnimationPlayer</c> (a glTF import or a hand-built scene); wins over sprites in the 3D views.</summary>
    public string Scene3D { get; set; } = "";
}

/// <summary>Sound overrides: a logical sound name (see the art guide) to a file inside the pack.</summary>
public sealed class CharacterAudio
{
    public Dictionary<string, string> Sfx { get; set; } = [];
}

/// <summary>
/// <c>character.json</c>: a character as data. The simulation part (stats and the move table) is covered by the character
/// fingerprint that online play and replays compare; the rest (names, colours, art, sounds) is presentation and never
/// enters it. Hand-written JSON may contain comments and trailing commas.
/// </summary>
public sealed class CharacterManifest
{
    public const int CurrentSchema = 1;
    public const string FileName = "character.json";
    public const string DefaultMovesFile = "moves.fmt2";
    public const int MaxMoveNames = 64;

    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]{1,23}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int Schema { get; set; } = CurrentSchema;

    /// <summary>Lowercase id (letters, digits, underscore; 2-24 characters). It is what online play, replays and saves refer to.</summary>
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Copy the move table of this character (a built-in or an already loaded pack) when the pack has no <see cref="Moves"/> file.</summary>
    public string BaseOn { get; set; } = "";

    /// <summary>Move table (FMT2) inside the pack, as saved by the frame data editor.</summary>
    public string Moves { get; set; } = "";

    public CharacterStats Stats { get; set; } = new();

    /// <summary>Optional names by move index; animations, sounds and the validator refer to moves by these.</summary>
    public List<string> MoveNames { get; set; } = [];

    /// <summary>Character-select portrait (png / webp / jpg inside the pack).</summary>
    public string Portrait { get; set; } = "";

    public CharacterColors Colors { get; set; } = new();

    public CharacterVisual Visual { get; set; } = new();

    public CharacterAudio Audio { get; set; } = new();

    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);

    /// <summary>Walk speed and friends as the exact fixed-point values the simulation will use (milli-units per frame, rounded).</summary>
    public static int ToMilli(double unitsPerFrame) => (int)System.Math.Round(unitsPerFrame * 1000.0, MidpointRounding.AwayFromZero);

    /// <summary>Parses <c>#rrggbb</c> / <c>#rrggbbaa</c> (the leading '#' is optional).</summary>
    public static bool TryParseColor(string? text, out ColorRgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        ReadOnlySpan<char> hex = text.AsSpan().Trim();
        if (hex.Length > 0 && hex[0] == '#') hex = hex[1..];
        if (hex.Length is not (6 or 8)) return false;
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value)) return false;
        if (hex.Length == 6) value = (value << 8) | 0xFF;
        color = new ColorRgba(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }

    /// <summary>Checks the manifest on its own (no files, no move table). File contents are checked by the loader and the Godot validator.</summary>
    public ContentReport Validate(string where = FileName)
    {
        var report = new ContentReport();
        ValidateInto(report, where);
        return report;
    }

    /// <summary>
    /// Validates only what a built-in character can customise (names, colours, art, sounds): its stats and move table live in
    /// the engine, so the simulation fields of such a manifest are ignored.
    /// </summary>
    public ContentReport ValidatePresentation(string where = FileName)
    {
        var report = new ContentReport();
        ValidateInto(report, where, presentationOnly: true);
        return report;
    }

    internal void ValidateInto(ContentReport report, string where, bool presentationOnly = false)
    {
        if (Schema != CurrentSchema) report.Error(where, $"schema must be {CurrentSchema} (got {Schema}).");
        if (!IsValidId(Id)) report.Error(where, "id must be 2-24 lowercase letters, digits or '_', starting with a letter.");
        if (DisplayName.Length > 32) report.Error(where, "displayName is limited to 32 characters.");

        if (!presentationOnly)
        {
            if (string.IsNullOrEmpty(Moves) && string.IsNullOrEmpty(BaseOn)) report.Error(where, "set either 'moves' (a .fmt2 file) or 'baseOn' (a character to copy the moves from).");
            if (!string.IsNullOrEmpty(BaseOn) && !IsValidId(BaseOn)) report.Error(where, "baseOn is not a valid character id.");
            CheckPath(report, where, "moves", Moves, ".fmt2");
            ValidateStats(report, where);
        }

        CheckPath(report, where, "portrait", Portrait, ".png", ".webp", ".jpg", ".jpeg");
        ValidateMoveNames(report, where);

        if (!TryParseColor(Colors.Primary, out _)) report.Error(where, "colors.primary must look like #rrggbb.");
        if (!string.IsNullOrEmpty(Colors.Accent) && !TryParseColor(Colors.Accent, out _)) report.Error(where, "colors.accent must look like #rrggbb.");

        ValidateVisual(report, where);

        foreach (var (key, path) in Audio.Sfx)
        {
            if (!AnimationNames.IsValidName(key)) report.Error(where, $"audio.sfx key '{key}' must be lowercase letters, digits and '_'.");
            else if (!AudioKeys.IsKnown(key, this)) report.Warn(where, $"audio.sfx key '{key}' is not a sound the engine asks for: it will never play.");
            CheckPath(report, where, $"audio.sfx.{key}", path, ".ogg", ".wav", ".mp3");
        }
    }

    private void ValidateStats(ContentReport report, string where)
    {
        static bool Between(double v, double lo, double hi) => double.IsFinite(v) && v >= lo && v <= hi;

        if (Stats.MaxHealth is < 100 or > 10000) report.Error(where, "stats.maxHealth must be 100..10000.");
        if (!Between(Stats.WalkForward, 0.01, 1.0)) report.Error(where, "stats.walkForward must be 0.01..1 units per frame.");
        foreach (var (name, value) in new[] { ("walkBack", Stats.WalkBack), ("dashSpeed", Stats.DashSpeed), ("backdashSpeed", Stats.BackdashSpeed) })
        {
            if (!Between(value, 0, 2.0)) report.Error(where, $"stats.{name} must be 0 (derived) or up to 2 units per frame.");
        }
        foreach (var (name, value) in new[] { ("dashFrames", Stats.DashFrames), ("backdashFrames", Stats.BackdashFrames), ("backdashInvulnFrames", Stats.BackdashInvulnFrames) })
        {
            if (value is < 0 or > 120) report.Error(where, $"stats.{name} must be 0 (derived) or up to 120 frames.");
        }
    }

    private void ValidateMoveNames(ContentReport report, string where)
    {
        if (MoveNames.Count > MaxMoveNames) report.Error(where, $"moveNames holds at most {MaxMoveNames} entries.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < MoveNames.Count; i++)
        {
            string name = MoveNames[i];
            if (string.IsNullOrEmpty(name)) continue; // an empty entry keeps the default name of that slot
            if (!AnimationNames.IsValidName(name)) report.Error(where, $"moveNames[{i}] '{name}' must be lowercase letters, digits and '_'.");
            else if (!seen.Add(name)) report.Error(where, $"moveNames[{i}] '{name}' is used twice.");
            else if (AnimationNames.Standard.Contains(name)) report.Error(where, $"moveNames[{i}] '{name}' clashes with a standard animation name.");
        }
    }

    private void ValidateVisual(ContentReport report, string where)
    {
        var v = Visual;
        if (!double.IsFinite(v.Scale) || v.Scale is < 0.05 or > 8) report.Error(where, "visual.scale must be 0.05..8.");
        CheckPath(report, where, "visual.scene2D", v.Scene2D, ".tscn", ".scn");
        CheckPath(report, where, "visual.scene3D", v.Scene3D, ".tscn", ".scn", ".glb", ".gltf");

        var sheet = v.Sprites;
        if (sheet == null) return;

        if (sheet.FrameWidth is < 16 or > 2048 || sheet.FrameHeight is < 16 or > 2048) report.Error(where, "visual.sprites frame size must be 16..2048 pixels.");
        if (!(sheet.PivotX >= 0 && sheet.PivotX <= sheet.FrameWidth && sheet.PivotY >= 0 && sheet.PivotY <= sheet.FrameHeight)) report.Error(where, "visual.sprites pivot must lie inside the frame.");
        if (sheet.Animations.Count == 0) report.Error(where, "visual.sprites needs at least the 'idle' animation.");
        if (sheet.Animations.Count > 128) report.Error(where, "visual.sprites holds at most 128 animations.");

        foreach (var (name, anim) in sheet.Animations)
        {
            string at = $"{where} animation '{name}'";
            if (!AnimationNames.IsValidName(name)) report.Error(at, "name must be lowercase letters, digits and '_'.");
            else if (!IsKnownAnimation(name)) report.Warn(at, "is not a standard animation nor a move name: it will never play.");
            if (!ContentReport.IsSafeRelativePath(anim.File)) report.Error(at, "file must be a relative path like sprites/idle.png.");
            else CheckPath(report, at, "file", anim.File, ".png", ".webp");
            if (anim.Frames is < 1 or > 256) report.Error(at, "frames must be 1..256.");
            if (!double.IsFinite(anim.Fps) || anim.Fps is < 1 or > 60) report.Error(at, "fps must be 1..60.");
        }

        if (!sheet.Animations.ContainsKey(AnimationNames.Idle) && string.IsNullOrEmpty(v.Scene2D)) report.Error(where, "visual.sprites must define the 'idle' animation.");
        foreach (string name in AnimationNames.Recommended)
        {
            if (!sheet.Animations.ContainsKey(name)) report.Warn(where, $"no '{name}' animation: the fighter falls back to idle in that situation.");
        }
    }

    /// <summary>Whether the engine can ever ask for an animation called <paramref name="name"/> for this character.</summary>
    public bool IsKnownAnimation(string name)
    {
        if (AnimationNames.Standard.Contains(name)) return true;
        if (name.StartsWith("move_", StringComparison.Ordinal) && int.TryParse(name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out _)) return true;
        for (int i = 0; i < MaxMoveNames; i++)
        {
            if (AnimationNames.MoveName(MoveNames, i) == name) return true;
        }
        return false;
    }

    private static void CheckPath(ContentReport report, string where, string field, string path, params string[] extensions)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!ContentReport.IsSafeRelativePath(path))
        {
            report.Error(where, $"{field} must be a relative path inside the pack (no '..', no drive, forward slashes).");
            return;
        }
        if (!extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))) report.Error(where, $"{field} must end with {string.Join(" / ", extensions)}.");
    }
}

/// <summary>Source-generated JSON metadata: no reflection, trimming- and AOT-safe.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CharacterManifest))]
internal sealed partial class CharacterJsonContext : JsonSerializerContext;

/// <summary>Reads and writes <c>character.json</c>.</summary>
public static class CharacterManifestSerializer
{
    public static string ToJson(CharacterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, CharacterJsonContext.Default.CharacterManifest);
    }

    /// <exception cref="InvalidDataException">Not valid JSON, or not a character manifest.</exception>
    public static CharacterManifest FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var manifest = JsonSerializer.Deserialize(json, CharacterJsonContext.Default.CharacterManifest)
                ?? throw new InvalidDataException("character.json is empty.");
            // Hand-written files may say "stats": null or omit sections: normalise so the rest of the code never sees null.
            manifest.Id ??= "";
            manifest.DisplayName ??= "";
            manifest.BaseOn ??= "";
            manifest.Moves ??= "";
            manifest.Portrait ??= "";
            manifest.Stats ??= new CharacterStats();
            manifest.MoveNames ??= [];
            manifest.Colors ??= new CharacterColors();
            manifest.Colors.Primary ??= "#4080ff";
            manifest.Colors.Accent ??= "";
            manifest.Visual ??= new CharacterVisual();
            manifest.Visual.Scene2D ??= "";
            manifest.Visual.Scene3D ??= "";
            manifest.Audio ??= new CharacterAudio();
            manifest.Audio.Sfx ??= [];
            if (manifest.Visual.Sprites != null)
            {
                manifest.Visual.Sprites.Animations ??= [];
                foreach (string key in manifest.Visual.Sprites.Animations.Keys.ToList())
                {
                    var anim = manifest.Visual.Sprites.Animations[key] ??= new SpriteAnimationDef();
                    anim.File ??= "";
                }
            }
            return manifest;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"character.json is not valid: {e.Message}", e);
        }
    }
}
