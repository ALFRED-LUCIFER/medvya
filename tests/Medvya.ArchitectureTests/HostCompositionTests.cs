namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P0-040: Host.Api is composition only. Feature endpoints, services and entities live in their module's Api/Application/Infrastructure
/// project; the host keeps sign-in (BFF), request context, middleware, telemetry, the public site and Program.cs.
/// </summary>
public class HostCompositionTests
{
    static readonly string[] AllowedFolders = ["Auth", "Context", "Middleware", "Observability", "PublicSite"];

    static string HostDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src", "Host.Api");
    }

    internal static List<string> Offenders(string hostDir, IEnumerable<string> files) =>
        [.. files.Select(f => Path.GetRelativePath(hostDir, f).Replace('\\', '/'))
            .Where(r => !r.StartsWith("obj/", StringComparison.Ordinal) && !r.StartsWith("bin/", StringComparison.Ordinal))
            .Where(r => r != "Program.cs" && !AllowedFolders.Any(a => r.StartsWith(a + "/", StringComparison.Ordinal)))];

    [Fact]
    public void HostApiHoldsOnlyCompositionCode() =>
        Assert.Empty(Offenders(HostDir(), Directory.EnumerateFiles(HostDir(), "*.cs", SearchOption.AllDirectories)));

    [Fact]
    public void TheCheckerCatchesAFeatureEndpointInTheHost()
    {
        var host = HostDir();
        Assert.Single(Offenders(host, [Path.Combine(host, "Staff", "StaffEndpoints.cs")]));
        Assert.Empty(Offenders(host, [Path.Combine(host, "Auth", "StaffAuthentication.cs"), Path.Combine(host, "Program.cs")]));
    }
}
