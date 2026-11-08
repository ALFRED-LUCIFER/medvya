using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;

namespace Medvya.IntegrationTests;

/// <summary>
/// Forgiving desk name search on real PostgreSQL: spelling variants, a doubled, missing or transposed letter, partial two-word names,
/// Arabic and Devanagari names, ranking (exact before near) and tenant and facility isolation. Synthetic names only.
/// </summary>
public sealed class PatientNameSearchTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    readonly Guid tenant = Guid.NewGuid(), otherTenant = Guid.NewGuid(), facility = Guid.NewGuid(), otherFacility = Guid.NewGuid();

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    async Task AddAsync(Guid t, Guid f, string mrn, string name, string? arabic = null)
    {
        await using var db = sql.Patients(t);
        db.Patients.Add(new Patient
        {
            Id = Guid.NewGuid(), TenantId = t, FacilityId = f, Mrn = mrn, CountryCode = "IN", FullName = name, ArabicName = arabic, Mobile = "+9190000" + mrn,
            DateOfBirth = new DateOnly(1980, 1, 1), Status = RegistrationStatus.Complete, RegisteredAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    async Task SeedAsync()
    {
        string[] names = ["Muhammad Rafiq Khan", "Mohammed Ali", "Krishnan Subramanian", "Sreenivasan Kumar", "Shreekumar Nair", "Fathima Beevi", "Venkatesh Reddy",
            "Anna D'Souza", "Aaron Srinivasa", "Priya Menon"];
        for (var i = 0; i < names.Length; i++) await AddAsync(tenant, facility, $"{100000 + i}", names[i]);
        await AddAsync(tenant, facility, "100100", "Fatima Al Mansoori", "فاطمة المنصوري");
        await AddAsync(tenant, facility, "100101", "Sunita Sharma", "सुनीता शर्मा");
        await AddAsync(tenant, otherFacility, "100000", "Mohamed Elsewhere");
        await AddAsync(otherTenant, facility, "100000", "Mohamed Hussain");
    }

    async Task<IReadOnlyList<Guid>> Names(string query)
    {
        var registry = new PatientRegistry(sql.Patients(tenant), new PostgresFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(facility),
            new FixedUser(Guid.NewGuid()), new AuditLog(sql.Audit(tenant)), TimeProvider.System, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));
        var result = await registry.SearchAsync(new("IN", query));
        Assert.Equal(SearchKind.Name, result.MatchedOn);
        return [.. result.Items.Select(i => i.Id)];
    }

    async Task<string[]> Found(string query)
    {
        var ids = await Names(query);
        await using var db = sql.Patients(tenant);
        var all = db.Patients.Where(p => p.FacilityId == facility).ToDictionary(p => p.Id, p => p.FullName);
        return [.. ids.Select(id => all[id])];
    }

    [Fact]
    public async Task ANameEndingInADigitIsFoundByItsFullName()
    {
        await SeedAsync();
        await AddAsync(tenant, facility, "000002", "Test Patient 2");
        Assert.Contains("Test Patient 2", await Found("Test Patient 2"));
        Assert.Contains("Test Patient 2", await Found("test patient"));
    }

    [Fact]
    public async Task SpellingVariantsDoubledMissingAndTransposedLettersAreForgiven()
    {
        await SeedAsync();
        var mohamed = await Found("mohamed");
        Assert.Contains("Muhammad Rafiq Khan", mohamed);
        Assert.Contains("Mohammed Ali", mohamed);
        Assert.DoesNotContain(mohamed, n => n.Contains("Hussain", StringComparison.Ordinal) || n.Contains("Elsewhere", StringComparison.Ordinal)); // another tenant, another facility
        Assert.Contains("Krishnan Subramanian", await Found("krishnann subramanian")); // doubled letter
        Assert.Contains("Krishnan Subramanian", await Found("krsihnan")); // transposed
        Assert.Contains("Venkatesh Reddy", await Found("venkatsh")); // missing letter
        Assert.Contains("Fathima Beevi", await Found("fatima"));
        Assert.Contains("Anna D'Souza", await Found("dsouza"));
        Assert.Contains("Anna D'Souza", await Found("d'souza"));
    }

    [Fact]
    public async Task PartialTwoWordNamesAndOtherScriptsAreFound()
    {
        await SeedAsync();
        Assert.Equal("Sreenivasan Kumar", (await Found("sreen kumar"))[0]);
        Assert.Contains("Shreekumar Nair", await Found("shree kumar"));
        Assert.Contains("Fatima Al Mansoori", await Found("فاطمة"));
        Assert.Contains("Sunita Sharma", await Found("सुनीता"));
        Assert.Contains("Priya Menon", await Found("pri men"));
    }

    [Fact]
    public async Task ExactSpellingRanksBeforeNearOnesAndOtherTenantsAreNeverShown()
    {
        await SeedAsync();
        // "Aaron Srinivasa" is a near match for sreenivasan and sorts first by name; the exact word still ranks first.
        var found = await Found("sreenivasan");
        Assert.Equal("Sreenivasan Kumar", found[0]);
        Assert.Contains("Aaron Srinivasa", found);
        Assert.Empty(await Found("hussain"));
        Assert.Empty(await Found("elsewhere"));
    }
}
