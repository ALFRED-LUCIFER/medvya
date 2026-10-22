using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A legal number series only moves forward, one number at a time. The app role may UPDATE billing (DocumentNumbering's
    /// MERGE needs it), so without this trigger an UPDATE could rewind NextNumber, skip numbers or move a series to another key:
    /// the same damage as the DELETE the role is denied.
    /// </summary>
    public partial class DocSequencesOnlyMoveForward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A new series holds 1 (nothing issued) or 2 (DocumentNumbering issued number 1). An update keeps the series key and
            // adds exactly one. UPDATE() is true for every column on INSERT, so the two cases are told apart by the deleted rows.
            migrationBuilder.Sql("""
                CREATE TRIGGER billing.TR_DocSequences_OnlyMoveForward ON billing.DocSequences AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                    BEGIN
                        IF UPDATE(Id) OR UPDATE(TenantId) OR UPDATE(CountryCode) OR UPDATE(DocumentType) OR UPDATE(FinancialYear) OR UPDATE(Prefix)
                           OR EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE i.NextNumber <> d.NextNumber + 1)
                            THROW 51000, N'A document number series only moves forward, one number at a time.', 1;
                    END
                    ELSE IF EXISTS (SELECT 1 FROM inserted WHERE NextNumber > 2)
                        THROW 51000, N'A new document number series starts at 1.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER billing.TR_DocSequences_OnlyMoveForward;");
        }
    }
}
