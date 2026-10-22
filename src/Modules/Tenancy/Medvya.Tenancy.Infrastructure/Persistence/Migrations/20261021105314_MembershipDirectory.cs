using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdleTimeoutMinutes",
                schema: "tenancy",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_IdleTimeoutMinutes",
                schema: "tenancy",
                table: "Tenants",
                sql: "IdleTimeoutMinutes BETWEEN 1 AND 480");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                schema: "tenancy",
                table: "Memberships",
                column: "UserId");

            // Facility picker (MDV-P0-012). A connection with SESSION_CONTEXT('user_id') and NO tenant (MembershipDirectory)
            // may read that user's ACTIVE memberships, and the tenants and facilities they belong to; nothing else.
            // Tenant-scoped connections never set user_id, so for them every predicate reduces to the tenant rule as before.
            // Only FILTER predicates change: BLOCK predicates stay tenant-only, so a connection without a tenant still writes nothing.
            // SCHEMABINDING on tenancy.Memberships: changing its UserId/TenantId/FacilityId/Status columns means re-creating these functions.
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_membership_access(@tenant_id uniqueidentifier, @user_id uniqueidentifier, @status nvarchar(20))
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed
                WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier)
                   OR (SESSION_CONTEXT(N'tenant_id') IS NULL
                       AND @user_id = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)
                       AND @status = N'Active');
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_facility_access(@tenant_id uniqueidentifier, @facility_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed
                WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier)
                   OR (SESSION_CONTEXT(N'tenant_id') IS NULL AND EXISTS (
                       SELECT 1 FROM tenancy.Memberships m
                       WHERE m.FacilityId = @facility_id AND m.Status = N'Active'
                         AND m.UserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)));
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_tenant_row_access(@id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed
                WHERE @id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier)
                   OR (SESSION_CONTEXT(N'tenant_id') IS NULL AND EXISTS (
                       SELECT 1 FROM tenancy.Memberships m
                       WHERE m.TenantId = @id AND m.Status = N'Active'
                         AND m.UserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)));
                """);
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ALTER FILTER PREDICATE security.fn_tenant_row_access(Id) ON tenancy.Tenants,
                    ALTER FILTER PREDICATE security.fn_facility_access(TenantId, Id) ON tenancy.Facilities,
                    ALTER FILTER PREDICATE security.fn_membership_access(TenantId, UserId, Status) ON tenancy.Memberships;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ALTER FILTER PREDICATE security.fn_tenant_access(Id) ON tenancy.Tenants,
                    ALTER FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Facilities,
                    ALTER FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Memberships;
                """);
            migrationBuilder.Sql("DROP FUNCTION security.fn_tenant_row_access;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_facility_access;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_membership_access;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_IdleTimeoutMinutes",
                schema: "tenancy",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_UserId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "IdleTimeoutMinutes",
                schema: "tenancy",
                table: "Tenants");
        }
    }
}
