using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P0-036: audit.AuditEvents becomes a hash-chained, append-only table (see LedgerSql). Forward-only: no Down.</summary>
    [DbContext(typeof(AuditDbContext))]
    [Migration("20261106180001_LedgerChain")]
    public partial class LedgerChain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddLedger("audit", "AuditEvents");
            // audit.verify_chain(tenant): the id of the first broken row, or NULL when the chain is intact.
            migrationBuilder.Sql("CREATE FUNCTION audit.verify_chain(tenant uuid) RETURNS uuid LANGUAGE sql AS $$ SELECT ledger.verify_chain('audit.AuditEvents', tenant) $$;");
        }
    }
}
