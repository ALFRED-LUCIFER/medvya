using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-006 (#213): the practice's sender name for email subjects. Forward-only: no Down.</summary>
    [DbContext(typeof(NotificationsDbContext))]
    [Migration("20261107170000_SenderName")]
    public partial class SenderName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<string>(name: "SenderName", schema: "notifications", table: "Preferences", type: "character varying(60)", maxLength: 60, nullable: true);
    }
}
