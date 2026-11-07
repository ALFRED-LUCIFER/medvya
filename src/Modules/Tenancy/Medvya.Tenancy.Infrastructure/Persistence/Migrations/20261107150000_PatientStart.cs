using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-061: keep how a practice chose to bring its patients in set-up (upload, move or start fresh). Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261107150000_PatientStart")]
    public partial class PatientStart : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<string>(name: "PatientStart", schema: "tenancy", table: "PracticeDefaults", type: "character varying(10)", maxLength: 10, nullable: true);
    }
}
