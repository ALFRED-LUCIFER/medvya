using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessReviewDecisions",
                schema: "tenancy",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviewDecisions", x => new { x.ReviewId, x.MembershipId });
                });

            migrationBuilder.CreateTable(
                name: "AccessReviews",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quarter = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantIndex",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIndex", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessReviews_TenantId_Quarter",
                schema: "tenancy",
                table: "AccessReviews",
                columns: new[] { "TenantId", "Quarter" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.AccessReviews,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.AccessReviews,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.AccessReviewDecisions,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.AccessReviewDecisions;
                """);

            // TenantIndex is not under row-level security (tenant ids only): the compliance job finds whose scope to enter here.
            // shortcut: tenants created before this migration are not in it (the migrator cannot read tenant rows); insert their ids once by hand.
            migrationBuilder.Sql("""
                CREATE TRIGGER tenancy.TR_Tenants_Index ON tenancy.Tenants AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT tenancy.TenantIndex (TenantId)
                    SELECT i.Id FROM inserted i
                    WHERE NOT EXISTS (SELECT 1 FROM tenancy.TenantIndex t WITH (UPDLOCK, HOLDLOCK) WHERE t.TenantId = i.Id);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER tenancy.TR_Tenants_Index;");
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.AccessReviews,
                    DROP BLOCK PREDICATE ON tenancy.AccessReviews,
                    DROP FILTER PREDICATE ON tenancy.AccessReviewDecisions,
                    DROP BLOCK PREDICATE ON tenancy.AccessReviewDecisions;
                """);
            migrationBuilder.DropTable(
                name: "AccessReviewDecisions",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "AccessReviews",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantIndex",
                schema: "tenancy");
        }
    }
}
