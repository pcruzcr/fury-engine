namespace Fury.Core.Authoring;

/// <summary>How serious a finding of the content validators is.</summary>
public enum IssueSeverity
{
    /// <summary>The content works but something looks unintended (missing optional animation, unused file...).</summary>
    Warning = 0,

    /// <summary>The content cannot be used.</summary>
    Error = 1,
}

/// <summary>One finding of a content validator: where it is (file or field) and what is wrong.</summary>
public readonly record struct ContentIssue(IssueSeverity Severity, string Where, string Message)
{
    public override string ToString() => $"{(Severity == IssueSeverity.Error ? "ERROR" : "warning")}: {Where}: {Message}";
}

/// <summary>Collects <see cref="ContentIssue"/>s while a validator walks a document.</summary>
public sealed class ContentReport
{
    private readonly List<ContentIssue> _issues = [];

    public IReadOnlyList<ContentIssue> Issues => _issues;

    public bool HasErrors => _issues.Any(i => i.Severity == IssueSeverity.Error);

    public int ErrorCount => _issues.Count(i => i.Severity == IssueSeverity.Error);

    public int WarningCount => _issues.Count(i => i.Severity == IssueSeverity.Warning);

    public void Error(string where, string message) => _issues.Add(new ContentIssue(IssueSeverity.Error, where, message));

    public void Warn(string where, string message) => _issues.Add(new ContentIssue(IssueSeverity.Warning, where, message));

    public void Add(ContentIssue issue) => _issues.Add(issue);

    public void AddRange(IEnumerable<ContentIssue> issues) => _issues.AddRange(issues);

    /// <summary>Keeps only a relative, forward-slash path that cannot leave its content folder.</summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 160) return false;
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':')) return false;
        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..") return false;
        }
        return true;
    }
}
