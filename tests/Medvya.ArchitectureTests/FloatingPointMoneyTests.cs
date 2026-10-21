using System.Reflection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>
/// MDV-P4-001 / AGENTS.md 3.4: money is Money (long minor units + ISO currency), so no domain type holds a float, double or
/// decimal. A measurement that truly needs a fraction (not money) gets a named exemption here with its reason.
/// The database half of the rule is BillingNumberingTests.NoColumnIsFloatingPointOrDecimal.
/// </summary>
public class FloatingPointMoneyTests
{
    static readonly Type[] Forbidden = [typeof(float), typeof(double), typeof(decimal)];

    internal static IEnumerable<string> FloatingPoint(IEnumerable<Type> types) =>
        types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                               .Where(p => Forbidden.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                               .Select(p => $"{t.FullName}.{p.Name}"));

    [Fact]
    public void NoDomainPropertyIsFloatingPointOrDecimal()
    {
        var types = ClassificationTests.DomainAssemblies.Select(Assembly.Load).Append(typeof(Money).Assembly).SelectMany(a => a.GetTypes());
        Assert.Empty(FloatingPoint(types));
    }

    public class Sample
    {
        public long AmountMinor { get; set; }
        public decimal? Total { get; set; }
    }

    [Fact]
    public void CheckerReportsADecimalProperty() =>
        Assert.Equal([$"{typeof(Sample).FullName}.Total"], FloatingPoint([typeof(Sample)]));
}
