using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1417: reinstalls the ledger functions with ledger.verify_chain_from (the daily verify re-hashes only rows written since the last digest)
    /// and a ledger.heads() that reads each chain head from its index instead of grouping the whole table. No schema or hash change. Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(AuditDbContext))]
    [Migration("20261116190000_LedgerForwardVerify")]
    public partial class LedgerForwardVerify : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.InstallLedger();
    }
}
