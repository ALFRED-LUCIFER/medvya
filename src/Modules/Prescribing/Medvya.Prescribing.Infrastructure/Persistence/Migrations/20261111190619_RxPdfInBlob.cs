using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxPdfInBlob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New copies go to Blob (MDV-P2-017); old rows keep their bytes, so their chain hashes stay the same. BlobKey is not in the table's
            // hashed columns (LedgerSql), but Sha256 is, and every read checks the blob against it.
            migrationBuilder.AlterColumn<byte[]>(
                name: "Content",
                schema: "prescribing",
                table: "PrescriptionPdfs",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<string>(
                name: "BlobKey",
                schema: "prescribing",
                table: "PrescriptionPdfs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PrescriptionPdfs_OneCopy",
                schema: "prescribing",
                table: "PrescriptionPdfs",
                sql: "(\"Content\" IS NULL) <> (\"BlobKey\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PrescriptionPdfs_OneCopy",
                schema: "prescribing",
                table: "PrescriptionPdfs");

            migrationBuilder.DropColumn(
                name: "BlobKey",
                schema: "prescribing",
                table: "PrescriptionPdfs");

            migrationBuilder.AlterColumn<byte[]>(
                name: "Content",
                schema: "prescribing",
                table: "PrescriptionPdfs",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
