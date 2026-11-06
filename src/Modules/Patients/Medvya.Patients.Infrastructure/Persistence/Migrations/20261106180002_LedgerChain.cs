using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P0-036: patients.ConsentReceipts becomes a hash-chained, append-only table (see LedgerSql). Forward-only: no Down.</summary>
    [DbContext(typeof(PatientsDbContext))]
    [Migration("20261106180002_LedgerChain")]
    public partial class LedgerChain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddLedger("patients", "ConsentReceipts");
        }
    }
}
