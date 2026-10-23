using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsentProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable: rows stored before the privacy notice (MDV-P1-002/003) have no proof of consent and must not be given one.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentedAt",
                schema: "leads",
                table: "Waitlist",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoticeVersion",
                schema: "leads",
                table: "Waitlist",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentedAt",
                schema: "leads",
                table: "DemoRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoticeVersion",
                schema: "leads",
                table: "DemoRequests",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentedAt",
                schema: "leads",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "NoticeVersion",
                schema: "leads",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "ConsentedAt",
                schema: "leads",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "NoticeVersion",
                schema: "leads",
                table: "DemoRequests");
        }
    }
}
