using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-071: the other locations an invitation also grants on acceptance (empty for existing invitations). Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261110180000_InvitationAlsoFacilities")]
    public partial class InvitationAlsoFacilities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(name: "AlsoFacilityIds", schema: "tenancy", table: "Invitations", type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]");
        }
    }
}
