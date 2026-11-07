using System.Reflection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P5-040: the platform console never shows patient records. Its host can load no clinic module, so no patient type, DbContext or
/// service can reach a console endpoint; its own domain holds no health or national-id field; and its database login has no right on any
/// clinic schema (DatabaseRoleTests in the integration tests).
/// </summary>
public class PlatformConsoleTests
{
    static readonly string[] ClinicModules = ["Patients", "Encounters", "Scheduling", "Notifications", "Billing", "Tenancy", "Identity", "Audit", "Leads"];

    internal static List<string> ClinicAssemblies(IEnumerable<string> names) =>
        [.. names.Where(n => ClinicModules.Any(m => n.StartsWith($"Medvya.{m}.", StringComparison.Ordinal)))];

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
