using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Formulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prescribing");

            migrationBuilder.CreateTable(
                name: "FormularyItems",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Generic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strength = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Brands = table.Column<string[]>(type: "text[]", nullable: false),
                    NationalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RegClass = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Atc = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    DefaultSig = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormularyItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderSets",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Items = table.Column<string[]>(type: "text[]", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormularyItems_Country_Generic",
                schema: "prescribing",
                table: "FormularyItems",
                columns: new[] { "Country", "Generic" });

            migrationBuilder.CreateIndex(
                name: "IX_FormularyItems_TenantId_Country_Generic_Strength_Form",
                schema: "prescribing",
                table: "FormularyItems",
                columns: new[] { "TenantId", "Country", "Generic", "Strength", "Form" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderSets_TenantId_Name",
                schema: "prescribing",
                table: "OrderSets",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            // The starter list (ADR-015), loaded before row-level security is switched on: its rows have no tenant, so no tenant could insert them afterwards.
            // shortcut: reads the current seed class, so editing FormularySeed does not change what an applied migration did; a new seed is a new migration.
            foreach (var r in FormularySeed.Rows)
                migrationBuilder.InsertData(
                    schema: "prescribing", table: "FormularyItems",
                    columns: ["Id", "Country", "Generic", "Strength", "Form", "Brands", "RegClass", "Atc", "DefaultSig", "Source", "Active"],
                    values: [FormularySeed.IdOf(r), r.Country, r.Generic, r.Strength, r.Form, Array.Empty<string>(), r.RegClass.ToString(), r.Atc, r.DefaultSig, "Starter", true]);

            migrationBuilder.Tenant("prescribing", "FormularyItems", sharedRows: true);
            migrationBuilder.Tenant("prescribing", "OrderSets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormularyItems",
                schema: "prescribing");

            migrationBuilder.DropTable(
                name: "OrderSets",
                schema: "prescribing");
        }
    }
}
