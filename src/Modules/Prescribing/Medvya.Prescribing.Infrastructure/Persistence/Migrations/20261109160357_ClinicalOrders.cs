using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Destination = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrderedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_OrderedHasTime", "(\"State\" = 'Ordered') = (\"OrderedAt\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_EncounterId",
                schema: "prescribing",
                table: "Orders",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_PatientId",
                schema: "prescribing",
                table: "Orders",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("prescribing", "Orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Orders",
                schema: "prescribing");
        }
    }
}
