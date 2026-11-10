using System.Reflection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P5-040: the platform console never shows patient records. Its host can load no clinic module but Tenancy and Audit (the organisation
/// review, MDV-P1-064, over its own narrow login), so no patient type, DbContext or service can reach a console endpoint; its own domain holds no health or national-id field; and its database login has no right on any
/// clinic schema (DatabaseRoleTests in the integration tests).
/// </summary>
public class PlatformConsoleTests
{
    static readonly string[] ClinicModules = ["Patients", "Encounters", "Prescribing", "Scheduling", "Notifications", "Billing", "Tenancy", "Identity", "Audit", "Leads"];

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

    // The organisation review (ADR-014 amended) needs Tenancy's approval services and the clinic audit log; nothing with patient data.
    static readonly string[] ReviewModules = ["Medvya.Audit.Application", "Medvya.Audit.Domain", "Medvya.Audit.Infrastructure",
        "Medvya.Tenancy.Application", "Medvya.Tenancy.Domain", "Medvya.Tenancy.Infrastructure"];

    [Fact]
    public void TheConsoleHostReachesNoClinicModuleButTheOrganisationReview() =>
        Assert.Empty(ClinicAssemblies(ReferenceClosure(typeof(Host.Platform.Auth.ConsoleAuthentication).Assembly)).Except(ReviewModules));

    [Fact]
    public void TheOrganisationReviewTypesHoldNoPatientField()
    {
        // What the queue and detail return: control-plane facts and document metadata only.
        var types = new[] { typeof(Medvya.Tenancy.Application.QueueItem), typeof(Medvya.Tenancy.Application.RequestDetail),
            typeof(Medvya.Tenancy.Application.OrganisationDocumentView), typeof(Medvya.Tenancy.Application.HistoryEntry) };
        var fields = types.SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();
        Assert.DoesNotContain(fields, f => f.Contains("Patient", StringComparison.OrdinalIgnoreCase) || f.Contains("Mrn", StringComparison.OrdinalIgnoreCase)
            || f.Contains("Diagnos", StringComparison.OrdinalIgnoreCase) || f.Contains("NationalId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(types.SelectMany(t => t.GetProperties()), p => p.PropertyType.Assembly.GetName().Name is { } n && ClinicAssemblies([n]).Except(ReviewModules).Any());
    }

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
