using System.Reflection;
using System.Text.RegularExpressions;
using HockeyIndex.Api.Domain;

namespace HockeyIndex.Api.Tests.Unit;

/// <summary>AC-LC-2: EventTransitions is the sole writer of status, hidden_reason and hidden_from_status.</summary>
public sealed partial class EventStatusSingleWriterTests
{
    private static readonly string[] AllowedWriterFiles =
    [
        Path.Combine("Features", "Events", "EventTransitions.cs"),
        Path.Combine("Domain", "HockeyEvent.cs"),
    ];

    [Theory]
    [InlineData(nameof(HockeyEvent.Status))]
    [InlineData(nameof(HockeyEvent.HiddenReason))]
    [InlineData(nameof(HockeyEvent.HiddenFromStatus))]
    [InlineData(nameof(HockeyEvent.PublishedAt))]
    [InlineData(nameof(HockeyEvent.CancelledAt))]
    [InlineData(nameof(HockeyEvent.ArchivedAt))]
    public void Lifecycle_properties_have_private_setters(string propertyName)
    {
        var setter = typeof(HockeyEvent).GetProperty(propertyName)!.SetMethod;

        Assert.NotNull(setter);
        Assert.True(setter.IsPrivate);
    }

    [Fact]
    public void ApplyLifecycle_is_not_public()
    {
        var method = typeof(HockeyEvent).GetMethod(nameof(HockeyEvent.ApplyLifecycle), BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.True(method.IsAssembly);
    }

    [Fact]
    public void Only_EventTransitions_writes_lifecycle_columns()
    {
        var apiRoot = FindApiSourceRoot();
        var offenders = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(apiRoot, path), Path: path))
            .Where(file => !IsExcluded(file.Relative))
            .SelectMany(file => FindWrites(File.ReadAllText(file.Path)).Select(match => $"{file.Relative}: {match}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("evt.ApplyLifecycle(next, now);")]
    [InlineData("setters.SetProperty(e => e.Status, EventStatus.Archived)")]
    [InlineData("setters.SetProperty(evt => evt.HiddenReason, (HiddenReason?)null)")]
    [InlineData("\"UPDATE events SET hidden_from_status = NULL WHERE id = @id\"")]
    [InlineData("$\"\"\"\n    UPDATE events e\n    SET updated_at = now(), status = 'archived'\n    \"\"\"")]
    public void Scanner_detects_forbidden_writes(string source) => Assert.NotEmpty(FindWrites(source));

    [Theory]
    [InlineData("setters.SetProperty(evt => evt.PublishRequestedAt, (DateTimeOffset?)null)")]
    [InlineData("\"UPDATE events SET updated_at = now() WHERE status = 'draft'\"")]
    [InlineData("\"SELECT status FROM events WHERE id = @id\"")]
    public void Scanner_ignores_allowed_code(string source) => Assert.Empty(FindWrites(source));

    private static bool IsExcluded(string relativePath) =>
        AllowedWriterFiles.Contains(relativePath, StringComparer.Ordinal)
        || relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment is "Migrations" or "bin" or "obj");

    private static IEnumerable<string> FindWrites(string source) =>
        new[] { ApplyLifecycleCall(), SetLifecycleProperty(), SqlLifecycleUpdate() }
            .SelectMany(pattern => pattern.Matches(source))
            .Select(match => match.Value.ReplaceLineEndings(" "));

    private static string FindApiSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "HockeyIndex.Api");
            if (File.Exists(Path.Combine(candidate, "HockeyIndex.Api.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("HockeyIndex.Api source directory not found above the test output directory.");
    }

    [GeneratedRegex(@"\bApplyLifecycle\s*\(")]
    private static partial Regex ApplyLifecycleCall();

    [GeneratedRegex(@"SetProperty\s*\(\s*\w+\s*=>\s*\w+\.(Status|HiddenReason|HiddenFromStatus|PublishedAt|CancelledAt|ArchivedAt)\b")]
    private static partial Regex SetLifecycleProperty();

    [GeneratedRegex(@"UPDATE\s+events\b(?:(?!;|""""|\bWHERE\b)[\s\S])*?\bSET\b(?:(?!;|""""|\bWHERE\b)[\s\S])*?\b(status|hidden_reason|hidden_from_status|published_at|cancelled_at|archived_at)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex SqlLifecycleUpdate();
}
