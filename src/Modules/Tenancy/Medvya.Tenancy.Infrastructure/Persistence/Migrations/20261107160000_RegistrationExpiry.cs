using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-010: the last valid day of the practice's own registration, for the expiry reminders. Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261107160000_RegistrationExpiry")]
    public partial class RegistrationExpiry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<DateOnly>(name: "RegistrationExpiresOn", schema: "tenancy", table: "Profiles", type: "date", nullable: true);
    }
}
