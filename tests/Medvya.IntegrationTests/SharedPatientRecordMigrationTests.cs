using Medvya.Audit.Infrastructure.Persistence;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-072: the migration that makes the MRN unique per organisation, on a database that already holds colliding MRNs (a second database in the container).</summary>
public class SharedPatientRecordMigrationTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    static readonly Guid Tenant = Guid.NewGuid(), OtherTenant = Guid.NewGuid(), Surat = Guid.NewGuid(), Mumbai = Guid.NewGuid(), Pune = Guid.NewGuid();

    static readonly string[] Order = ["surat1", "surat2", "mumbai1", "mumbai2", "pune1"];

    [Fact]
    public async Task CollidingMrnsOfOneTenantAreRenumberedInRegistrationOrderAndNothingIsLost()
    {
        var gate = new NpgsqlConnectionStringBuilder(sql.AdminConnectionString) { Database = "shared_" + Guid.NewGuid().ToString("N")[..8], Pooling = false }.ConnectionString;
        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand($"CREATE DATABASE {new NpgsqlConnectionStringBuilder(gate).Database}", admin).ExecuteNonQueryAsync();

        await using var conn = new NpgsqlConnection(gate);
        await conn.OpenAsync();
        await Medvya.DbMigrate.DatabaseMigrator.ApplyRolesAsync(conn);
        await new NpgsqlCommand("SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();
        var none = new PostgresFixture.FixedTenant(null);
        await using (var tenancy = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(conn).Options, none)) await tenancy.Database.MigrateAsync();
        await using (var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(conn).Options, none)) await audit.Database.MigrateAsync();
        PatientsDbContext Patients() => new(new DbContextOptionsBuilder<PatientsDbContext>().UseNpgsql(conn).Options, none, NationalIdColumns.MigrationsOnly);
        await using (var before = Patients()) await before.Database.MigrateAsync("20261107205614_PatientSearchKey"); // the step before SharedPatientRecord

        // Two facilities of one tenant each issued 000001 and 000002 (per-facility counters); another tenant has its own 000001.
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync();
        await new NpgsqlCommand($"""
            INSERT INTO tenancy."TenantIndex" ("TenantId") VALUES ('{Tenant}'), ('{OtherTenant}');
            INSERT INTO patients."MrnCounters" ("TenantId", "FacilityId", "LastNumber") VALUES ('{Tenant}', '{Surat}', 2), ('{Tenant}', '{Mumbai}', 2), ('{OtherTenant}', '{Pune}', 1);
            """, conn).ExecuteNonQueryAsync();
        var ids = new Dictionary<string, Guid>();
        foreach (var (key, tenant, facility, mrn, day) in new[]
        {
            ("surat1", Tenant, Surat, "000001", 1), ("surat2", Tenant, Surat, "000002", 3), ("mumbai1", Tenant, Mumbai, "000001", 2), ("mumbai2", Tenant, Mumbai, "000002", 4),
            ("pune1", OtherTenant, Pune, "000001", 1),
        })
        {
            ids[key] = Guid.NewGuid();
            await new NpgsqlCommand($"""
                INSERT INTO patients."Patients" ("Id", "TenantId", "FacilityId", "Mrn", "CountryCode", "FullName", "DateOfBirth", "DateOfBirthEstimated", "SexAtBirth", "Mobile", "Status", "RegisteredAt", "ChangedAt")
                VALUES ('{ids[key]}', '{tenant}', '{facility}', '{mrn}', 'IN', '{key} (synthetic)', '1980-01-01', false, 'Female', '+91 80000 00000', 'Complete', '2026-01-0{day}T00:00:00Z', '2026-01-0{day}T00:00:00Z')
                """, conn).ExecuteNonQueryAsync();
        }
        await new NpgsqlCommand("SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();

        // SAFETY: mutation check: removing the renumbering UPDATE (or the order by) from SharedPatientRecord makes this fail.
        await using (var after = Patients()) await after.Database.MigrateAsync();
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync();

        var mrns = new Dictionary<Guid, string>();
        await using (var read = await new NpgsqlCommand("""SELECT "Id", "Mrn" FROM patients."Patients" """, conn).ExecuteReaderAsync())
            while (await read.ReadAsync()) mrns[read.GetGuid(0)] = read.GetString(1);
        // The earliest holder keeps the number; the later one of 000001 and the two of the other facility follow the highest in registration order.
        Assert.Equal(["000001", "000002", "000003", "000004", "000001"], Order.Select(k => mrns[ids[k]]));
        Assert.Equal(5, mrns.Count);
        Assert.Equal(4, (int)(await new NpgsqlCommand($"""SELECT "LastNumber" FROM patients."MrnCounters" WHERE "TenantId" = '{Tenant}' """, conn).ExecuteScalarAsync())!);
        Assert.Equal(1, (int)(await new NpgsqlCommand($"""SELECT "LastNumber" FROM patients."MrnCounters" WHERE "TenantId" = '{OtherTenant}' """, conn).ExecuteScalarAsync())!);
        Assert.Equal(2L, (long)(await new NpgsqlCommand("""SELECT count(*) FROM patients."MrnCounters" """, conn).ExecuteScalarAsync())!);
    }
}
