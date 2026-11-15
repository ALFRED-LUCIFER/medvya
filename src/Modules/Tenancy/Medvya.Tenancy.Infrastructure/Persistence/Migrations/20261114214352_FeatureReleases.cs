using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeatureReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureReleases",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Percentage = table.Column<int>(type: "integer", nullable: false),
                    Organisations = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    ClinicalSignOff = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SignOffByMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignOffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChangedByMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureReleases", x => x.Id);
                    table.CheckConstraint("CK_FeatureReleases_Percentage", "\"Percentage\" BETWEEN 0 AND 99");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureReleases_Country_Key",
                schema: "tenancy",
                table: "FeatureReleases",
                columns: new[] { "Country", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureReleases",
                schema: "tenancy");
        }
    }
}
