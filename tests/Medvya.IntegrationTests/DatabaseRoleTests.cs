using Microsoft.Data.SqlClient;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-008: least-privilege database roles (infra/sql/roles.sql). Each role user is impersonated with EXECUTE AS USER,
/// the local equivalent of the Entra managed identity mapped to that role in Azure. The fixture already proves the
/// migration role can run every migration.
/// </summary>
public class DatabaseRoleTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    // Errors SQL Server raises when a permission is missing (229 object, 262 statement; 1088/3701/15151/33268 "does not exist or no permission").
    static readonly int[] PermissionDenied = [229, 262, 1088, 3701, 15151, 33268];

    async Task Run(string user, string statement, Guid? tenant = null)
    {
        await using var conn = await sql.OpenAs(user, tenant);
        await using var cmd = new SqlCommand(statement, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task Denied(string user, string statement)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => Run(user, statement));
        Assert.True(PermissionDenied.Contains(ex.Number), $"{ex.Number}: {ex.Message}"); // refused by permission, not by some other rule (ledger, RLS, missing object)
    }

    [Theory]
    [InlineData("CREATE TABLE tenancy.Injected (Id int)")]
    [InlineData("ALTER TABLE tenancy.Tenants ADD Injected int NULL")]
    [InlineData("DROP TABLE tenancy.Memberships")]
    [InlineData("CREATE TABLE audit.Injected (Id int)")]
    [InlineData("DROP SECURITY POLICY security.tenant_isolation")]
    [InlineData("ALTER SECURITY POLICY audit.tenant_isolation WITH (STATE = OFF)")]
    [InlineData("ALTER SECURITY POLICY security.patients_tenant_isolation WITH (STATE = OFF)")]
    [InlineData("ALTER TABLE patients.Patients ALTER COLUMN Mobile DROP MASKED")]
    [InlineData("DROP TABLE patients.Identifiers")]
    [InlineData("ALTER SECURITY POLICY security.billing_tenant_isolation WITH (STATE = OFF)")]
    [InlineData("DROP TABLE billing.DocSequences")]
    [InlineData("ALTER SECURITY POLICY security.scheduling_tenant_isolation WITH (STATE = OFF)")]
    [InlineData("DROP TABLE scheduling.Templates")]
    public Task AppCannotChangeTheSchema(string ddl) => Denied("app_user", ddl);

    [Fact]
    public async Task MigratorCanChangeTheSchemaButNotTouchModuleData()
    {
        await Run("migrator_user", "CREATE TABLE tenancy.MigratorProbe (Id int); ALTER TABLE tenancy.MigratorProbe ADD Name nvarchar(10) NULL; DROP TABLE tenancy.MigratorProbe;");
        await Denied("migrator_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Denied("migrator_user", "DELETE FROM tenancy.Tenants");
        await Denied("migrator_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Denied("migrator_user", "INSERT INTO audit.AuditEvents (Id, TenantId, Action, ResourceType, Purpose, Outcome, OccurredAt) VALUES (NEWID(), NEWID(), N'x', N'x', N'x', N'x', SYSDATETIMEOFFSET())");
    }

    [Fact]
    public async Task ModuleRolesCannotReadEachOthersSchema()
    {
        await Run("tenancy_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Run("audit_user", "SELECT TOP 1 Id FROM audit.AuditEvents");
        await Denied("tenancy_user", "SELECT TOP 1 Id FROM audit.AuditEvents");
        await Denied("audit_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Run("identity_user", "SELECT TOP 1 Id FROM [identity].Users");
        await Denied("identity_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Denied("tenancy_user", "SELECT TOP 1 Id FROM [identity].Users");
        await Denied("identity_user", "DELETE FROM [identity].Users");
        await Run("patients_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Denied("patients_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Denied("patients_user", "SELECT TOP 1 Id FROM audit.AuditEvents");
        await Denied("tenancy_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Run("billing_user", "SELECT TOP 1 Id FROM billing.DocSequences");
        await Denied("billing_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Denied("billing_user", "SELECT TOP 1 Id FROM tenancy.Tenants");
        await Denied("patients_user", "SELECT TOP 1 Id FROM billing.DocSequences");
        await Denied("migrator_user", "SELECT TOP 1 Id FROM billing.DocSequences");
        await Run("scheduling_user", "SELECT TOP 1 Id FROM scheduling.Templates");
        await Denied("scheduling_user", "SELECT TOP 1 Id FROM tenancy.Memberships");
        await Denied("scheduling_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Denied("patients_user", "SELECT TOP 1 Id FROM scheduling.Templates");
        await Denied("reporting_user", "SELECT TOP 1 Id FROM scheduling.Templates");
        await Run("leads_user", "SELECT TOP 1 Id FROM leads.DemoRequests");
        await Denied("leads_user", "SELECT TOP 1 Id FROM patients.Patients");
        await Denied("leads_user", "DELETE FROM leads.DemoRequests");
        await Denied("leads_user", "UPDATE leads.DemoRequests SET Name = N'x'");
        await Denied("patients_user", "SELECT TOP 1 Id FROM leads.DemoRequests");
    }

    [Fact]
    public Task BillingRoleCannotDeleteANumberSeries() => Denied("billing_user", "DELETE FROM billing.DocSequences"); // would restart legal numbering

    [Fact]
    public async Task AuditRoleCanAppendButNeverChangeOrDelete()
    {
        var tenant = Guid.NewGuid();
        await Run("audit_user", Insert, tenant);

        // The ledger rejects UPDATE/DELETE before the permission check (AuditLedgerTests), so ask SQL Server for the permission itself.
        await using var conn = await sql.OpenAs("audit_user");
        await using var cmd = new SqlCommand("SELECT HAS_PERMS_BY_NAME(N'audit.AuditEvents', N'OBJECT', N'UPDATE') + HAS_PERMS_BY_NAME(N'audit.AuditEvents', N'OBJECT', N'DELETE') + HAS_PERMS_BY_NAME(N'audit.AuditEvents', N'OBJECT', N'INSERT')", conn);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!); // INSERT only
    }

    [Fact]
    public async Task ReportingRoleIsReadOnly()
    {
        await Run("reporting_user", "SELECT TOP 1 Id FROM tenancy.Facilities");
        await Denied("reporting_user", "INSERT INTO tenancy.Tenants (Id, Name, CountryCode, ResidencyRegion) VALUES (NEWID(), N'x', N'IN', N'centralindia')");
        await Denied("reporting_user", "UPDATE tenancy.Tenants SET Name = N'x'");
        await Denied("reporting_user", "DELETE FROM tenancy.Tenants");
        await Denied("reporting_user", "SELECT TOP 1 Id FROM audit.AuditEvents");
        await Denied("reporting_user", "SELECT TOP 1 DisplayName FROM [identity].Users");
        await Denied("reporting_user", "CREATE TABLE tenancy.Injected (Id int)");
        await Run("reporting_user", "SELECT TOP 1 Id, FullName FROM patients.Patients"); // masked: PatientRegistryTests
        await Denied("reporting_user", "UPDATE patients.Patients SET Mobile = N'x'");
        await Denied("reporting_user", "DELETE FROM patients.Identifiers");
    }

    [Fact]
    public async Task AppRoleWorksThroughRowLevelSecurity()
    {
        // The RLS predicate functions need no EXECUTE/SELECT grant: the app writes and reads its tenant's rows, and only those.
        var tenant = Guid.NewGuid();
        await Run("app_user", "INSERT INTO tenancy.Tenants (Id, Name, CountryCode, ResidencyRegion) VALUES (CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier), N'Iyer Family Clinic (synthetic)', N'IN', N'centralindia');" + Insert, tenant);

        await using var conn = await sql.OpenAs("app_user", tenant);
        await using var count = new SqlCommand("SELECT (SELECT COUNT(*) FROM tenancy.Tenants WHERE Id = @t) + (SELECT COUNT(*) FROM audit.AuditEvents WHERE TenantId = @t)", conn);
        count.Parameters.AddWithValue("@t", tenant);
        Assert.Equal(2, (int)(await count.ExecuteScalarAsync())!);

        var blocked = await Assert.ThrowsAsync<SqlException>(() => Run("app_user", "INSERT INTO tenancy.Tenants (Id, Name, CountryCode, ResidencyRegion) VALUES (NEWID(), N'x', N'AE', N'uaenorth')", tenant));
        Assert.Contains("block predicate", blocked.Message, StringComparison.Ordinal);
    }

    const string Insert = """
        INSERT INTO audit.AuditEvents (Id, TenantId, Action, ResourceType, Purpose, Outcome, OccurredAt)
        VALUES (NEWID(), CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier), N'patient.read', N'patient', N'treatment', N'Allowed', SYSDATETIMEOFFSET());
        """;
}
