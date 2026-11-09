using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-008: least-privilege database roles (infra/sql/roles.sql). Each role user is impersonated with EXECUTE AS USER,
/// the local equivalent of the Entra managed identity mapped to that role in Azure. The fixture already proves the
/// migration role can run every migration.
/// </summary>
public class DatabaseRoleTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    // SQLSTATE 42501: insufficient_privilege.

    async Task Run(string user, string statement, Guid? tenant = null)
    {
        await using var conn = await sql.OpenAs(user, tenant);
        await using var cmd = new NpgsqlCommand(statement, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task Denied(string user, string statement)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => Run(user, statement));
        Assert.True(ex.SqlState == PostgresErrorCodes.InsufficientPrivilege, $"{ex.SqlState}: {ex.Message}"); // refused by permission, not by some other rule (trigger, RLS, missing object)
    }

    // MDV-P0-008: a deploy is repeatable. The second migrate finds nothing pending.
    [Fact]
    public async Task MigrateIsRepeatable()
    {
        await using var migrator = await sql.OpenAs("migrator_user");
        await Medvya.DbMigrate.DatabaseMigrator.ApplyAsync(migrator);
    }

    // MDV-P0-042: the deploy job maps the app's managed identity IN ROLE medvya_app. Locally the Entra login already exists (Azure creates it
    // with pgaadauth_create_principal_with_oid), so this proves the grant, that a re-run changes nothing, and that no other role can be handed out.
    [Fact]
    public async Task AnEntraLoginIsMappedToTheAppRoleOnlyAndRepeatably()
    {
        await using var admin = new NpgsqlConnection(sql.AdminConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand("""CREATE ROLE "app-medvya-test" LOGIN""", admin).ExecuteNonQueryAsync();
        var oid = Guid.NewGuid();
        await Medvya.DbMigrate.DatabaseMigrator.MapEntraPrincipalAsync(admin, "medvya_app", "app-medvya-test", oid);
        await Medvya.DbMigrate.DatabaseMigrator.MapEntraPrincipalAsync(admin, "medvya_app", "app-medvya-test", oid);
        Assert.Equal(true, await new NpgsqlCommand("SELECT pg_has_role('app-medvya-test', 'medvya_app', 'MEMBER')", admin).ExecuteScalarAsync());
        Assert.Equal(false, await new NpgsqlCommand("SELECT pg_has_role('app-medvya-test', 'medvya_owner', 'MEMBER')", admin).ExecuteScalarAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => Medvya.DbMigrate.DatabaseMigrator.MapEntraPrincipalAsync(admin, "medvya_owner", "app-medvya-test", oid));
    }

    [Theory]
    [InlineData("CREATE TABLE tenancy.\"Injected\" (\"Id\" int)")]
    [InlineData("ALTER TABLE tenancy.\"Tenants\" ADD COLUMN \"Injected\" int")]
    [InlineData("DROP TABLE tenancy.\"Memberships\"")]
    [InlineData("CREATE TABLE audit.\"Injected\" (\"Id\" int)")]
    [InlineData("ALTER TABLE tenancy.\"Tenants\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE audit.\"AuditEvents\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE patients.\"Patients\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE patients.\"Patients\" ALTER COLUMN \"Mobile\" TYPE text")]
    [InlineData("DROP TABLE patients.\"Identifiers\"")]
    [InlineData("ALTER TABLE billing.\"DocSequences\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("DROP TABLE billing.\"DocSequences\"")]
    [InlineData("ALTER TABLE billing.\"DocSequences\" DISABLE TRIGGER \"TR_DocSequences_OnlyMoveForward\"")]
    [InlineData("ALTER TABLE scheduling.\"Appointments\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("DROP TABLE scheduling.\"Templates\"")]
    public Task AppCannotChangeTheSchema(string ddl) => Denied("app_user", ddl);

    // MDV-P0-008: the deploy creates the Hangfire tables as the migration role (the app may not); the app then works in them but cannot change them.
    [Fact]
    public async Task DeployCreatesJobTablesThatTheAppCanUseButNotAlter()
    {
        await Run("app_user", "INSERT INTO hangfire.set (key, score, value) VALUES ('probe', 0, 'probe'); DELETE FROM hangfire.set WHERE key = 'probe';");
        await Denied("app_user", "ALTER TABLE hangfire.set ADD COLUMN \"Injected\" int");
        await Denied("reporting_user", "SELECT key FROM hangfire.set LIMIT 1");
    }

    [Fact]
    public async Task MigratorCanChangeTheSchemaButNotTouchModuleData()
    {
        await Run("migrator_user", "CREATE TABLE tenancy.\"MigratorProbe\" (\"Id\" int); ALTER TABLE tenancy.\"MigratorProbe\" ADD COLUMN \"Name\" varchar(10); DROP TABLE tenancy.\"MigratorProbe\";");
        await Denied("migrator_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Denied("migrator_user", "DELETE FROM tenancy.\"Tenants\"");
        await Denied("migrator_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("migrator_user", "INSERT INTO audit.\"AuditEvents\" (\"Id\", \"TenantId\", \"Action\", \"ResourceType\", \"Purpose\", \"Outcome\", \"OccurredAt\") VALUES (gen_random_uuid(), gen_random_uuid(), 'x', 'x', 'x', 'x', now())");
    }

    [Fact]
    public async Task ModuleRolesCannotReadEachOthersSchema()
    {
        await Run("tenancy_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Run("audit_user", "SELECT \"Id\" FROM audit.\"AuditEvents\" LIMIT 1");
        await Denied("tenancy_user", "SELECT \"Id\" FROM audit.\"AuditEvents\" LIMIT 1");
        await Denied("audit_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Run("identity_user", "SELECT \"Id\" FROM identity.\"Users\" LIMIT 1");
        await Denied("identity_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Denied("tenancy_user", "SELECT \"Id\" FROM identity.\"Users\" LIMIT 1");
        await Denied("identity_user", "DELETE FROM identity.\"Users\"");
        await Run("patients_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("patients_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Denied("patients_user", "SELECT \"Id\" FROM audit.\"AuditEvents\" LIMIT 1");
        await Denied("tenancy_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Run("billing_user", "SELECT \"Id\" FROM billing.\"DocSequences\" LIMIT 1");
        await Denied("billing_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("billing_user", "SELECT \"Id\" FROM tenancy.\"Tenants\" LIMIT 1");
        await Denied("patients_user", "SELECT \"Id\" FROM billing.\"DocSequences\" LIMIT 1");
        await Denied("migrator_user", "SELECT \"Id\" FROM billing.\"DocSequences\" LIMIT 1");
        await Run("scheduling_user", "SELECT \"Id\" FROM scheduling.\"Templates\" LIMIT 1");
        await Denied("scheduling_user", "SELECT \"Id\" FROM tenancy.\"Memberships\" LIMIT 1");
        await Denied("scheduling_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("patients_user", "SELECT \"Id\" FROM scheduling.\"Templates\" LIMIT 1");
        await Denied("reporting_user", "SELECT \"Id\" FROM scheduling.\"Templates\" LIMIT 1");
        await Run("leads_user", "SELECT \"Id\" FROM leads.\"DemoRequests\" LIMIT 1");
        await Denied("leads_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("leads_user", "DELETE FROM leads.\"DemoRequests\"");
        await Denied("leads_user", "UPDATE leads.\"DemoRequests\" SET \"Name\" = 'x'");
        await Denied("patients_user", "SELECT \"Id\" FROM leads.\"DemoRequests\" LIMIT 1");
        await Run("notifications_user", "SELECT \"Id\" FROM notifications.\"Notifications\" LIMIT 1");
        await Denied("notifications_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("notifications_user", "SELECT \"Id\" FROM scheduling.\"Appointments\" LIMIT 1");
        await Denied("notifications_user", "DELETE FROM notifications.\"Attempts\""); // the attempts are the delivery history
        await Denied("scheduling_user", "SELECT \"Id\" FROM notifications.\"Notifications\" LIMIT 1");
        await Run("encounters_user", "SELECT \"Id\" FROM encounters.\"Encounters\" LIMIT 1");
        await Denied("encounters_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("encounters_user", "SELECT \"Id\" FROM scheduling.\"Appointments\" LIMIT 1");
        await Denied("encounters_user", "DELETE FROM encounters.\"Encounters\""); // clinical records are never deleted
        await Denied("encounters_user", "DELETE FROM encounters.\"Observations\""); // a reading is never deleted; a re-measure is a new row
        await Denied("scheduling_user", "SELECT \"Id\" FROM encounters.\"Observations\" LIMIT 1");
        await Denied("scheduling_user", "SELECT \"Id\" FROM encounters.\"Encounters\" LIMIT 1");
        await Denied("reporting_user", "SELECT \"Id\" FROM encounters.\"Encounters\" LIMIT 1");
        await Run("prescribing_user", "SELECT \"Id\" FROM prescribing.\"FormularyItems\" LIMIT 1");
        await Denied("prescribing_user", "SELECT \"Id\" FROM patients.\"Patients\" LIMIT 1");
        await Denied("prescribing_user", "SELECT \"Id\" FROM encounters.\"Encounters\" LIMIT 1");
        await Denied("prescribing_user", "DELETE FROM prescribing.\"FormularyItems\""); // a medicine is made inactive, never deleted
        await Denied("encounters_user", "SELECT \"Id\" FROM prescribing.\"OrderSets\" LIMIT 1");
        await Denied("reporting_user", "SELECT \"Id\" FROM prescribing.\"FormularyItems\" LIMIT 1");
    }

    [Fact]
    public Task BillingRoleCannotDeleteANumberSeries() => Denied("billing_user", "DELETE FROM billing.\"DocSequences\""); // would restart legal numbering

    [Fact]
    public async Task TheAppDeletesOnlyFromTheTablesItDeletesFrom()
    {
        // #879: patients, memberships, settings history, access reviews, MRN counters and visit history are never deleted.
        await using var conn = await sql.OpenAs("app_user");
        await using var cmd = new NpgsqlCommand("""
            SELECT string_agg(schemaname || '.' || tablename, ',' ORDER BY schemaname COLLATE "C", tablename COLLATE "C") FROM pg_tables
            WHERE schemaname IN ('tenancy', 'patients', 'scheduling') AND has_table_privilege(format('%I.%I', schemaname, tablename), 'DELETE')
            """, conn);
        Assert.Equal("patients.PayerCoverages,patients.Relationships,scheduling.ScheduleExceptions,scheduling.ScheduleSlots,scheduling.SlotHolds,"
            + "tenancy.Closures,tenancy.OperatingHours", await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AuditRoleCanAppendButNeverChangeOrDelete()
    {
        var tenant = Guid.NewGuid();
        await Run("audit_user", Insert, tenant);

        // Triggers refuse UPDATE/DELETE before the permission check (AuditLedgerTests), so ask Postgres for the privileges themselves.
        await using var conn = await sql.OpenAs("audit_user");
        await using var cmd = new NpgsqlCommand("SELECT has_table_privilege('audit.\"AuditEvents\"', 'UPDATE')::int + has_table_privilege('audit.\"AuditEvents\"', 'DELETE')::int + has_table_privilege('audit.\"AuditEvents\"', 'INSERT')::int", conn);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!); // INSERT only
    }

    [Fact]
    public async Task ReportingRoleIsReadOnly()
    {
        await Run("reporting_user", "SELECT \"Id\" FROM tenancy.\"Facilities\" LIMIT 1");
        await Denied("reporting_user", "INSERT INTO tenancy.\"Tenants\" (\"Id\", \"Name\", \"CountryCode\", \"ResidencyRegion\") VALUES (gen_random_uuid(), 'x', 'IN', 'centralindia')");
        await Denied("reporting_user", "UPDATE tenancy.\"Tenants\" SET \"Name\" = 'x'");
        await Denied("reporting_user", "DELETE FROM tenancy.\"Tenants\"");
        await Denied("reporting_user", "SELECT \"Id\" FROM audit.\"AuditEvents\" LIMIT 1");
        await Denied("reporting_user", "SELECT \"DisplayName\" FROM identity.\"Users\" LIMIT 1");
        await Denied("reporting_user", "CREATE TABLE tenancy.\"Injected\" (\"Id\" int)");
        await Run("reporting_user", "SELECT \"Id\", \"Initials\", \"AgeBand\" FROM reporting.\"Patients\" LIMIT 1"); // the masked view (MDV-P0-037)
        await Denied("reporting_user", "SELECT \"Id\", \"FullName\" FROM patients.\"Patients\" LIMIT 1"); // never the base table
        await Denied("reporting_user", "UPDATE reporting.\"Patients\" SET \"Mrn\" = 'x'");
        await Denied("reporting_user", "UPDATE patients.\"Patients\" SET \"Mobile\" = 'x'");
        await Denied("reporting_user", "DELETE FROM patients.\"Identifiers\"");
    }

    [Fact]
    public async Task AppRoleWorksThroughRowLevelSecurity()
    {
        // The RLS predicate functions need no EXECUTE/SELECT grant: the app writes and reads its tenant's rows, and only those.
        var tenant = Guid.NewGuid();
        await Run("app_user", "INSERT INTO tenancy.\"Tenants\" (\"Id\", \"Name\", \"CountryCode\", \"ResidencyRegion\") VALUES (current_setting('medvya.tenant_id')::uuid, 'Iyer Family Clinic (synthetic)', 'IN', 'centralindia');" + Insert, tenant);

        await using var conn = await sql.OpenAs("app_user", tenant);
        await using var count = new NpgsqlCommand("SELECT (SELECT COUNT(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = @t) + (SELECT COUNT(*) FROM audit.\"AuditEvents\" WHERE \"TenantId\" = @t)", conn);
        count.Parameters.AddWithValue("t", tenant);
        Assert.Equal(2L, await count.ExecuteScalarAsync());

        var blocked = await Assert.ThrowsAsync<PostgresException>(() => Run("app_user", "INSERT INTO tenancy.\"Tenants\" (\"Id\", \"Name\", \"CountryCode\", \"ResidencyRegion\") VALUES (gen_random_uuid(), 'x', 'AE', 'uaenorth')", tenant));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, blocked.SqlState); // row-level security refuses the row (42501)
    }

    const string Insert = """
        INSERT INTO audit."AuditEvents" ("Id", "TenantId", "Action", "ResourceType", "Purpose", "Outcome", "OccurredAt")
        VALUES (gen_random_uuid(), current_setting('medvya.tenant_id')::uuid, 'patient.read', 'patient', 'treatment', 'Allowed', now());
        """;
}
