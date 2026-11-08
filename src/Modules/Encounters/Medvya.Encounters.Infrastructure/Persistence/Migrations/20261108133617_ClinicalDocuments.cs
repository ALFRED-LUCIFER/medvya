using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <summary>Tenant tables, row-level secured like every tenant table. Forward-only: no Down.</summary>
    public partial class ClinicalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalDocuments",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScanStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_BlobKey",
                schema: "encounters",
                table: "ClinicalDocuments",
                column: "BlobKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_TenantId_PatientId_DocumentDate",
                schema: "encounters",
                table: "ClinicalDocuments",
                columns: new[] { "TenantId", "PatientId", "DocumentDate" });

            migrationBuilder.Tenant("encounters", "ClinicalDocuments");
        }
    }
}
