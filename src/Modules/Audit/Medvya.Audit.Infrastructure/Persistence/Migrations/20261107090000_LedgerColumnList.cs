using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-041: the ledger hashes an explicit column list per table (ledger."Tables"."Columns") instead of the whole row, so adding a
    /// column no longer breaks every old chain; tables already ledgered get the columns they have now, which is what their hashes cover.
    /// Only a listed table that carries the chain trigger is acted on. Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(AuditDbContext))]
    [Migration("20261107090000_LedgerColumnList")]
    public partial class LedgerColumnList : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.InstallLedger();
    }
}
