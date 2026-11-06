using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("patients", "ConsentReceipts");
            migrationBuilder.Tenant("patients", "Identifiers");
            migrationBuilder.Tenant("patients", "MergeRequests");
            migrationBuilder.Tenant("patients", "MrnCounters");
            migrationBuilder.Tenant("patients", "Patients");
            migrationBuilder.Tenant("patients", "PayerCoverages");
            migrationBuilder.Tenant("patients", "Relationships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."ConsentReceipts"; DROP POLICY tenant_insert ON patients."ConsentReceipts"; DROP POLICY tenant_update ON patients."ConsentReceipts"; DROP POLICY tenant_delete ON patients."ConsentReceipts"; ALTER TABLE patients."ConsentReceipts" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."ConsentReceipts" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."Identifiers"; DROP POLICY tenant_insert ON patients."Identifiers"; DROP POLICY tenant_update ON patients."Identifiers"; DROP POLICY tenant_delete ON patients."Identifiers"; ALTER TABLE patients."Identifiers" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."Identifiers" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."MergeRequests"; DROP POLICY tenant_insert ON patients."MergeRequests"; DROP POLICY tenant_update ON patients."MergeRequests"; DROP POLICY tenant_delete ON patients."MergeRequests"; ALTER TABLE patients."MergeRequests" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."MergeRequests" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."MrnCounters"; DROP POLICY tenant_insert ON patients."MrnCounters"; DROP POLICY tenant_update ON patients."MrnCounters"; DROP POLICY tenant_delete ON patients."MrnCounters"; ALTER TABLE patients."MrnCounters" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."MrnCounters" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."Patients"; DROP POLICY tenant_insert ON patients."Patients"; DROP POLICY tenant_update ON patients."Patients"; DROP POLICY tenant_delete ON patients."Patients"; ALTER TABLE patients."Patients" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."Patients" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."PayerCoverages"; DROP POLICY tenant_insert ON patients."PayerCoverages"; DROP POLICY tenant_update ON patients."PayerCoverages"; DROP POLICY tenant_delete ON patients."PayerCoverages"; ALTER TABLE patients."PayerCoverages" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."PayerCoverages" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON patients."Relationships"; DROP POLICY tenant_insert ON patients."Relationships"; DROP POLICY tenant_update ON patients."Relationships"; DROP POLICY tenant_delete ON patients."Relationships"; ALTER TABLE patients."Relationships" NO FORCE ROW LEVEL SECURITY; ALTER TABLE patients."Relationships" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
