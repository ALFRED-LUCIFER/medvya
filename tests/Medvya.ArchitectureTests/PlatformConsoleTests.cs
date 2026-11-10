using System.Reflection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P5-040: the platform console never shows patient records. Its host can load no clinic module except the reviewed organisation review path
/// (ADR-014 note, MDV-P1-064: Tenancy's contracts and implementation and Audit's, which reference no patient-data module), so no patient type,
/// DbContext or service can reach a console endpoint; its own domain holds no health or national-id field; and its database login has no right on any
/// clinic schema (DatabaseRoleTests in the integration tests).
/// </summary>
public class PlatformConsoleTests
{
    static readonly string[] ClinicModules = ["Patients", "Encounters", "Prescribing", "Scheduling", "Notifications", "Billing", "Tenancy", "Identity", "Audit", "Leads"];

    // The reviewed path: the organisation's request, documents and state (Tenancy) and the audit write of a decision (Audit). Not Tenancy.Api, which
    // pulls Identity and Scheduling, and not any other module. A new entry here is a review, like a new grant in infra/postgres/table-rights.sql.
    static readonly string[] ReviewPath = ["Medvya.Tenancy.Application", "Medvya.Tenancy.Domain", "Medvya.Tenancy.Infrastructure",
        "Medvya.Audit.Application", "Medvya.Audit.Domain", "Medvya.Audit.Infrastructure"];

    internal static List<string> ClinicAssemblies(IEnumerable<string> names) =>
        [.. names.Where(n => !ReviewPath.Contains(n) && ClinicModules.Any(m => n.StartsWith($"Medvya.{m}.", StringComparison.Ordinal)))];

    static HashSet<string> ReferenceClosure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<AssemblyName>([root.GetName()]);
        while (todo.TryPop(out var name))
        {
            if (!seen.Add(name.Name!) || !name.Name!.StartsWith("Medvya", StringComparison.Ordinal) && name.Name != "Host.Platform") continue;
            foreach (var r in Assembly.Load(name).GetReferencedAssemblies()) todo.Push(r);
        }
        return seen;
    }

    [Fact]
    public void TheConsoleHostCannotReachAnyClinicModule() =>
        Assert.Empty(ClinicAssemblies(ReferenceClosure(typeof(Host.Platform.Auth.ConsoleAuthentication).Assembly)));

    [Fact]
    public void TheCheckerCatchesAClinicModule()
    {
        Assert.Equal(["Medvya.Patients.Infrastructure"], ClinicAssemblies(["Host.Platform", "Medvya.Platform.Api", "Medvya.Patients.Infrastructure"]));
        // Only the named review path is let through: the rest of Tenancy and Audit, and Identity or Scheduling behind them, are not.
        Assert.Equal(["Medvya.Tenancy.Api", "Medvya.Identity.Application", "Medvya.Scheduling.Application"],
            ClinicAssemblies(["Medvya.Tenancy.Infrastructure", "Medvya.Tenancy.Api", "Medvya.Audit.Infrastructure", "Medvya.Identity.Application", "Medvya.Scheduling.Application"]));
        Assert.Empty(ClinicAssemblies(["Medvya.Platform.Domain", "Medvya.BuildingBlocks.Security"]));
    }

    [Fact]
    public void TheConsoleDomainHoldsNoHealthOrNationalIdField()
    {
        var fields = typeof(Medvya.Platform.Domain.PlatformMember).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.GetCustomAttribute<ClassificationAttribute>()?.Value is DataClass.SensitiveHealth or DataClass.NationalId)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");
        Assert.Empty(fields);
    }
}
