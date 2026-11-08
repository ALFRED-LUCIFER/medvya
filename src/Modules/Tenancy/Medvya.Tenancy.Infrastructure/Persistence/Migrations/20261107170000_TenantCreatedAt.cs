using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>#912: when the practice was created, so its first access review is due a quarter later. Existing tenants get 0001-01-01 (reviews stay due as before). Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261107170000_TenantCreatedAt")]
    public partial class TenantCreatedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<DateTimeOffset>(name: "CreatedAt", schema: "tenancy", table: "Tenants", type: "timestamp with time zone", nullable: false,
                defaultValue: new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
