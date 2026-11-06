using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("tenancy", "AccessReviewDecisions");
            migrationBuilder.Tenant("tenancy", "AccessReviews");
            migrationBuilder.Tenant("tenancy", "Closures");
            migrationBuilder.Tenant("tenancy", "CustomRoles");
            migrationBuilder.Tenant("tenancy", "DoctorLicences");
            migrationBuilder.Tenant("tenancy", "DoctorProfiles");
            migrationBuilder.Tenant("tenancy", "FeatureFlags");
            migrationBuilder.Tenant("tenancy", "Invitations");
            migrationBuilder.Tenant("tenancy", "OperatingHours");
            migrationBuilder.Tenant("tenancy", "PracticeDefaults");
            migrationBuilder.Tenant("tenancy", "Profiles");
            migrationBuilder.Tenant("tenancy", "SettingsChanges");
            migrationBuilder.Tenant("tenancy", "TermsAcceptances");
            // Facility picker (MDV-P0-012): with a user and no tenant, read only that user's Active memberships and the facilities and tenants they belong to.
            migrationBuilder.Tenant("tenancy", "Memberships", alsoReadable: $"\"UserId\" = {RowLevelSecurity.CurrentUser} AND \"Status\" = 'Active'");
            migrationBuilder.Tenant("tenancy", "Facilities", alsoReadable: $"EXISTS (SELECT 1 FROM tenancy.\"Memberships\" m WHERE m.\"FacilityId\" = \"Facilities\".\"Id\" AND m.\"UserId\" = {RowLevelSecurity.CurrentUser} AND m.\"Status\" = 'Active')");
            migrationBuilder.Tenant("tenancy", "Tenants", "Id", $"EXISTS (SELECT 1 FROM tenancy.\"Memberships\" m WHERE m.\"TenantId\" = \"Tenants\".\"Id\" AND m.\"UserId\" = {RowLevelSecurity.CurrentUser} AND m.\"Status\" = 'Active')");
            // shortcut: TenantIndex lists tenant ids for the access-review job, which runs with no tenant; it holds ids only, so no policy.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."AccessReviewDecisions"; DROP POLICY tenant_insert ON tenancy."AccessReviewDecisions"; DROP POLICY tenant_update ON tenancy."AccessReviewDecisions"; DROP POLICY tenant_delete ON tenancy."AccessReviewDecisions"; ALTER TABLE tenancy."AccessReviewDecisions" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."AccessReviewDecisions" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."AccessReviews"; DROP POLICY tenant_insert ON tenancy."AccessReviews"; DROP POLICY tenant_update ON tenancy."AccessReviews"; DROP POLICY tenant_delete ON tenancy."AccessReviews"; ALTER TABLE tenancy."AccessReviews" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."AccessReviews" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Closures"; DROP POLICY tenant_insert ON tenancy."Closures"; DROP POLICY tenant_update ON tenancy."Closures"; DROP POLICY tenant_delete ON tenancy."Closures"; ALTER TABLE tenancy."Closures" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Closures" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."CustomRoles"; DROP POLICY tenant_insert ON tenancy."CustomRoles"; DROP POLICY tenant_update ON tenancy."CustomRoles"; DROP POLICY tenant_delete ON tenancy."CustomRoles"; ALTER TABLE tenancy."CustomRoles" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."CustomRoles" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."DoctorLicences"; DROP POLICY tenant_insert ON tenancy."DoctorLicences"; DROP POLICY tenant_update ON tenancy."DoctorLicences"; DROP POLICY tenant_delete ON tenancy."DoctorLicences"; ALTER TABLE tenancy."DoctorLicences" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."DoctorLicences" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."DoctorProfiles"; DROP POLICY tenant_insert ON tenancy."DoctorProfiles"; DROP POLICY tenant_update ON tenancy."DoctorProfiles"; DROP POLICY tenant_delete ON tenancy."DoctorProfiles"; ALTER TABLE tenancy."DoctorProfiles" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."DoctorProfiles" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."FeatureFlags"; DROP POLICY tenant_insert ON tenancy."FeatureFlags"; DROP POLICY tenant_update ON tenancy."FeatureFlags"; DROP POLICY tenant_delete ON tenancy."FeatureFlags"; ALTER TABLE tenancy."FeatureFlags" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."FeatureFlags" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Invitations"; DROP POLICY tenant_insert ON tenancy."Invitations"; DROP POLICY tenant_update ON tenancy."Invitations"; DROP POLICY tenant_delete ON tenancy."Invitations"; ALTER TABLE tenancy."Invitations" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Invitations" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."OperatingHours"; DROP POLICY tenant_insert ON tenancy."OperatingHours"; DROP POLICY tenant_update ON tenancy."OperatingHours"; DROP POLICY tenant_delete ON tenancy."OperatingHours"; ALTER TABLE tenancy."OperatingHours" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."OperatingHours" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."PracticeDefaults"; DROP POLICY tenant_insert ON tenancy."PracticeDefaults"; DROP POLICY tenant_update ON tenancy."PracticeDefaults"; DROP POLICY tenant_delete ON tenancy."PracticeDefaults"; ALTER TABLE tenancy."PracticeDefaults" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."PracticeDefaults" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Profiles"; DROP POLICY tenant_insert ON tenancy."Profiles"; DROP POLICY tenant_update ON tenancy."Profiles"; DROP POLICY tenant_delete ON tenancy."Profiles"; ALTER TABLE tenancy."Profiles" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Profiles" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."SettingsChanges"; DROP POLICY tenant_insert ON tenancy."SettingsChanges"; DROP POLICY tenant_update ON tenancy."SettingsChanges"; DROP POLICY tenant_delete ON tenancy."SettingsChanges"; ALTER TABLE tenancy."SettingsChanges" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."SettingsChanges" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."TermsAcceptances"; DROP POLICY tenant_insert ON tenancy."TermsAcceptances"; DROP POLICY tenant_update ON tenancy."TermsAcceptances"; DROP POLICY tenant_delete ON tenancy."TermsAcceptances"; ALTER TABLE tenancy."TermsAcceptances" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."TermsAcceptances" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Memberships"; DROP POLICY tenant_insert ON tenancy."Memberships"; DROP POLICY tenant_update ON tenancy."Memberships"; DROP POLICY tenant_delete ON tenancy."Memberships"; ALTER TABLE tenancy."Memberships" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Memberships" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Facilities"; DROP POLICY tenant_insert ON tenancy."Facilities"; DROP POLICY tenant_update ON tenancy."Facilities"; DROP POLICY tenant_delete ON tenancy."Facilities"; ALTER TABLE tenancy."Facilities" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Facilities" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON tenancy."Tenants"; DROP POLICY tenant_insert ON tenancy."Tenants"; DROP POLICY tenant_update ON tenancy."Tenants"; DROP POLICY tenant_delete ON tenancy."Tenants"; ALTER TABLE tenancy."Tenants" NO FORCE ROW LEVEL SECURITY; ALTER TABLE tenancy."Tenants" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
