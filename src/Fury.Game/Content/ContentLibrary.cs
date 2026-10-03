using Fury.Core.Authoring;
using Fury.Core.Data;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Fury.Game.Content;

/// <summary>One character the player can pick: a built-in or a content pack, with the presentation data of its manifest.</summary>
/// <param name="Folder">Pack folder (<c>res://characters/x</c> or <c>user://characters/x</c>); null for a built-in without a manifest.</param>
/// <param name="Manifest">The parsed manifest, or null for a built-in that ships no <c>character.json</c>.</param>
public sealed record CharacterEntry(string Id, string DisplayName, string? Folder, CharacterManifest? Manifest, bool IsBuiltIn)
{
    /// <summary>Main colour of the procedural fighter and UI accents.</summary>
    public Color Primary => Manifest != null && CharacterManifest.TryParseColor(Manifest.Colors.Primary, out var c)
        ? new Color(c.R, c.G, c.B, c.A)
        : DefaultPrimary(Id);

    public Color Accent => Manifest != null && CharacterManifest.TryParseColor(Manifest.Colors.Accent, out var c)
        ? new Color(c.R, c.G, c.B, c.A)
        : Primary.Lightened(0.55f);

    /// <summary>Full path of the portrait image, or null.</summary>
    public string? PortraitPath => Folder != null && Manifest is { Portrait.Length: > 0 } ? $"{Folder}/{Manifest.Portrait}" : null;

    /// <summary>Full path of a file inside the pack.</summary>
    public string Resolve(string relative) => $"{Folder}/{relative}";

    private static Color DefaultPrimary(string id) => id switch
    {
        "glen" => new Color(0.25f, 0.5f, 1f),
        "kira" => new Color(1f, 0.3f, 0.3f),
        _ => Color.FromHsv(((uint)id.GetHashCode() % 360) / 360f, 0.6f, 0.95f),
    };
}

/// <summary>
/// Everything an artist or modder can add without code. Scans <c>res://characters/*</c> (shipped) and <c>user://characters/*</c>
/// (mods) for folders holding a <c>character.json</c>, validates them, and registers each usable one in the
/// <see cref="CharacterRoster"/>. A folder whose name starts with '_' is a template and is skipped unless
/// <see cref="IncludeTemplates"/> is set. Problems never stop the game: a broken pack is skipped and listed in <see cref="Report"/>.
/// </summary>
public static class ContentLibrary
{
    public const string ShippedRoot = "res://characters";
    public const string UserRoot = "user://characters";

    private static readonly List<CharacterEntry> Entries = [];
    private static bool _loaded;

    /// <summary>Also load folders whose name starts with '_' (the shipped template and test packs).</summary>
    public static bool IncludeTemplates { get; set; }

    /// <summary>Everything found by the last load: errors and warnings per pack.</summary>
    public static ContentReport Report { get; private set; } = new();

    /// <summary>Characters in selection order: the built-ins, then packs (shipped first, then mods, each alphabetically).</summary>
    public static IReadOnlyList<CharacterEntry> Characters
    {
        get
        {
            EnsureLoaded();
            return Entries;
        }
    }

    public static void EnsureLoaded()
    {
        if (!_loaded) Reload();
    }

    /// <summary>Scans again (after the editor saved a pack or art changed).</summary>
    public static void Reload()
    {
        _loaded = true;
        if (OS.GetCmdlineUserArgs().Contains("--fury-templates")) IncludeTemplates = true; // dev / CI: load the '_' template packs too
        AssetLoader.ClearCache();
        CharacterRoster.ClearRegistered();
        Entries.Clear();
        Report = new ContentReport();

        var packs = new List<(string Folder, string Json)>();
        foreach (string root in new[] { ShippedRoot, UserRoot })
        {
            foreach (string name in SortedDirectories(root))
            {
                if (name.StartsWith('_') && !IncludeTemplates) continue;
                string folder = $"{root}/{name}";
                string manifestPath = $"{folder}/{CharacterManifest.FileName}";
                if (FileAccess.FileExists(manifestPath)) packs.Add((folder, FileAccess.GetFileAsString(manifestPath)));
            }
        }

        var builtInManifests = new Dictionary<string, (string Folder, CharacterManifest Manifest)>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string Folder, string Json)>();
        foreach (var pack in packs)
        {
            if (!TryAddBuiltInPresentation(pack, builtInManifests)) pending.Add(pack);
        }

        foreach (string id in CharacterRoster.Ids)
        {
            builtInManifests.TryGetValue(id, out var found);
            Entries.Add(new CharacterEntry(id, found.Manifest is { DisplayName.Length: > 0 } ? found.Manifest.DisplayName : id.ToUpperInvariant(), found.Folder, found.Manifest, IsBuiltIn: true));
        }

        LoadPacks(pending);
    }

    public static CharacterEntry? Find(string id) => Characters.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(string id) => Find(id)?.DisplayName ?? id.ToUpperInvariant();

    // ------------------------------------------------------------------ loading

    /// <summary>A manifest whose id is a built-in character customises only its presentation.</summary>
    private static bool TryAddBuiltInPresentation((string Folder, string Json) pack, Dictionary<string, (string, CharacterManifest)> found)
    {
        CharacterManifest manifest;
        try
        {
            manifest = CharacterManifestSerializer.FromJson(pack.Json);
        }
        catch (InvalidDataException)
        {
            return false; // reported by the normal path
        }

        if (!CharacterRoster.IsBuiltIn(manifest.Id)) return false;

        var report = manifest.ValidatePresentation(pack.Folder);
        Report.AddRange(report.Issues);
        if (report.HasErrors) return true;
        if (!found.TryAdd(manifest.Id, (pack.Folder, manifest))) Report.Warn(pack.Folder, $"presentation for built-in '{manifest.Id}' already provided by another folder; ignored.");
        return true;
    }

    /// <summary>Loads packs repeatedly so one can be based on another regardless of folder order.</summary>
    private static void LoadPacks(List<(string Folder, string Json)> pending)
    {
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastFailure = new Dictionary<string, ContentReport>();

        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var (folder, json) = pending[i];
                var result = CharacterPackLoader.Load(json, path => AssetLoader.ReadBytes($"{folder}/{path}"), folder);
                if (result.Pack == null)
                {
                    lastFailure[folder] = result.Report;
                    continue;
                }

                var pack = result.Pack;
                Report.AddRange(result.Report.Issues);
                pending.RemoveAt(i--);
                progress = true;

                if (!registered.Add(pack.Manifest.Id))
                {
                    Report.Error(folder, $"another pack already defines the character '{pack.Manifest.Id}'; this one was ignored.");
                    continue;
                }

                CharacterRoster.Register(pack.Config);
                Entries.Add(new CharacterEntry(
                    pack.Manifest.Id,
                    pack.Manifest.DisplayName.Length > 0 ? pack.Manifest.DisplayName : pack.Manifest.Id.ToUpperInvariant(),
                    folder,
                    pack.Manifest,
                    IsBuiltIn: false));
            }
        }

        foreach (var (folder, _) in pending)
        {
            if (lastFailure.TryGetValue(folder, out var failure)) Report.AddRange(failure.Issues);
            GD.PushWarning($"Character pack skipped: {folder}");
        }

        foreach (var issue in Report.Issues.Where(i => i.Severity == IssueSeverity.Error)) GD.PushWarning(issue.ToString());
    }

    private static IEnumerable<string> SortedDirectories(string root)
    {
        if (!DirAccess.DirExistsAbsolute(root)) return []; // no mods installed yet: not an error
        var names = DirAccess.GetDirectoriesAt(root);
        Array.Sort(names, StringComparer.Ordinal);
        return names;
    }
}
