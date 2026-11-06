using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-019: one terms acceptance per tenant and version, so two simultaneous accepts cannot write two ledger rows. Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261107090000_UniqueTermsAcceptance")]
    public partial class UniqueTermsAcceptance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_TermsAcceptances_TenantId_TermsVersion", schema: "tenancy", table: "TermsAcceptances");
            migrationBuilder.CreateIndex(name: "IX_TermsAcceptances_TenantId_TermsVersion", schema: "tenancy", table: "TermsAcceptances",
                columns: ["TenantId", "TermsVersion"], unique: true);
        }
    }
}
