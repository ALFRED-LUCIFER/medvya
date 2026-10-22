using System.Reflection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.ArchitectureTests;

public class ClassificationTests
{
    internal static readonly string[] DomainAssemblies = ["Medvya.Identity.Domain", "Medvya.Tenancy.Domain", "Medvya.Audit.Domain", "Medvya.Patients.Domain", "Medvya.Billing.Domain", "Medvya.Scheduling.Domain", "Medvya.Leads.Domain"];

    internal static IEnumerable<string> Unclassified(IEnumerable<Type> types) =>
        types.Where(t => t.IsClass && (t.IsPublic || t.IsNestedPublic))
             .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                               .Where(p => p.GetCustomAttribute<ClassificationAttribute>() is null)
                               .Select(p => $"{t.FullName}.{p.Name}"));

    /// <summary>The list above is hand-kept; a module missing from it would skip this test and FloatingPointMoneyTests.</summary>
    [Fact]
    public void EveryModuleDomainAssemblyIsChecked()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Medvya.sln"))) root = root.Parent ?? throw new InvalidOperationException("Medvya.sln not found above the test output.");
        var modules = Directory.GetDirectories(Path.Combine(root.FullName, "src", "Modules"), "Medvya.*.Domain", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.Equal(modules.Order(), DomainAssemblies.Order());
    }

    [Fact]
    public void EveryDomainPropertyHasADataClassification()
    {
        var types = DomainAssemblies.SelectMany(n => Assembly.Load(n).GetTypes());
        Assert.Empty(Unclassified(types));
    }

    public class Sample
    {
        [Classification(DataClass.Personal)] public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
    }

    [Fact]
    public void CheckerReportsAMissingClassification() =>
        Assert.Equal([$"{typeof(Sample).FullName}.Phone"], Unclassified([typeof(Sample)]));
}
