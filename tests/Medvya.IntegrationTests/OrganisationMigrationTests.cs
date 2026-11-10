using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-068: the migration that introduces the organisation state and owner attestation, run over a database that holds tenants with a
/// verified, a pending and a rejected licence, as the real (non-bypassing) owner role under row-level security. Own database: it migrates by hand.
/// </summary>
public sealed class OrganisationMigrationTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    const string Before = "20261107170000_TenantCreatedAt";

    [Fact]
    public async Task ExistingTenantsBecomeApprovedAndEveryLicenceIsAttestedByTheOwnerWithAnAuditRow()
    {
        await using var conn = await NewDatabase();
        await new NpgsqlCommand("SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();
        var none = new PostgresFixture.FixedTenant(null);
        await using (var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(conn).Options, none)) await audit.Database.MigrateAsync();
        await using (var tenancy = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(conn).Options, none)) await tenancy.Database.MigrateAsync(Before);

        var (tenant, owner, facility) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (verified, pending, rejected) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (emptyTenant, ownerless) = (Guid.NewGuid(), Guid.NewGuid());
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync(); // the superuser seeds the old shape; the migration itself runs as the owner
        await Run(conn, $"""INSERT INTO tenancy."Tenants" ("Id", "Name", "CountryCode", "ResidencyRegion") VALUES ('{tenant}', 'Pilot Walk Clinic (synthetic)', 'IN', 'centralindia'), ('{emptyTenant}', 'Empty (synthetic)', 'IN', 'centralindia'), ('{ownerless}', 'No Owner (synthetic)', 'IN', 'centralindia')""");
        await Run(conn, $"""INSERT INTO tenancy."Facilities" ("Id", "TenantId", "Name", "TimeZone") VALUES ('{facility}', '{tenant}', 'Main (synthetic)', 'Asia/Kolkata')""");
        await Run(conn, $"""
            INSERT INTO tenancy."Memberships" ("Id", "TenantId", "UserId", "FacilityId", "Role", "Status") VALUES
              ('{Guid.NewGuid()}', '{tenant}', '{owner}', '{facility}', 'PracticeAdmin', 'Active'),
              ('{verified}', '{tenant}', '{Guid.NewGuid()}', '{facility}', 'HospitalDoctor', 'Active'),
              ('{pending}', '{tenant}', '{Guid.NewGuid()}', '{facility}', 'HospitalDoctor', 'Active'),
              ('{rejected}', '{tenant}', '{Guid.NewGuid()}', '{facility}', 'HospitalDoctor', 'Active')
            """);
        await Run(conn, $"""
            INSERT INTO tenancy."DoctorLicences" ("MembershipId", "TenantId", "Authority", "Number", "SubmittedAt", "VerifiedAt", "VerifiedBy", "RejectedAt", "RejectionReason") VALUES
              ('{verified}', '{tenant}', 'TNMC', '84213', now(), now(), '{Guid.NewGuid()}', NULL, NULL),
              ('{pending}', '{tenant}', 'TNMC', '84214', now(), NULL, NULL, NULL, NULL),
              ('{rejected}', '{tenant}', 'TNMC', '84215', now(), NULL, NULL, now(), 'Not found')
            """);

        await new NpgsqlCommand("SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();
        await using (var tenancy = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(conn).Options, none)) await tenancy.Database.MigrateAsync();
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync();

        Assert.Equal(3, await Count(conn, """SELECT count(*) FROM tenancy."Tenants" WHERE "State" = 'Approved'"""));
        Assert.Equal(3, await Count(conn, $"""SELECT count(*) FROM tenancy."DoctorLicences" WHERE "AttestedBy" = '{owner}' AND "AttestedAt" > now() - interval '1 hour'"""));
        Assert.Equal(3, await Count(conn, """SELECT count(*) FROM tenancy."SettingsChanges" WHERE "Section" = 'organisation' AND "After" LIKE '%MDV-P1-068%'"""));
        Assert.Equal(1, await Count(conn, $"""SELECT count(*) FROM tenancy."SettingsChanges" WHERE "Section" = 'doctor' AND "ResourceId" = '{verified}' AND ("Before"::jsonb)->>'licence' = 'Verified' AND "ActorUserId" = '{owner}'"""));
        Assert.Equal(1, await Count(conn, $"""SELECT count(*) FROM tenancy."SettingsChanges" WHERE "Section" = 'doctor' AND "ResourceId" = '{pending}' AND ("Before"::jsonb)->>'licence' = 'Pending'"""));
        Assert.Equal(1, await Count(conn, $"""SELECT count(*) FROM tenancy."SettingsChanges" WHERE "Section" = 'doctor' AND "ResourceId" = '{rejected}' AND ("Before"::jsonb)->>'licence' = 'Rejected'"""));
        Assert.Equal(0, await Count(conn, """SELECT count(*) FROM information_schema.columns WHERE table_schema = 'tenancy' AND table_name = 'DoctorLicences' AND column_name IN ('VerifiedAt', 'VerifiedBy', 'RejectedAt', 'RejectionReason')"""));
    }

    [Fact]
    public async Task AnEmptyDatabaseMigratesAndRunningTheMigrationsAgainChangesNothing()
    {
        await using var conn = await NewDatabase();
        var none = new PostgresFixture.FixedTenant(null);
        await new NpgsqlCommand("SET ROLE medvya_owner", conn).ExecuteNonQueryAsync();
        await using (var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(conn).Options, none)) await audit.Database.MigrateAsync();
        for (var run = 0; run < 2; run++)
        {
            await using var tenancy = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(conn).Options, none);
            await tenancy.Database.MigrateAsync();
        }
        await new NpgsqlCommand("RESET ROLE", conn).ExecuteNonQueryAsync();
        Assert.Equal(0, await Count(conn, """SELECT count(*) FROM tenancy."Tenants" """));
        Assert.Equal(0, await Count(conn, """SELECT count(*) FROM tenancy."SettingsChanges" """));
    }

    async Task<NpgsqlConnection> NewDatabase()
    {
        var gate = new NpgsqlConnectionStringBuilder(sql.AdminConnectionString) { Database = "org_" + Guid.NewGuid().ToString("N")[..8], Pooling = false }.ConnectionString;
        await using (var admin = await sql.OpenAs("dbo"))
            await new NpgsqlCommand($"CREATE DATABASE {new NpgsqlConnectionStringBuilder(gate).Database}", admin).ExecuteNonQueryAsync();
        var conn = new NpgsqlConnection(gate);
        await conn.OpenAsync();
        await Medvya.DbMigrate.DatabaseMigrator.ApplyRolesAsync(conn);
        return conn;
    }

    static async Task Run(NpgsqlConnection conn, string text) => await new NpgsqlCommand(text, conn).ExecuteNonQueryAsync();

    static async Task<long> Count(NpgsqlConnection conn, string text) => (long)(await new NpgsqlCommand(text, conn).ExecuteScalarAsync())!;
}
