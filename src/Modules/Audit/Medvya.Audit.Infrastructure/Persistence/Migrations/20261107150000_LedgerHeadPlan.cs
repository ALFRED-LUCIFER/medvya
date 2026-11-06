using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-039: reinstalls the ledger functions so ledger.chain() runs with enable_sort = off, which keeps its chain-head lookup on the
    /// (TenantId, ChainSeq) index whatever the table statistics say. No schema or hash change. Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(AuditDbContext))]
    [Migration("20261107150000_LedgerHeadPlan")]
    public partial class LedgerHeadPlan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.InstallLedger();
    }
}
