using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditRoleFacilityDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "audit",
                table: "AuditEvents",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                schema: "audit",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                schema: "audit",
                table: "AuditEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                schema: "audit",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Role",
                schema: "audit",
                table: "AuditEvents");
        }
    }
}
