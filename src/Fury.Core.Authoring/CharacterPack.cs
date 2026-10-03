using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Authoring;

/// <summary>A loaded character pack: its manifest (presentation data) and the simulation config built from it.</summary>
public sealed record CharacterPack(CharacterManifest Manifest, CharacterConfig Config);

/// <summary>What loading a pack produced: the pack when it is usable, and every finding either way.</summary>
public sealed record CharacterPackResult(CharacterPack? Pack, ContentReport Report);

/// <summary>
/// Turns <c>character.json</c> (+ its move table) into a <see cref="CharacterConfig"/>. Engine-free: the caller supplies file
/// access, so the same code runs in the game, in the headless validator and in unit tests.
/// </summary>
public static class CharacterPackLoader
{
    /// <param name="manifestJson">Contents of <c>character.json</c>.</param>
    /// <param name="readFile">Reads a file of the pack by relative path; null when it does not exist.</param>
    /// <param name="where">Label for messages (usually the pack folder).</param>
    public static CharacterPackResult Load(string manifestJson, Func<string, byte[]?> readFile, string where)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(readFile);
        var report = new ContentReport();

        CharacterManifest manifest;
        try
        {
            manifest = CharacterManifestSerializer.FromJson(manifestJson);
        }
        catch (InvalidDataException e)
        {
            report.Error(where, e.Message);
            return new CharacterPackResult(null, report);
        }

        manifest.ValidateInto(report, where);
        if (report.HasErrors) return new CharacterPackResult(null, report);

        var table = ResolveMoves(manifest, readFile, report, where);
        if (table == null) return new CharacterPackResult(null, report);

        if (manifest.MoveNames.Count > table.MoveCount)
        {
            report.Warn(where, $"moveNames lists {manifest.MoveNames.Count} names but the move table has {table.MoveCount} moves.");
        }

        var s = manifest.Stats;
        var config = new CharacterConfig(manifest.Id, s.MaxHealth, Milli(s.WalkForward), table)
        {
            WalkBackSpeed = Milli(s.WalkBack),
            DashSpeed = Milli(s.DashSpeed),
            DashFrames = s.DashFrames,
            BackdashSpeed = Milli(s.BackdashSpeed),
            BackdashFrames = s.BackdashFrames,
            BackdashInvulnFrames = s.BackdashInvulnFrames,
        };

        return new CharacterPackResult(new CharacterPack(manifest, config), report);
    }

    private static Fp Milli(double unitsPerFrame) => Fp.FromRatio(CharacterManifest.ToMilli(unitsPerFrame), 1000);

    private static BinaryMoveTable? ResolveMoves(CharacterManifest manifest, Func<string, byte[]?> readFile, ContentReport report, string where)
    {
        if (!string.IsNullOrEmpty(manifest.Moves))
        {
            byte[]? bytes = readFile(manifest.Moves);
            if (bytes == null)
            {
                report.Error(where, $"move table '{manifest.Moves}' was not found in the pack.");
                return null;
            }

            try
            {
                var table = MoveTableDocument.FromBytes(bytes);
                for (int i = 0; i < table.Count; i++) MoveTableDocument.Validate(table.Get(i));
                var parsed = BinaryMoveTable.Parse(bytes);
                if (parsed.MoveCount < MoveSlot.Count)
                {
                    report.Error(where, $"the move table needs at least {MoveSlot.Count} moves (the canonical slots), it has {parsed.MoveCount}.");
                    return null;
                }
                return parsed;
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException)
            {
                report.Error(where, $"move table '{manifest.Moves}' is invalid: {e.Message}");
                return null;
            }
        }

        if (string.Equals(manifest.BaseOn, manifest.Id, StringComparison.Ordinal))
        {
            report.Error(where, "baseOn cannot be the character itself.");
            return null;
        }

        try
        {
            return CharacterRoster.Create(manifest.BaseOn).Moves;
        }
        catch (ArgumentException)
        {
            report.Error(where, $"baseOn character '{manifest.BaseOn}' is not installed (load it first, or ship a moves file).");
            return null;
        }
    }
}
