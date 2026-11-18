using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DentalFormulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MDV-P8-015: the dental medicines join the shared starter list (rows with no tenant). Row-level security is forced on the table, so no tenant
            // can write a shared row; the migration lifts the force for its own insert and puts it back, inside the migration's transaction.
            // shortcut: reads the current DentalFormularySeed, like the Formulary migration; a changed list is a new migration.
            migrationBuilder.Sql("""ALTER TABLE "prescribing"."FormularyItems" NO FORCE ROW LEVEL SECURITY;""");
            foreach (var r in DentalFormularySeed.Rows)
                migrationBuilder.InsertData(
                    schema: "prescribing", table: "FormularyItems",
                    columns: ["Id", "Country", "Generic", "Strength", "Form", "Brands", "RegClass", "Atc", "DefaultSig", "Source", "Active"],
                    values: [FormularySeed.IdOf(r), r.Country, r.Generic, r.Strength, r.Form, Array.Empty<string>(), r.RegClass.ToString(), r.Atc, r.DefaultSig, "Starter", true]);
            migrationBuilder.Sql("""ALTER TABLE "prescribing"."FormularyItems" FORCE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "prescribing"."FormularyItems" NO FORCE ROW LEVEL SECURITY;""");
            foreach (var r in DentalFormularySeed.Rows)
                migrationBuilder.DeleteData(schema: "prescribing", table: "FormularyItems", keyColumn: "Id", keyValue: FormularySeed.IdOf(r));
            migrationBuilder.Sql("""ALTER TABLE "prescribing"."FormularyItems" FORCE ROW LEVEL SECURITY;""");
        }
    }
}
