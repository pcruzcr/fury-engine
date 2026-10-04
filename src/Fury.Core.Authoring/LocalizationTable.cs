using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fury.Core.Authoring;

/// <summary>
/// One language of the interface: a flat JSON file <c>i18n/&lt;code&gt;.json</c> mapping the English text of the game to its
/// translation, plus a <c>"_name"</c> entry with the language's own name ("Español"). The English text is the key, so the
/// game reads naturally in code and an untranslated text simply shows in English. Placeholders (<c>{0}</c>, <c>{1}</c>) must
/// appear in the translation exactly as in the key.
/// </summary>
public sealed class LocalizationTable
{
    public const string NameKey = "_name";

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CodePattern = new("^[a-z]{2,3}(_[A-Z]{2})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public LocalizationTable(string code, string name, IReadOnlyDictionary<string, string> messages)
    {
        Code = code;
        Name = name;
        Messages = messages;
    }

    /// <summary>Locale code such as <c>es</c> or <c>pt_BR</c> (the file name without extension).</summary>
    public string Code { get; }

    /// <summary>The language written in itself.</summary>
    public string Name { get; }

    public IReadOnlyDictionary<string, string> Messages { get; }

    public static bool IsValidCode(string? code) => code != null && CodePattern.IsMatch(code);

    /// <summary>Placeholder indexes used by a text, sorted ("{1} of {0}" gives 0, 1).</summary>
    public static IReadOnlyList<int> PlaceholdersOf(string text) =>
        Placeholder.Matches(text).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToList();

    /// <summary>Parses a language file; the table is null when the file cannot be used at all.</summary>
    public static (LocalizationTable? Table, ContentReport Report) Parse(string code, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var report = new ContentReport();
        string where = $"i18n/{code}.json";

        if (!IsValidCode(code))
        {
            report.Error(where, "the file name must be a locale code such as es or pt_BR.");
            return (null, report);
        }

        Dictionary<string, string?>? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, LocalizationJsonContext.Default.DictionaryStringString);
        }
        catch (JsonException e)
        {
            report.Error(where, $"not valid JSON (every value must be a string): {e.Message}");
            return (null, report);
        }

        if (raw == null)
        {
            report.Error(where, "the file is empty.");
            return (null, report);
        }

        string name = code;
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            if (value == null)
            {
                report.Warn(where, $"'{key}' has no translation (null): ignored.");
                continue;
            }
            if (key == NameKey)
            {
                name = value.Length > 0 ? value : code;
                continue;
            }
            if (value.Length == 0) continue; // an empty value means "not translated yet"

            if (!PlaceholdersOf(key).SequenceEqual(PlaceholdersOf(value)))
            {
                report.Warn(where, $"'{key}': the translation must contain the same placeholders as the original ({string.Join(", ", PlaceholdersOf(key).Select(i => "{" + i + "}"))}).");
            }
            messages[key] = value;
        }

        if (!raw.ContainsKey(NameKey)) report.Warn(where, $"no \"{NameKey}\" entry: the language will be listed by its code.");
        return (new LocalizationTable(code, name, messages), report);
    }
}

/// <summary>Source-generated JSON metadata (no reflection; trimming- and AOT-safe).</summary>
[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string?>), TypeInfoPropertyName = "DictionaryStringString")]
internal sealed partial class LocalizationJsonContext : JsonSerializerContext;
