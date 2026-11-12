using Medvya.Tenancy.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// What each scheduling action needs from the role matrix (Foundations §06), applied to the user's active membership at the
/// facility the request is about (IStaffAccess: base role plus the practice-defined role's extras). Patients' "Own" access is the portal's, never the clinic calendar.
/// </summary>
public static class SchedulingAccess
{
    /// <summary>A capability and the levels of it that allow an action.</summary>
    public sealed record Need(Capability Capability, params Access[] Levels);

    /// <summary>See the calendar: full or read access to "Register / book patients" (doctors, reception, admin, nurse, coordinator).</summary>
    public static readonly Need CanView = new(Capability.RegisterBookPatients, Access.Read, Access.Full);

    /// <summary>Hold slots and book, reschedule or cancel: full access to "Register / book patients" (doctors, reception, practice admin).</summary>
    public static readonly Need CanBook = new(Capability.RegisterBookPatients, Access.Full);

    /// <summary>See the reception board: read or full access to "Check-in & queue" (doctors, reception, nurse, coordinator, admin).</summary>
    public static readonly Need CanViewBoard = new(Capability.CheckInQueue, Access.Read, Access.Full);

    /// <summary>Check in and move visits along the board: full access to "Check-in & queue" (owner-doctor, reception, nurse).</summary>
    public static readonly Need CanRunQueue = new(Capability.CheckInQueue, Access.Full);

    /// <summary>Change templates and resources: practice configuration (owner doctor, practice admin, hospital IT).</summary>
    public static readonly Need CanConfigure = new(Capability.StaffRolesIntegrations, Access.Config);
}

/// <summary>
/// The tenant flags that switch the optional visit steps on (MDV audit UX-05). The keys are the ones the Encounters module
/// (IntakeFlag) and the clinic navigation already use.
/// </summary>
public static class VisitStepFlags
{
    public const string Nurse = "clinical.nurse-intake";
    public const string Billing = "billing.invoices";
}
