using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FollowUpBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueOn",
                schema: "scheduling",
                table: "Waitlist",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                schema: "scheduling",
                table: "Waitlist",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUp",
                schema: "scheduling",
                table: "Waitlist",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_TenantId_EncounterId",
                schema: "scheduling",
                table: "Waitlist",
                columns: new[] { "TenantId", "EncounterId" },
                unique: true,
                filter: "\"EncounterId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Waitlist_TenantId_EncounterId",
                schema: "scheduling",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "DueOn",
                schema: "scheduling",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                schema: "scheduling",
                table: "Waitlist");

            migrationBuilder.DropColumn(
                name: "FollowUp",
                schema: "scheduling",
                table: "Waitlist");
        }
    }
}
