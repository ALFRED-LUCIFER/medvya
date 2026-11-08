using System.Text.RegularExpressions;

namespace Medvya.ArchitectureTests;

/// <summary>
/// #888: every module records its migrations in the one shared public."__EFMigrationsHistory", keyed by migration id alone. Two modules
/// with the same id would make EF treat the second as applied and skip it silently (an RLS or ledger step lost), so ids must be unique.
/// </summary>
public partial class MigrationIdTests
{
    [GeneratedRegex("""\[Migration\("(?<id>[^"]+)"\)\]""")]
    private static partial Regex MigrationAttribute();

    internal static List<string> Duplicates(IEnumerable<(string Path, string Text)> designers) =>
        [.. designers.SelectMany(d => MigrationAttribute().Matches(d.Text).Select(m => (Id: m.Groups["id"].Value, d.Path)))
            .GroupBy(x => x.Id).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => Path.GetFileName(x.Path)))}")];

    [Fact]
    public void NoTwoMigrationsShareAnId()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        var src = Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src");
        var designers = Directory.EnumerateFiles(src, "*.Designer.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p))).ToList();
        Assert.NotEmpty(designers);
        Assert.Empty(Duplicates(designers));
    }

    [Fact]
    public void TheCheckFindsTheSameIdInTwoModules() =>
        Assert.Single(Duplicates([("a/X.Designer.cs", """[Migration("20261107090000_A")]"""), ("b/Y.Designer.cs", """[Migration("20261107090000_A")]""")]));
}
