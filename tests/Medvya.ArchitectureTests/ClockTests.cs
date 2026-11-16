using System.Text.RegularExpressions;

namespace Medvya.ArchitectureTests;

/// <summary>
/// Time comes from the injected <see cref="TimeProvider"/>, so tests and the DevClock browser journeys control audit times, session and
/// invitation expiry. A genuine wall-clock read (a storage link checked by Azure, a sign-in token checked by the OIDC handler) says why on
/// the same line with a <c>// wall clock:</c> comment.
/// </summary>
public partial class ClockTests
{
    [GeneratedRegex(@"\bDateTime(Offset)?\.(UtcNow|Now|Today)\b")]
    private static partial Regex WallClock();

    internal static List<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        [.. files.SelectMany(f => f.Text.Split('\n').Select((line, i) => (f.Path, Line: line, No: i + 1)))
            .Where(l => WallClock().IsMatch(l.Line) && !l.Line.Contains("// wall clock:", StringComparison.Ordinal))
            .Select(l => $"{l.Path}:{l.No}")];

    static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src");
    }

    [Fact]
    public void ProductionCodeReadsTimeFromTheInjectedClock()
    {
        var src = Src();
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(src, p).Replace('\\', '/'))
            .Where(r => !r.Contains("/obj/", StringComparison.Ordinal) && !r.Contains("/bin/", StringComparison.Ordinal) && !r.Contains("/Migrations/", StringComparison.Ordinal))
            .Select(r => (r, File.ReadAllText(Path.Combine(src, r))));
        Assert.Empty(Offenders(files));
    }

    [Fact]
    public void TheCheckerCatchesADirectClockRead() =>
        Assert.Equal(["a.cs:2", "a.cs:3"], Offenders([("a.cs", "var x = clock.GetUtcNow();\nvar y = DateTimeOffset.UtcNow;\nvar d = DateTime.Today;\nvar z = DateTimeOffset.UtcNow; // wall clock: Azure checks it\npublic DateTimeOffset Now => x;")]));
}
