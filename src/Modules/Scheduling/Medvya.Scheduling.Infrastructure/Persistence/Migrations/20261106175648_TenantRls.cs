using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("scheduling", "AppointmentReschedules");
            migrationBuilder.Tenant("scheduling", "Appointments");
            migrationBuilder.Tenant("scheduling", "OutboxEvents");
            migrationBuilder.Tenant("scheduling", "QueueDisplays");
            migrationBuilder.Tenant("scheduling", "QueueTokens");
            migrationBuilder.Tenant("scheduling", "Resources");
            migrationBuilder.Tenant("scheduling", "ScheduleExceptions");
            migrationBuilder.Tenant("scheduling", "ScheduleSlots");
            migrationBuilder.Tenant("scheduling", "SlotHolds");
            migrationBuilder.Tenant("scheduling", "Templates");
            migrationBuilder.Tenant("scheduling", "VisitStatusEvents");
            // shortcut: OutboxTenants lists tenant ids for the dispatcher, which runs with no tenant; ids only, so no policy.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."AppointmentReschedules"; DROP POLICY tenant_insert ON scheduling."AppointmentReschedules"; DROP POLICY tenant_update ON scheduling."AppointmentReschedules"; DROP POLICY tenant_delete ON scheduling."AppointmentReschedules"; ALTER TABLE scheduling."AppointmentReschedules" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."AppointmentReschedules" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."Appointments"; DROP POLICY tenant_insert ON scheduling."Appointments"; DROP POLICY tenant_update ON scheduling."Appointments"; DROP POLICY tenant_delete ON scheduling."Appointments"; ALTER TABLE scheduling."Appointments" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."Appointments" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."OutboxEvents"; DROP POLICY tenant_insert ON scheduling."OutboxEvents"; DROP POLICY tenant_update ON scheduling."OutboxEvents"; DROP POLICY tenant_delete ON scheduling."OutboxEvents"; ALTER TABLE scheduling."OutboxEvents" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."OutboxEvents" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."QueueDisplays"; DROP POLICY tenant_insert ON scheduling."QueueDisplays"; DROP POLICY tenant_update ON scheduling."QueueDisplays"; DROP POLICY tenant_delete ON scheduling."QueueDisplays"; ALTER TABLE scheduling."QueueDisplays" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."QueueDisplays" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."QueueTokens"; DROP POLICY tenant_insert ON scheduling."QueueTokens"; DROP POLICY tenant_update ON scheduling."QueueTokens"; DROP POLICY tenant_delete ON scheduling."QueueTokens"; ALTER TABLE scheduling."QueueTokens" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."QueueTokens" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."Resources"; DROP POLICY tenant_insert ON scheduling."Resources"; DROP POLICY tenant_update ON scheduling."Resources"; DROP POLICY tenant_delete ON scheduling."Resources"; ALTER TABLE scheduling."Resources" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."Resources" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."ScheduleExceptions"; DROP POLICY tenant_insert ON scheduling."ScheduleExceptions"; DROP POLICY tenant_update ON scheduling."ScheduleExceptions"; DROP POLICY tenant_delete ON scheduling."ScheduleExceptions"; ALTER TABLE scheduling."ScheduleExceptions" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."ScheduleExceptions" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."ScheduleSlots"; DROP POLICY tenant_insert ON scheduling."ScheduleSlots"; DROP POLICY tenant_update ON scheduling."ScheduleSlots"; DROP POLICY tenant_delete ON scheduling."ScheduleSlots"; ALTER TABLE scheduling."ScheduleSlots" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."ScheduleSlots" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."SlotHolds"; DROP POLICY tenant_insert ON scheduling."SlotHolds"; DROP POLICY tenant_update ON scheduling."SlotHolds"; DROP POLICY tenant_delete ON scheduling."SlotHolds"; ALTER TABLE scheduling."SlotHolds" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."SlotHolds" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."Templates"; DROP POLICY tenant_insert ON scheduling."Templates"; DROP POLICY tenant_update ON scheduling."Templates"; DROP POLICY tenant_delete ON scheduling."Templates"; ALTER TABLE scheduling."Templates" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."Templates" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON scheduling."VisitStatusEvents"; DROP POLICY tenant_insert ON scheduling."VisitStatusEvents"; DROP POLICY tenant_update ON scheduling."VisitStatusEvents"; DROP POLICY tenant_delete ON scheduling."VisitStatusEvents"; ALTER TABLE scheduling."VisitStatusEvents" NO FORCE ROW LEVEL SECURITY; ALTER TABLE scheduling."VisitStatusEvents" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
