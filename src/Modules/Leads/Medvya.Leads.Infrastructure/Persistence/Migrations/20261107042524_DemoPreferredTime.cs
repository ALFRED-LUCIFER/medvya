using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Leads.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-057: the visitor's preferred demo time, in UTC (null on earlier rows). Forward-only: no Down.</summary>
    public partial class DemoPreferredTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreferredAt",
                schema: "leads",
                table: "DemoRequests",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
