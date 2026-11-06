using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-035: national and insurance IDs become application-encrypted bytea (AES-256-GCM, NationalIdColumns) plus an HMAC blind index
    /// for exact-match lookups; tenants' wrapped data keys live in DataKeys under forced row-level security. The old plaintext columns are
    /// dropped, not converted: only the application can encrypt, and this branch has synthetic data only (re-seed). Forward-only: no Down.
    /// </summary>
    public partial class NationalIdEncryption : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MDV-P0-041: dropping the plaintext columns destroys any identifier in them, so refuse unless both tables are still empty.
            // The size check sees every tenant's rows (a row count would see none: forced row-level security binds the migrator too). It
            // also refuses a table that once held rows; that is deliberate, an empty database is the only safe place to run this.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF pg_relation_size('patients."Identifiers"') > 0 OR pg_relation_size('patients."PayerCoverages"') > 0 THEN
                        RAISE EXCEPTION 'NationalIdEncryption refused: patients.Identifiers or patients.PayerCoverages already holds data, and this migration drops the plaintext identifier columns. Export the identifiers and re-enter them through the application on an empty database instead.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(name: "IX_Identifiers_TenantId_Scheme_LookupValue", schema: "patients", table: "Identifiers");
            foreach (var (table, column) in new[] { ("Identifiers", "LookupValue"), ("Identifiers", "DisplayValue"), ("PayerCoverages", "MemberNumber") })
            {
                migrationBuilder.DropColumn(name: column, schema: "patients", table: table);
                migrationBuilder.AddColumn<byte[]>(name: column, schema: "patients", table: table, type: "bytea", nullable: true);
            }
            migrationBuilder.AddColumn<byte[]>(name: "LookupIndex", schema: "patients", table: "Identifiers", type: "bytea", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Identifiers_TenantId_Scheme_LookupIndex", schema: "patients", table: "Identifiers", columns: ["TenantId", "Scheme", "LookupIndex"]);

            migrationBuilder.CreateTable(
                name: "DataKeys",
                schema: "patients",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WrapKeyId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Wrapped = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_DataKeys", x => new { x.TenantId, x.Purpose }));
            migrationBuilder.Tenant("patients", "DataKeys");
        }
    }
}
