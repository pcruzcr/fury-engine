using System.Globalization;
using Fury.Core.Authoring;
using Godot;

namespace Fury.Game.Content;

/// <summary>
/// Interface languages as data. Every <c>i18n/&lt;code&gt;.json</c> in <c>res://i18n</c> or <c>user://i18n</c> (mods) is a flat map from
/// the English text of the game to its translation (see <see cref="LocalizationTable"/>). Godot translates the text of labels,
/// buttons and menu items by itself when it matches a key, so most of the interface needs no code; texts built at runtime go
/// through <see cref="T"/> / <see cref="F"/>. A text without a translation shows in English, never as an empty string or a key.
/// </summary>
public static class Localization
{
    private static readonly string[] Roots = ["res://i18n", "user://i18n"];
    private static readonly List<LocalizationTable> Tables = [];
    private static readonly List<Translation> Registered = [];
    private static bool _loaded;

    /// <summary>Findings of the last load (warnings about lost placeholders, errors for unusable files).</summary>
    public static ContentReport Report { get; private set; } = new();

    /// <summary>The languages found on disk (English, the source language, is always available and not listed).</summary>
    public static IReadOnlyList<LocalizationTable> Languages
    {
        get
        {
            EnsureLoaded();
            return Tables;
        }
    }

    public static void EnsureLoaded()
    {
        if (!_loaded) Reload();
    }

    public static void Reload()
    {
        _loaded = true;
        foreach (var translation in Registered) TranslationServer.RemoveTranslation(translation);
        Registered.Clear();
        Tables.Clear();
        Report = new ContentReport();

        foreach (string root in Roots)
        {
            if (!DirAccess.DirExistsAbsolute(root)) continue;
            var files = DirAccess.GetFilesAt(root);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string code = file[..^5];
                if (Tables.Any(t => t.Code == code)) continue; // shipped languages win over a mod with the same code

                var (table, report) = LocalizationTable.Parse(code, Godot.FileAccess.GetFileAsString($"{root}/{file}"));
                Report.AddRange(report.Issues);
                if (table == null) continue;

                var translation = new Translation { Locale = code };
                foreach (var (key, value) in table.Messages) translation.AddMessage(key, value);
                TranslationServer.AddTranslation(translation);
                Registered.Add(translation);
                Tables.Add(table);
            }
        }

        foreach (var issue in Report.Issues.Where(i => i.Severity == IssueSeverity.Error)) GD.PushWarning(issue.ToString());
    }

    /// <summary>
    /// Applies a language setting: <c>""</c> follows the system language when a translation for it exists, <c>"en"</c> is English,
    /// anything else is a locale code from <see cref="Languages"/>. Unknown codes fall back to English.
    /// </summary>
    public static void Apply(string setting)
    {
        _applied = true;
        EnsureLoaded();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--fury-language=", StringComparison.Ordinal)) setting = arg[16..]; // dev / CI override
        }

        string locale = setting.Length == 0 ? FromSystem() : setting;
        if (locale != "en" && !Tables.Any(t => Matches(t.Code, locale))) locale = "en";
        TranslationServer.SetLocale(locale);
    }

    private static bool _applied;

    /// <summary>Applies the saved language the first time a scene needs it (scenes launched directly, tools, tests).</summary>
    public static void EnsureApplied()
    {
        if (_applied) return;
        GameSettings.Load();
        Apply(GameSettings.Language);
    }

    /// <summary>Translates a text built in code (the English text is the key).</summary>
    public static string T(string text) => TranslationServer.Translate(text);

    /// <summary>Translates a template and fills its <c>{0}</c>, <c>{1}</c> placeholders.</summary>
    public static string F(string template, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(template), args);

    private static string FromSystem()
    {
        string system = OS.GetLocale();
        foreach (var table in Tables)
        {
            if (Matches(table.Code, system)) return table.Code;
        }
        return "en";
    }

    /// <summary>"es" matches "es_ES", "pt_BR" matches only itself and "pt".</summary>
    private static bool Matches(string code, string locale) =>
        string.Equals(code, locale, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, locale.Split('_', '-')[0], StringComparison.OrdinalIgnoreCase);
}
