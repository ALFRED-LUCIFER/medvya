using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicesCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Services",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Sac = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: true),
                    TaxTreatment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    GstRateBasisPoints = table.Column<int>(type: "integer", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    ConsultationKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.CheckConstraint("CK_Services_GstRate", "(\"TaxTreatment\" = 'Taxable' AND \"GstRateBasisPoints\" BETWEEN 0 AND 2800) OR (\"TaxTreatment\" = 'Exempt' AND \"GstRateBasisPoints\" IS NULL)");
                    table.CheckConstraint("CK_Services_Sac", "\"Sac\" IS NULL OR \"Sac\" ~ '^[0-9]{6}$'");
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "billing",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoicePrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReceiptPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreditNotePrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PaymentMethods = table.Column<string[]>(type: "text[]", nullable: false),
                    DiscountReasons = table.Column<string[]>(type: "text[]", nullable: false),
                    MaxDiscountPercent = table.Column<int>(type: "integer", nullable: false),
                    PaymentLinkExpiryMinutes = table.Column<int>(type: "integer", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "PriceVersions",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceVersions", x => x.Id);
                    table.CheckConstraint("CK_PriceVersions_Amount", "\"AmountMinor\" BETWEEN 0 AND 100000000");
                    table.ForeignKey(
                        name: "FK_PriceVersions_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "billing",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceVersions_ServiceId_DoctorId_EffectiveFrom",
                schema: "billing",
                table: "PriceVersions",
                columns: new[] { "ServiceId", "DoctorId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Services_TenantId_ConsultationKind",
                schema: "billing",
                table: "Services",
                columns: new[] { "TenantId", "ConsultationKind" },
                unique: true,
                filter: "\"ConsultationKind\" IS NOT NULL AND \"ArchivedAt\" IS NULL");

            // Tenant isolation layer 2 (ADR-012), as for DocSequences.
            migrationBuilder.Tenant("billing", "Services");
            migrationBuilder.Tenant("billing", "PriceVersions");
            migrationBuilder.Tenant("billing", "Settings");
            migrationBuilder.Sql(CopyFeesFromTenancy);
        }

        /// <summary>
        /// The one copy of the fees Tenancy kept before the catalogue held them (MDV-P4-002): each tenant's onboarding defaults become the clinic default of the two
        /// consultation services, and each doctor profile's fees that doctor's own price, from when they were last set. Run here, at deploy, as the owner inside each
        /// tenant (row-level security binds the owner too), with the per-tenant loop of Tenancy's OrganisationStateAndAttestation (which reads only its own tables).
        /// Idempotent: a tenant or doctor that already has prices is skipped. Reading Tenancy's tables from a Billing migration is the AGENTS.md 3.1 exception for a
        /// one-time data copy (read-only, tenant by tenant under row-level security); the app never does it. db-migrate runs Tenancy's migrations first, so the
        /// fee columns must not be dropped before every environment has applied this one.
        /// </summary>
        public const string CopyFeesFromTenancy = """
            DO $$
            DECLARE t uuid; cur text; created timestamptz; new_id uuid; follow_id uuid;
            BEGIN
              FOR t IN SELECT "TenantId" FROM tenancy."TenantIndex" LOOP
                PERFORM set_config('medvya.tenant_id', t::text, true);
                SELECT CASE "CountryCode" WHEN 'IN' THEN 'INR' WHEN 'AE' THEN 'AED' END, "CreatedAt" INTO cur, created FROM tenancy."Tenants" WHERE "Id" = t;
                CONTINUE WHEN cur IS NULL
                  OR NOT (EXISTS (SELECT 1 FROM tenancy."PracticeDefaults" WHERE "TenantId" = t) OR EXISTS (SELECT 1 FROM tenancy."DoctorProfiles" WHERE "TenantId" = t));
                INSERT INTO billing."Services" ("Id", "TenantId", "Name", "Category", "TaxTreatment", "ConsultationKind", "CreatedAt", "ChangedAt")
                  SELECT gen_random_uuid(), t, k.name, 'Consultation', 'Exempt', k.kind, now(), now()
                  FROM (VALUES ('New', 'Consultation (new visit)'), ('FollowUp', 'Consultation (follow-up)')) AS k(kind, name)
                  WHERE NOT EXISTS (SELECT 1 FROM billing."Services" s WHERE s."TenantId" = t AND s."ConsultationKind" = k.kind AND s."ArchivedAt" IS NULL);
                new_id := (SELECT "Id" FROM billing."Services" WHERE "TenantId" = t AND "ConsultationKind" = 'New' AND "ArchivedAt" IS NULL);
                follow_id := (SELECT "Id" FROM billing."Services" WHERE "TenantId" = t AND "ConsultationKind" = 'FollowUp' AND "ArchivedAt" IS NULL);
                INSERT INTO billing."PriceVersions" ("Id", "TenantId", "ServiceId", "DoctorId", "AmountMinor", "Currency", "EffectiveFrom", "CreatedAt")
                  SELECT gen_random_uuid(), t, x.service, NULL, x.amount, cur, created, now()
                  FROM tenancy."PracticeDefaults" d, LATERAL (VALUES (new_id, d."NewVisitFeeMinor"), (follow_id, d."FollowUpFeeMinor")) AS x(service, amount)
                  WHERE d."TenantId" = t AND NOT EXISTS (SELECT 1 FROM billing."PriceVersions" v WHERE v."ServiceId" = x.service AND v."DoctorId" IS NULL);
                INSERT INTO billing."PriceVersions" ("Id", "TenantId", "ServiceId", "DoctorId", "AmountMinor", "Currency", "EffectiveFrom", "CreatedAt")
                  SELECT gen_random_uuid(), t, x.service, p."MembershipId", x.amount, cur, p."ChangedAt", now()
                  FROM tenancy."DoctorProfiles" p, LATERAL (VALUES (new_id, p."NewVisitFeeMinor"), (follow_id, p."FollowUpFeeMinor")) AS x(service, amount)
                  WHERE p."TenantId" = t AND NOT EXISTS (SELECT 1 FROM billing."PriceVersions" v WHERE v."ServiceId" = x.service AND v."DoctorId" = p."MembershipId");
              END LOOP;
              PERFORM set_config('medvya.tenant_id', '', true);
            END $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceVersions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "Services",
                schema: "billing");
        }
    }
}
