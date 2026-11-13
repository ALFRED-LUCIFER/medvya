using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityConfirmedWithPatient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IdentityConfirmedAt",
                schema: "encounters",
                table: "Encounters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdentityConfirmedBy",
                schema: "encounters",
                table: "Encounters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Encounters_IdentityConfirmedHasWho",
                schema: "encounters",
                table: "Encounters",
                sql: "(\"IdentityConfirmedAt\" IS NULL) = (\"IdentityConfirmedBy\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Encounters_IdentityConfirmedHasWho",
                schema: "encounters",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "IdentityConfirmedAt",
                schema: "encounters",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "IdentityConfirmedBy",
                schema: "encounters",
                table: "Encounters");
        }
    }
}
