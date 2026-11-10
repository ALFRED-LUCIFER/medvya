using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P1-063, P1-065, P1-068 (ADR-016): the organisation state, and the owner's attestation of each doctor licence in place of Medvya's
    /// verification. Every tenant that exists now becomes Approved (the column default) and every licence is attested by the tenant's owner
    /// (the earliest solo-doctor or practice-admin membership) as of now; both are recorded in SettingsChanges, naming this migration.
    /// The data steps run per tenant with the tenant setting on, so row-level security applies to them too. Safe to run again: the audit rows
    /// are written once and the update only restates the same attestation. Forward-only: no Down.
    /// </summary>
    public partial class OrganisationStateAndAttestation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "State", schema: "tenancy", table: "Tenants", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Approved");
            migrationBuilder.AddColumn<Guid>(name: "AttestedBy", schema: "tenancy", table: "DoctorLicences", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "AttestedAt", schema: "tenancy", table: "DoctorLicences", type: "timestamp with time zone", nullable: false,
                defaultValue: new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero));

            migrationBuilder.Sql("""
                DO $$
                DECLARE t uuid; owner uuid;
                BEGIN
                  FOR t IN SELECT "TenantId" FROM tenancy."TenantIndex" LOOP
                    PERFORM set_config('medvya.tenant_id', t::text, true);
                    owner := (SELECT "UserId" FROM tenancy."Memberships"
                              WHERE "TenantId" = t AND "Role" IN ('SoloDoctor', 'PracticeAdmin') AND "UserId" IS NOT NULL ORDER BY "Id" LIMIT 1);
                    INSERT INTO tenancy."SettingsChanges" ("Id", "TenantId", "ActorUserId", "Section", "ResourceId", "Before", "After", "OccurredAt")
                      SELECT gen_random_uuid(), t, NULL, 'organisation', t, NULL, '{"state":"Approved","by":"migration MDV-P1-068"}', now()
                      WHERE NOT EXISTS (SELECT 1 FROM tenancy."SettingsChanges" WHERE "Section" = 'organisation' AND "ResourceId" = t AND "After" LIKE '%MDV-P1-068%');
                    INSERT INTO tenancy."SettingsChanges" ("Id", "TenantId", "ActorUserId", "Section", "ResourceId", "Before", "After", "OccurredAt")
                      SELECT gen_random_uuid(), t, owner, 'doctor', l."MembershipId",
                             '{"licence":"' || CASE WHEN l."VerifiedAt" IS NOT NULL THEN 'Verified' WHEN l."RejectedAt" IS NOT NULL THEN 'Rejected' ELSE 'Pending' END || '"}',
                             '{"licence":"Attested","by":"migration MDV-P1-068"}', now()
                      FROM tenancy."DoctorLicences" l
                      WHERE l."TenantId" = t AND l."AttestedAt" < '2000-01-01'
                        AND NOT EXISTS (SELECT 1 FROM tenancy."SettingsChanges" c WHERE c."Section" = 'doctor' AND c."ResourceId" = l."MembershipId" AND c."After" LIKE '%MDV-P1-068%');
                    UPDATE tenancy."DoctorLicences" SET "AttestedBy" = owner, "AttestedAt" = now() WHERE "TenantId" = t AND "AttestedAt" < '2000-01-01';
                  END LOOP;
                  PERFORM set_config('medvya.tenant_id', '', true);
                END $$;
                """);

            migrationBuilder.DropColumn(name: "VerifiedAt", schema: "tenancy", table: "DoctorLicences");
            migrationBuilder.DropColumn(name: "VerifiedBy", schema: "tenancy", table: "DoctorLicences");
            migrationBuilder.DropColumn(name: "RejectedAt", schema: "tenancy", table: "DoctorLicences");
            migrationBuilder.DropColumn(name: "RejectionReason", schema: "tenancy", table: "DoctorLicences");
        }
    }
}
