using Medvya.SyntheticData;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>The bulk patient seed: idempotent, MRNs sequential, nobody reachable, reset limited to its own practice, refused for production-looking names.</summary>
public sealed class SyntheticPatientsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SeedingTwiceAddsNothingAndKeepsMrnsSequentialUnreachableAndSearchable()
    {
        var (tenant, facility, ids, added) = await SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Seed Test Clinic", 1500, 100_000);
        Assert.Equal(1500, added);
        Assert.Equal(0, (await SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Seed Test Clinic", 1500, 100_000)).Added);

        await using var db = sql.Patients(tenant);
        var patients = await db.Patients.Where(p => p.FacilityId == facility).OrderBy(p => p.Mrn).ToListAsync();
        Assert.Equal(1500, patients.Count);
        Assert.Equal(Enumerable.Range(100_000, 1500).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), patients.Select(p => p.Mrn));
        Assert.Equal(ids.Order(), patients.Select(p => p.Id).Order());
        Assert.All(patients, p => Assert.Null(p.Email));
        Assert.Empty(await db.ConsentReceipts.ToListAsync()); // no consent: no reminder is ever sent to them
        Assert.Equal(1_500 + 99_999, (await db.Database.SqlQuery<int>($"""SELECT "LastNumber" AS "Value" FROM patients."MrnCounters" """).SingleAsync())); // one counter per organisation
        Assert.Contains(patients, p => p.Status == Medvya.Patients.Domain.RegistrationStatus.Incomplete);

        // Another tenant sees none of them (row-level security).
        await using var other = sql.Patients(Guid.NewGuid());
        Assert.Empty(await other.Patients.ToListAsync());
    }

    [Fact]
    public async Task ResetClearsOnlyThatPracticeAndProductionLookingNamesAreRefused()
    {
        await SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Reset Keep Clinic", 300, 100_000);
        var (tenant, facility, _, _) = await SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Reset Me Clinic", 300, 100_000);
        var again = await SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Reset Me Clinic", 200, 100_000, resetConnectionString: sql.As("medvya_migrator"));
        Assert.Equal(200, again.Added);
        await using var db = sql.Patients(tenant);
        Assert.Equal(200, await db.Patients.CountAsync(p => p.FacilityId == facility));
        await using var kept = sql.Patients(SyntheticPatients.Ids("Reset Keep Clinic").Tenant);
        Assert.Equal(300, await kept.Patients.CountAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Acme Production Clinic", 10, 100_000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticPatients.SeedAsync(sql.ConnectionString, "Production", "Seed Test Clinic", 10, 100_000));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Seed Test Clinic", 10, 999_995));
    }
}
