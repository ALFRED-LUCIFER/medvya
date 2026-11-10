using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-070: tax number and registration certificate of an added location (all null for locations that exist). Forward-only: no Down.</summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261110120000_LocationCertificate")]
    public partial class LocationCertificate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "TaxNumber", schema: "tenancy", table: "Facilities", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "CertificateName", schema: "tenancy", table: "Facilities", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "CertificateKey", schema: "tenancy", table: "Facilities", type: "character varying(200)", maxLength: 200, nullable: true);
        }
    }
}
