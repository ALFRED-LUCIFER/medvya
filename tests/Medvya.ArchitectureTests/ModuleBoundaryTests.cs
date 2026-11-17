using System.Xml.Linq;

namespace Medvya.ArchitectureTests;

/// <summary>
/// AGENTS.md 3.1: a module never reaches into another module. A project under src/Modules/&lt;M&gt; may reference another module's
/// project only if it is that module's Application or Contracts (its published services); never its Domain, Infrastructure or Api.
/// Shared code goes to src/BuildingBlocks, which in turn references no module.
/// </summary>
public class ModuleBoundaryTests
{
    static readonly string[] OpenLayers = ["Application", "Contracts"];

    static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src");
    }

    /// <summary>"Modules/Tenancy/Medvya.Tenancy.Api" → ("Tenancy", "Api"); a BuildingBlocks project → ("", name); anything else → null.</summary>
    static (string Module, string Layer)? Classify(string srcDir, string projectPath)
    {
        var parts = Path.GetRelativePath(srcDir, projectPath).Replace('\\', '/').Split('/');
        if (parts is ["Modules", var module, var project, ..]) return (module, project[(project.LastIndexOf('.') + 1)..]);
        if (parts is ["BuildingBlocks", var bb, ..]) return ("", bb);
        return null;
    }

    /// <summary>Every "From → To" reference that breaks the rule. <paramref name="references"/> maps a project file to the project files it references.</summary>
    internal static List<string> Offenders(string srcDir, IEnumerable<(string Project, IEnumerable<string> References)> references)
    {
        var offenders = new List<string>();
        foreach (var (project, refs) in references)
        {
            if (Classify(srcDir, project) is not { } from) continue;
            foreach (var target in refs)
            {
                if (Classify(srcDir, target) is not { } to || to.Module == from.Module) continue;
                var allowed = from.Module.Length > 0 && (to.Module.Length == 0 || OpenLayers.Contains(to.Layer)); // BuildingBlocks: no module at all
                if (!allowed) offenders.Add($"{Path.GetFileNameWithoutExtension(project)} → {Path.GetFileNameWithoutExtension(target)}");
            }
        }
        return offenders;
    }

    static IEnumerable<string> ReferencesOf(string csproj) =>
        XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));

    [Fact]
    public void ModulesReachEachOtherOnlyThroughApplicationOrContracts()
    {
        var src = Src();
        var projects = Directory.EnumerateFiles(Path.Combine(src, "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(src, "BuildingBlocks"), "*.csproj", SearchOption.AllDirectories))
            .Select(p => (p, ReferencesOf(p)));
        Assert.Empty(Offenders(src, projects));
    }

    [Fact]
    public void TheCheckerCatchesAReachIntoAnotherModule()
    {
        var src = Src();
        string P(string module, string layer) => Path.Combine(src, "Modules", module, $"Medvya.{module}.{layer}", $"Medvya.{module}.{layer}.csproj");
        var bb = Path.Combine(src, "BuildingBlocks", "Medvya.BuildingBlocks.Domain", "Medvya.BuildingBlocks.Domain.csproj");
        Assert.Equal(4, Offenders(src, [(P("Scheduling", "Api"), [P("Tenancy", "Api"), P("Tenancy", "Domain"), P("Tenancy", "Infrastructure"), P("Tenancy", "Application"), P("Scheduling", "Domain"), bb]),
            (bb, [P("Tenancy", "Application")])]).Count);
    }
}
