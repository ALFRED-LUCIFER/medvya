using System.Reflection;
using Medvya.Audit.Infrastructure;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Security;
using Medvya.Patients.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-041 against real PostgreSQL: the confirmed findings of the PostgreSQL security review stay fixed. Synthetic data only.</summary>
public class PostgresHardeningTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    static async Task<PostgresException> Refused(NpgsqlConnection conn, string statement) =>
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(statement, conn).ExecuteNonQueryAsync());

    static async Task<long> Count(NpgsqlConnection conn, string query) => (long)(await new NpgsqlCommand(query, conn).ExecuteScalarAsync())!;

    /// <summary>One identifier row for <paramref name="tenant"/>, encrypted with the tenant's key the way the API stores it.</summary>
    async Task<Guid> SeedIdentifier(Guid tenant, string value = "91-2345-6789-0123") // synthetic ABHA-shaped values
    {
        var (patient, identifier) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var cipher = sql.NationalIds.Encrypt(tenant, NationalIdColumns.IdentifierLookup, identifier, value)!;
        await using var conn = await sql.OpenAs("dbo", tenant);
        await new NpgsqlCommand("SET session_replication_role = replica", conn).ExecuteNonQueryAsync(); // no patient row needed for this test
        await using var cmd = new NpgsqlCommand("""INSERT INTO patients."Identifiers" ("Id", "TenantId", "PatientId", "Scheme", "LookupValue") VALUES (@i, @t, @p, 'Abha', @v)""", conn);
        cmd.Parameters.AddWithValue("i", identifier);
        cmd.Parameters.AddWithValue("t", tenant);
        cmd.Parameters.AddWithValue("p", patient);
        cmd.Parameters.AddWithValue("v", cipher);
        await cmd.ExecuteNonQueryAsync();
        return identifier;
    }

    [Fact]
    public async Task AModuleRoleCannotDeleteOrReassignADataKeyButCanRewrapIt()
    {
        var tenant = Guid.NewGuid();
        sql.NationalIds.Index(tenant, "X"); // creates the tenant's keys through the application
        foreach (var user in new[] { "patients_user", "app_user" })
        {
            await using var conn = await sql.OpenAs(user, tenant);
            // SAFETY: mutation check: dropping the DataKeys REVOKE in table-rights.sql makes this fail (MDV-P0-041 PR).
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Refused(conn, $"""DELETE FROM patients."DataKeys" WHERE "TenantId" = '{tenant}'""")).SqlState);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Refused(conn, $"""UPDATE patients."DataKeys" SET "Purpose" = 'other' WHERE "TenantId" = '{tenant}'""")).SqlState);
        }

        var store = new PatientsDataKeyStore(sql.ConnectionString);
        var key = store.Find(tenant, TenantKeyRing.BlindIndexPurpose)!;
        store.Replace(key with { WrapKeyId = key.WrapKeyId + "-rewrapped" }); // the re-wrap path: wrapping columns only
        Assert.Equal(key.WrapKeyId + "-rewrapped", store.Find(tenant, TenantKeyRing.BlindIndexPurpose)!.WrapKeyId);
    }

    [Fact]
    public async Task AMissingKeyForATenantWithEncryptedRowsThrowsAndCreatesNoKey()
    {
        var tenant = Guid.NewGuid();
        await SeedIdentifier(tenant);
        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand($"""DELETE FROM patients."DataKeys" WHERE "TenantId" = '{tenant}'""", admin).ExecuteNonQueryAsync(); // a lost key row

        // A fresh key ring (no cached key), exactly as a restarted API would build it.
        var restarted = PatientsModule.NationalIdColumnsFor(sql.ConnectionString, "Development");
        // SAFETY: mutation check: removing the NOT EXISTS guard in PatientsDataKeyStore.Add makes this fail (MDV-P0-041 PR).
        var ex = Assert.Throws<KeyProtectionException>(() => restarted.Encrypt(tenant, NationalIdColumns.IdentifierLookup, Guid.NewGuid(), "91-2345-6789-0124"));
        Assert.Equal(KeyProtectionException.KeyMissing, ex.Message);
        Assert.Throws<KeyProtectionException>(() => restarted.Index(tenant, "X"));

        await using var check = await sql.OpenAs("dbo");
        Assert.Equal(0, await Count(check, $"""SELECT count(*) FROM patients."DataKeys" WHERE "TenantId" = '{tenant}'"""));
    }

    [Fact]
    public async Task ACiphertextCopiedIntoAnotherRowDoesNotDecrypt()
    {
        var tenant = Guid.NewGuid();
        var (source, target) = (await SeedIdentifier(tenant, "91-2345-6789-0125"), await SeedIdentifier(tenant, "91-2345-6789-0127"));
        await using (var db = sql.Patients(tenant))
            Assert.Equal("91-2345-6789-0127", (await db.Identifiers.AsNoTracking().SingleAsync(i => i.Id == target)).LookupValue);

        // A database administrator pastes one patient's encrypted ID over another's (row-level security is no obstacle to them).
        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand($"""UPDATE patients."Identifiers" SET "LookupValue" = (SELECT "LookupValue" FROM patients."Identifiers" WHERE "Id" = '{source}') WHERE "Id" = '{target}'""", admin).ExecuteNonQueryAsync();

        // SAFETY: mutation check: binding every value to one constant in NationalIdColumns makes this fail (MDV-P0-041 PR).
        await using var after = sql.Patients(tenant);
        await Assert.ThrowsAnyAsync<Exception>(() => after.Identifiers.AsNoTracking().SingleAsync(i => i.Id == target));
        Assert.Equal("91-2345-6789-0125", (await after.Identifiers.AsNoTracking().SingleAsync(i => i.Id == source)).LookupValue);
    }

    // Tenancy tables with personal data that reporting must never read (MDV-P0-041 b).
    static readonly string[] PersonalTenancyTables = ["Invitations", "DoctorLicences", "DoctorProfiles", "Profiles", "SettingsChanges"];

    [Fact]
    public async Task ReportingCannotReadTenancyTablesWithPersonalData()
    {
        await using var conn = await sql.OpenAs("reporting_user", Guid.NewGuid());
        foreach (var table in PersonalTenancyTables)
            // SAFETY: mutation check: restoring GRANT SELECT ON ALL TABLES IN SCHEMA tenancy in roles.sql makes this fail (MDV-P0-041 PR).
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Refused(conn, $"SELECT 1 FROM tenancy.\"{table}\" LIMIT 1")).SqlState);
        Assert.Equal(0, await Count(conn, "SELECT count(*) FROM tenancy.\"Tenants\"")); // a listed table is readable (inside RLS: no tenant rows)
    }

    [Fact]
    public async Task EveryTenancyTableReportingReadsHoldsNoPersonalData()
    {
        await using var conn = await sql.OpenAs("dbo");
        var readable = new List<string>();
        await using (var r = await new NpgsqlCommand("""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'tenancy' AND c.relkind = 'r' AND has_table_privilege('medvya_reporting', c.oid, 'SELECT')
            """, conn).ExecuteReaderAsync())
            while (await r.ReadAsync()) readable.Add(r.GetString(0));

        await using var db = sql.Context(null);
        var personal = db.Model.GetEntityTypes()
            .Where(e => e.ClrType.GetProperties().Any(p => p.GetCustomAttribute<ClassificationAttribute>()?.Value is DataClass.Personal or DataClass.SensitiveHealth or DataClass.NationalId))
            .Select(e => e.GetTableName()!).ToHashSet();
        Assert.Contains("DoctorLicences", personal); // the classification lookup really finds personal tables
        Assert.Contains("Tenants", readable);
        Assert.DoesNotContain(readable, personal.Contains);
        Assert.Empty(readable.Intersect(PersonalTenancyTables));
    }

    [Fact]
    public async Task AnAppRoleCannotEditTheIdsOnlyListsOrTheLedgerTableList()
    {
        await using var conn = await sql.OpenAs("app_user");
        foreach (var statement in new[]
        {
            // SAFETY: mutation check: dropping the TenantIndex/OutboxTenants REVOKE or the ledger INSERT revoke makes this fail (MDV-P0-041 PR).
            """DELETE FROM ledger."Chains" """,
            """DELETE FROM tenancy."TenantIndex" """,
            """DELETE FROM scheduling."OutboxTenants" """,
            """UPDATE tenancy."TenantIndex" SET "TenantId" = gen_random_uuid()""",
            """INSERT INTO ledger."Tables" ("Name", "Columns") VALUES ('patients.Patients', '{Id}')""",
            """DELETE FROM ledger."Tables" """,
        })
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Refused(conn, statement)).SqlState);
    }

    /// <summary>The destructive national-ID migration on a database that already holds an identifier row (a second database in the container).</summary>
    [Fact]
    public async Task TheNationalIdMigrationRefusesADatabaseThatAlreadyHoldsIdentifiers()
    {
        var gate = new NpgsqlConnectionStringBuilder(sql.AdminConnectionString) { Database = "gate_" + Guid.NewGuid().ToString("N")[..8], Pooling = false }.ConnectionString;
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
        await using (var before = Patients()) await before.Database.MigrateAsync("20261106180002_LedgerChain"); // the step before NationalIdEncryption

        await new NpgsqlCommand("RESET ROLE; SET session_replication_role = replica", conn).ExecuteNonQueryAsync();
        await new NpgsqlCommand($"""INSERT INTO patients."Identifiers" ("Id", "TenantId", "PatientId", "Scheme", "LookupValue") VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', 'Abha', '91-2345-6789-0126')""", conn).ExecuteNonQueryAsync();
        await new NpgsqlCommand("SET session_replication_role = origin; SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();

        await using var patients = Patients();
        // SAFETY: mutation check: removing the pg_relation_size gate from NationalIdEncryption makes this fail (MDV-P0-041 PR).
        var ex = await Assert.ThrowsAsync<PostgresException>(() => patients.Database.MigrateAsync());
        Assert.Contains("NationalIdEncryption refused", ex.MessageText, StringComparison.Ordinal);
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync();
        Assert.Equal(1, await Count(conn, """SELECT count(*) FROM patients."Identifiers" WHERE "LookupValue" IS NOT NULL""")); // nothing was dropped
    }
}

/// <summary>MDV-P0-041 ledger changes; its own database, because it alters a ledgered table and the ledger's table list.</summary>
public class LedgerHardeningTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AColumnAddedToALedgerTableKeepsEveryOldChainVerifying()
    {
        var tenant = Guid.NewGuid();
        for (var i = 0; i < 3; i++) await LedgerChainTests.WriteEvent(sql, tenant);
        Assert.Null(await LedgerChainTests.Verify(sql, tenant));

        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand("""ALTER TABLE audit."AuditEvents" ADD COLUMN "LaterColumn" text NOT NULL DEFAULT 'added later'""", admin).ExecuteNonQueryAsync();

        // SAFETY: mutation check: hashing to_jsonb(row) instead of the listed columns makes this fail (MDV-P0-041 PR).
        Assert.Null(await LedgerChainTests.Verify(sql, tenant));
        await LedgerChainTests.WriteEvent(sql, tenant); // the chain goes on after the change
        Assert.Null(await LedgerChainTests.Verify(sql, tenant));
    }

    [Fact]
    public async Task ANameInTheTableListThatIsNotALedgerTableIsIgnoredByTheDigestAndRefusedByVerify()
    {
        var tenant = Guid.NewGuid();
        await LedgerChainTests.WriteEvent(sql, tenant);
        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand("""INSERT INTO ledger."Tables" ("Name", "Columns") VALUES ('patients.Patients', '{Id}'), ('no.such_table', '{Id}')""", admin).ExecuteNonQueryAsync();

        var (store, log) = (new MemoryDigestStore(), new CapturingLogger<LedgerDigests>());
        await new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log).PublishAsync(); // does not throw on the stray names
        Assert.Contains(tenant.ToString(), System.Text.Encoding.UTF8.GetString(Assert.Single(store.Blobs).Value));
        Assert.DoesNotContain("patients.Patients", System.Text.Encoding.UTF8.GetString(store.Blobs.Single().Value));

        await using var conn = await sql.OpenAs("app_user", tenant);
        await using var cmd = new NpgsqlCommand("SELECT ledger.verify_chain('patients.Patients', @t)", conn);
        cmd.Parameters.AddWithValue("t", tenant);
        Assert.Contains("not a ledger table", (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync())).MessageText, StringComparison.Ordinal);
    }
}
