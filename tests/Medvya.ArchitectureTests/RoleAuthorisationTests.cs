using System.Text.RegularExpressions;

namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P1-008 follow-up: access comes from IStaffAccess (membership, role, custom-role extras, licence gate, read from the database
/// per request). Nothing may authorise from the "role" claim, or from a role looked up and run through RoleMatrix, outside the files below.
/// </summary>
public partial class RoleAuthorisationTests
{
    [GeneratedRegex("""FindFirst[A-Za-z]*\(\s*"role"|RoleClaim|ClaimTypes\.Role|IsInRole|RequireRole|ActiveRoleAsync\(|RoleMatrix\.Of\(|CustomRoleRules\.Effective\(""")]
    private static partial Regex RoleUse();

    /// <summary>Reviewed uses that are not an access decision for the request. Adding one is a reviewed decision.</summary>
    static readonly Dictionary<string, string> Allowed = new()
    {
        ["Medvya.Tenancy.Domain/RoleMatrix.cs"] = "the matrix itself",
        ["Medvya.Tenancy.Domain/StaffRoles.cs"] = "custom-role rules (extras ceiling)",
        ["Medvya.Tenancy.Infrastructure/StaffAccess.cs"] = "the one place that turns a membership into access",
        ["Medvya.Tenancy.Infrastructure/FacilityDirectory.cs"] = "reads the membership for StaffAccess",
        ["Medvya.Tenancy.Application/IFacilityDirectory.cs"] = "contract of the above",
        ["Medvya.Tenancy.Infrastructure/StaffRoster.cs"] = "last-admin check on stored memberships when offboarding, not a caller check",
        ["Medvya.Tenancy.Infrastructure/Invitations.cs"] = "the invited role's access, compared with the inviter's from IStaffAccess (#875); not a caller check",
        ["Medvya.Tenancy.Infrastructure/PracticeSettings.cs"] = "copies the creator's membership role to a new location, not a check",
        ["Host.Api/Auth/SessionControl.cs"] = "drops the legacy role claim from old cookies",
        ["Medvya.Identity.Api/StaffEndpoints.cs"] = "displays the built-in role table behind ManageStaff",
        ["Medvya.Identity.Api/InvitationEndpoints.cs"] = "previews the invited role's table to the invitee",
        // Platform console (MDV-P5-040): not a practice role. The claim is never read from the cookie: the host replaces it on every
        // request from the live member row, so the console policies below decide on the database's current role.
        ["Host.Platform/Auth/ConsoleAuthentication.cs"] = "sets the platform role claim from the live member row on each request",
        ["Medvya.Platform.Api/PlatformEndpoints.cs"] = "console policies on that per-request platform role claim",
    };

    static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repo root not found"), "src");
    }

    internal static List<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        [.. files.Where(f => RoleUse().IsMatch(f.Text) && !Allowed.Keys.Any(a => f.Path.Replace('\\', '/').EndsWith(a, StringComparison.Ordinal))).Select(f => f.Path)];

    [Fact]
    public void NoCodeAuthorisesFromTheRoleClaimOrALookedUpRole()
    {
        var files = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p)));
        Assert.Empty(Offenders(files));
    }

    [Fact]
    public void TheCheckerCatchesAnEndpointReadingTheRoleClaim()
    {
        Assert.Single(Offenders([("src/Modules/Scheduling/Api/Rogue.cs", """ctx.User.FindFirstValue("role") == "PracticeAdmin" """)]));
        Assert.Single(Offenders([("src/Modules/Scheduling/Infra/Rogue.cs", "facilities.ActiveRoleAsync(id, f, ct)")]));
        Assert.Empty(Offenders([("src/Modules/Tenancy/Medvya.Tenancy.Infrastructure/StaffAccess.cs", "RoleMatrix.Of(role, c)")]));
    }
}
