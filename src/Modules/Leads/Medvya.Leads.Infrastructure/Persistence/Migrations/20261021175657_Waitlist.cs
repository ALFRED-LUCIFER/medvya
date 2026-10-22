using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Waitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Waitlist",
                schema: "leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waitlist", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_Country_ReceivedAt",
                schema: "leads",
                table: "Waitlist",
                columns: new[] { "Country", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Waitlist",
                schema: "leads");
        }
    }
}
