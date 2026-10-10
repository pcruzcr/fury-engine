using System.Text.RegularExpressions;
using Xunit;

namespace Fury.Core.Tests.Docs;

/// <summary>
/// Keeps the documentation honest: what the docs cite (files, folders, command-line options, ADRs, the version) has to exist.
/// A failure here means a document drifted from the repository; fix the document, not the test.
/// </summary>
public class DocumentationTests
{
    // Folders at the repository root that documents refer to. Anything else (build/, user://, res://, C:/...) is not checked.
    private static readonly string[] TopLevel =
    {
        "src", "docs", "tools", "tests", "cert", "scenes", "characters", "stages", "i18n", "archive-reference", "gdd",
    };

    // Worked examples in docs/ART_PIPELINE.md ("copy it to characters/ryu"): they name a folder the reader creates.
    private static readonly string[] ExamplePaths = { "characters/ryu", "characters/glen" };

    private static readonly Regex Flag =new(@"--fury-[a-z][a-z-]*", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"```.*?```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineCode = new(@"`([^`\n]+)`", RegexOptions.Compiled);

    private static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FuryEngine.sln"))) return dir.FullName;
        }

        throw new InvalidOperationException("FuryEngine.sln not found above " + AppContext.BaseDirectory);
    }

    private static string Abs(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(Abs(relative));

    /// <summary>Normative documents: the archive is history and is allowed to cite what no longer exists.</summary>
    private static IEnumerable<string> LiveDocs()
    {
        yield return "README.md";
        foreach (var f in Directory.EnumerateFiles(Abs("docs"), "*.*", SearchOption.TopDirectoryOnly))
        {
            if (f.EndsWith(".md") || f.EndsWith(".txt")) yield return Path.GetRelativePath(Root, f).Replace('\\', '/');
        }

        foreach (var f in Directory.EnumerateFiles(Abs("docs/adr"), "*.md")) yield return Path.GetRelativePath(Root, f).Replace('\\', '/');
    }

    private static IEnumerable<string> SourceFiles(string folder, string pattern)
    {
        foreach (var f in Directory.EnumerateFiles(Abs(folder), pattern, SearchOption.AllDirectories))
        {
            var rel = f.Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            yield return f;
        }
    }

    private static string? AsRepoPath(string token)
    {
        token = token.Trim().Trim('"', '\'', ',', ';', '(', ')', '[', ']').TrimEnd('.', ':');
        token = Regex.Replace(token, @"[:#]\d*[\w-]*$", string.Empty);
        if (token.Length == 0 || token.IndexOfAny(new[] { '<', '>', '*', '{', '}', '$', '|', '\\', ' ', '=' }) >= 0) return null;
        if (token.Contains("://") || token.Contains("...") || token.Contains('…')) return null;
        int slash = token.IndexOf('/');
        if (slash <= 0) return null;
        if (Array.IndexOf(TopLevel, token[..slash]) < 0) return null;
        return token.TrimEnd('/');
    }

    private static IEnumerable<string> PathCandidates(string markdown)
    {
        foreach (Match fence in Fence.Matches(markdown))
        {
            foreach (var word in Regex.Split(fence.Value, @"\s+"))
            {
                var p = AsRepoPath(word);
                if (p != null) yield return p;
            }
        }

        foreach (Match span in InlineCode.Matches(Fence.Replace(markdown, string.Empty)))
        {
            var p = AsRepoPath(span.Groups[1].Value);
            if (p != null) yield return p;
        }
    }

    [Fact]
    public void FilesAndFoldersCitedByTheDocs_Exist()
    {
        var missing = new List<string>();
        foreach (var doc in LiveDocs())
        {
            foreach (var path in PathCandidates(Read(doc)).Distinct())
            {
                if (Array.IndexOf(ExamplePaths, path) >= 0) continue;
                if (!File.Exists(Abs(path)) && !Directory.Exists(Abs(path))) missing.Add($"{doc}: {path}");
            }
        }

        Assert.True(missing.Count == 0, "Documents cite paths that do not exist:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryCommandLineOptionInTheGame_IsDocumentedInTheReadme()
    {
        var readme = Read("README.md");
        var undocumented = SourceFiles("src", "*.cs")
            .SelectMany(f => Flag.Matches(File.ReadAllText(f)).Select(m => m.Value))
            .Distinct()
            .Where(flag => !readme.Contains(flag + "=") && !Regex.IsMatch(readme, Regex.Escape(flag) + @"\b(?!-)"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(undocumented.Count == 0, "Options read by the game but missing from README.md:\n" + string.Join("\n", undocumented));
    }

    [Fact]
    public void CommandLineOptionsUsedByDocsAndScripts_ExistInTheGame()
    {
        var known = SourceFiles("src", "*.cs").SelectMany(f => Flag.Matches(File.ReadAllText(f)).Select(m => m.Value)).ToHashSet();
        var unknown = new List<string>();

        foreach (var doc in LiveDocs())
        {
            unknown.AddRange(Flag.Matches(Read(doc)).Select(m => m.Value).Where(f => !known.Contains(f)).Select(f => $"{doc}: {f}"));
        }

        foreach (var script in Directory.EnumerateFiles(Abs("tools"), "*.sh"))
        {
            var name = Path.GetFileName(script);
            unknown.AddRange(Flag.Matches(File.ReadAllText(script)).Select(m => m.Value).Where(f => !known.Contains(f)).Select(f => $"tools/{name}: {f}"));
        }

        Assert.True(unknown.Count == 0, "Options used but unknown to the game:\n" + string.Join("\n", unknown.Distinct()));
    }

    [Fact]
    public void Adrs_AreConsecutive_TitledAndIndexedInTheReadme()
    {
        var numbers = new List<int>();
        foreach (var file in Directory.EnumerateFiles(Abs("docs/adr"), "ADR-*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var m = Regex.Match(name, @"^ADR-(\d{3})-");
            Assert.True(m.Success, $"{name}: expected ADR-NNN-title.md");
            int n = int.Parse(m.Groups[1].Value);
            numbers.Add(n);
            var first = File.ReadLines(file).First(l => l.Trim().Length > 0);
            Assert.True(first.StartsWith($"# ADR-{n:000}"), $"{name}: first line must start with '# ADR-{n:000}', found '{first}'");
        }

        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);

        var readme = Read("README.md");
        Assert.True(
            Regex.IsMatch(readme, $@"ADR-001\s*(…|\.\.\.|-)\s*(ADR-)?0*{numbers.Max()}\b"),
            $"README.md must say that the ADRs run from ADR-001 to ADR-{numbers.Max():000}");
    }

    [Fact]
    public void EveryDocumentInDocs_IsListedInTheStatusMap()
    {
        var status = Read("docs/STATUS.md");
        var unlisted = Directory.EnumerateFiles(Abs("docs"), "*.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => (n!.EndsWith(".md") || n.EndsWith(".txt")) && n != "STATUS.md" && !status.Contains(n))
            .ToList();

        Assert.True(unlisted.Count == 0, "docs/STATUS.md (documentation map) does not list: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void VersionIsTheSameInStatusProjectAndExportPresets()
    {
        var status = Regex.Match(Read("docs/STATUS.md"), @"versión \*\*([^*]+)\*\*");
        Assert.True(status.Success, "docs/STATUS.md must state 'versión **x.y.z-label**'");
        string version = status.Groups[1].Value;

        var project = Regex.Match(Read("project.godot"), "(?m)^config/version=\"([^\"]+)\"");
        Assert.True(project.Success, "project.godot has no config/version");
        Assert.Equal(version, project.Groups[1].Value);

        string numeric = version.Split('-')[0];
        var presets = Regex.Matches(Read("export_presets.cfg"), "(?m)^application/version=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(presets);
        Assert.All(presets, v => Assert.Equal(numeric, v));
    }

    [Fact]
    public void EveryExportPreset_ShipsTheDataFormatsTheContentUses_AndExcludesTemplates()
    {
        // Content is read at run time, so Godot has to be told to pack it: a preset without these filters exports a game with no stages.
        var text = Read("export_presets.cfg");
        int presets = Regex.Matches(text, @"(?m)^\[preset\.\d+\]$").Count;
        var includes = Regex.Matches(text, "(?m)^include_filter=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();
        var excludes = Regex.Matches(text, "(?m)^exclude_filter=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.True(presets > 0);
        Assert.Equal(presets, includes.Count);
        Assert.Equal(presets, excludes.Count);
        foreach (var ext in new[] { "*.json", "*.stage", "*.fmt2" }) Assert.All(includes, f => Assert.Contains(ext, f));
        foreach (var pattern in new[] { "characters/_*/*", "stages/_*/*" }) Assert.All(excludes, f => Assert.Contains(pattern, f));
    }
}
