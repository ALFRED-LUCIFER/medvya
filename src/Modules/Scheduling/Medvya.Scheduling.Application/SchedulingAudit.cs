namespace Medvya.Scheduling.Application;

/// <summary>
/// Audit codes of the scheduling writes, shared by the endpoints (refusals, written by the endpoint) and the use cases (the
/// allowed event, written in the change's own transaction), so both name an attempt the same way (MDV-P0-017).
/// </summary>
public static class SchedulingAudit
{
    public const string TemplateUpdate = "schedule.template.update", ResourceCreate = "schedule.resource.create",
        SlotHold = "slot.hold", SlotRelease = "slot.release", Book = "appointment.book", CheckIn = "appointment.check_in", Status = "appointment.status";

    public const string TemplateResource = "schedule_template", ResourceResource = "schedule_resource", HoldResource = "slot_hold", AppointmentResource = "appointment";

    /// <summary>Templates and resources are practice configuration; holds, bookings and visit moves are care of a patient.</summary>
    public const string Operations = "operations", Treatment = "treatment";
}
