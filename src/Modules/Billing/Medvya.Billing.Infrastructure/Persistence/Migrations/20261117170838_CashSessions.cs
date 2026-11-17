using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CashSessionId",
                schema: "billing",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashSessions",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    OpeningFloatMinor = table.Column<long>(type: "bigint", nullable: false),
                    OpenedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedMinor = table.Column<long>(type: "bigint", nullable: true),
                    CountedMinor = table.Column<long>(type: "bigint", nullable: true),
                    VarianceMinor = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CountedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Counts = table.Column<string>(type: "jsonb", nullable: true),
                    Totals = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.Id);
                    table.CheckConstraint("CK_CashSessions_Float", "\"OpeningFloatMinor\" BETWEEN 0 AND 100000000");
                });

            migrationBuilder.CreateTable(
                name: "CashAdjustments",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashAdjustments", x => x.Id);
                    table.CheckConstraint("CK_CashAdjustments_Amount", "\"AmountMinor\" <> 0");
                    table.ForeignKey(
                        name: "FK_CashAdjustments_CashSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "billing",
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CashSessionId",
                schema: "billing",
                table: "Payments",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashAdjustments_SessionId",
                schema: "billing",
                table: "CashAdjustments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_FacilityId_ClosedAt",
                schema: "billing",
                table: "CashSessions",
                columns: new[] { "FacilityId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_FacilityId",
                schema: "billing",
                table: "CashSessions",
                columns: new[] { "TenantId", "FacilityId" },
                unique: true,
                filter: "\"State\" = 'Open'");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CashSessions_CashSessionId",
                schema: "billing",
                table: "Payments",
                column: "CashSessionId",
                principalSchema: "billing",
                principalTable: "CashSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Tenant isolation layer 2 (ADR-012), as for the other billing tables.
            migrationBuilder.Tenant("billing", "CashSessions");
            migrationBuilder.Tenant("billing", "CashAdjustments");
            // MDV-P4-012: a closed session is never changed or deleted, for every role (ENABLE ALWAYS: session_replication_role does not skip it);
            // a correction is a CashAdjustments row, and those are append-only.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.refuse_closed_cash_session() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."State" = 'Closed' THEN
                        RAISE EXCEPTION 'a closed cash session is immutable: % is refused', TG_OP USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_CashSessions_ClosedImmutable" BEFORE UPDATE OR DELETE ON billing."CashSessions" FOR EACH ROW EXECUTE FUNCTION billing.refuse_closed_cash_session();
                ALTER TABLE billing."CashSessions" ENABLE ALWAYS TRIGGER "TR_CashSessions_ClosedImmutable";
                CREATE FUNCTION billing.refuse_cash_adjustment_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'a cash adjustment is append-only: % is refused', TG_OP USING ERRCODE = 'restrict_violation';
                END $$;
                CREATE TRIGGER "TR_CashAdjustments_AppendOnly" BEFORE UPDATE OR DELETE ON billing."CashAdjustments" FOR EACH ROW EXECUTE FUNCTION billing.refuse_cash_adjustment_change();
                ALTER TABLE billing."CashAdjustments" ENABLE ALWAYS TRIGGER "TR_CashAdjustments_AppendOnly";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production; this is for a throwaway database.
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_CashAdjustments_AppendOnly" ON billing."CashAdjustments"; DROP FUNCTION billing.refuse_cash_adjustment_change();
                DROP TRIGGER "TR_CashSessions_ClosedImmutable" ON billing."CashSessions"; DROP FUNCTION billing.refuse_closed_cash_session();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CashSessions_CashSessionId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CashSessions_CashSessionId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "CashAdjustments",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "CashSessions",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CashSessionId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CashSessionId",
                schema: "billing",
                table: "Payments");
        }
    }
}
