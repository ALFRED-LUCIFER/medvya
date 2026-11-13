using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P1-107: the practice owner, so that only an owner offboards an owner or a doctor. Existing practices: the person who set it up holds an
    /// owner-doctor or practice-admin membership and was never invited into that organisation (every later colleague joins by invitation); each of
    /// their memberships there is marked. The backfill reads every tenant, so it lifts FORCE for its own statement only, inside the migration's
    /// transaction (the table owner then reads past the tenant policy) and puts it back before the commit. Forward-only: no Down.
    /// </summary>
    public partial class MembershipOwner : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOwner",
                schema: "tenancy",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                ALTER TABLE tenancy."Memberships" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy."Invitations" NO FORCE ROW LEVEL SECURITY;
                UPDATE tenancy."Memberships" m SET "IsOwner" = true
                WHERE m."UserId" IS NOT NULL AND m."Role" IN ('SoloDoctor', 'PracticeAdmin')
                  AND NOT EXISTS (
                    SELECT 1 FROM tenancy."Memberships" o JOIN tenancy."Invitations" i ON i."MembershipId" = o."Id"
                    WHERE o."TenantId" = m."TenantId" AND o."UserId" = m."UserId");
                ALTER TABLE tenancy."Invitations" FORCE ROW LEVEL SECURITY;
                ALTER TABLE tenancy."Memberships" FORCE ROW LEVEL SECURITY;
                """);
        }
    }
}
