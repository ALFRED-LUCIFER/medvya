using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P1-062 and MDV-P1-064 (ADR-016): the onboarding request and its documents, row-level secured like every tenant table. The review queue of Medvya
    /// operations reads the organisation, its request and its documents across tenants through the <c>medvya.operator</c> setting (no tenant set), which
    /// widens read access on these three tables only; writes still need the connection's own tenant. Forward-only: no Down.
    /// </summary>
    public partial class OrganisationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganisationDocuments",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganisationRequests",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ItContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItContactEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationRequests", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_OrganisationRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationDocuments_TenantId_Kind",
                schema: "tenancy",
                table: "OrganisationDocuments",
                columns: new[] { "TenantId", "Kind" },
                unique: true);

            migrationBuilder.Tenant("tenancy", "OrganisationRequests", alsoReadable: RowLevelSecurity.OperatorReview);
            migrationBuilder.Tenant("tenancy", "OrganisationDocuments", alsoReadable: RowLevelSecurity.OperatorReview);
            // The tenant row itself: the facility picker's branch plus the operator's. Replaces the two policies that carry the read condition.
            var own = $"\"Id\" = {RowLevelSecurity.CurrentTenant}";
            var read = $"({own}) OR ({RowLevelSecurity.NoTenantSet} AND ((EXISTS (SELECT 1 FROM tenancy.\"Memberships\" m WHERE m.\"TenantId\" = \"Tenants\".\"Id\" AND m.\"UserId\" = {RowLevelSecurity.CurrentUser} AND m.\"Status\" = 'Active')) OR {RowLevelSecurity.OperatorReview}))";
            migrationBuilder.Sql($"""
                DROP POLICY tenant_select ON tenancy."Tenants";
                DROP POLICY tenant_update ON tenancy."Tenants";
                CREATE POLICY tenant_select ON tenancy."Tenants" FOR SELECT USING ({read});
                CREATE POLICY tenant_update ON tenancy."Tenants" FOR UPDATE USING ({read}) WITH CHECK ({own});
                """);
        }
    }
}
