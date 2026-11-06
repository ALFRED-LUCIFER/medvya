using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "leads");

            migrationBuilder.CreateTable(
                name: "DemoRequests",
                schema: "leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Organisation = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(5)", unicode: false, maxLength: 5, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NoticeVersion = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Waitlist",
                schema: "leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", unicode: false, maxLength: 2, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NoticeVersion = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waitlist", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_ReceivedAt",
                schema: "leads",
                table: "DemoRequests",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_Country_ReceivedAt",
                schema: "leads",
                table: "Waitlist",
                columns: new[] { "Country", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoRequests",
                schema: "leads");

            migrationBuilder.DropTable(
                name: "Waitlist",
                schema: "leads");
        }
    }
}
