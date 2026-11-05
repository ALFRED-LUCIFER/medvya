using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxTenants",
                schema: "scheduling",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxTenants", x => x.TenantId);
                });

            // Not under row-level security (tenant ids only): the job that publishes events finds whose scope to enter here.
            // UPDLOCK/HOLDLOCK makes two bookings of a new tenant at once insert the id once instead of failing on the key.
            migrationBuilder.Sql("""
                CREATE TRIGGER scheduling.TR_OutboxEvents_Tenant ON scheduling.OutboxEvents AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    INSERT scheduling.OutboxTenants (TenantId)
                    SELECT DISTINCT i.TenantId FROM inserted i
                    WHERE NOT EXISTS (SELECT 1 FROM scheduling.OutboxTenants t WITH (UPDLOCK, HOLDLOCK) WHERE t.TenantId = i.TenantId);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER scheduling.TR_OutboxEvents_Tenant;");

            migrationBuilder.DropTable(
                name: "OutboxTenants",
                schema: "scheduling");
        }
    }
}
