using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("audit", "AuditEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON audit."AuditEvents"; DROP POLICY tenant_insert ON audit."AuditEvents"; DROP POLICY tenant_update ON audit."AuditEvents"; DROP POLICY tenant_delete ON audit."AuditEvents"; ALTER TABLE audit."AuditEvents" NO FORCE ROW LEVEL SECURITY; ALTER TABLE audit."AuditEvents" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
