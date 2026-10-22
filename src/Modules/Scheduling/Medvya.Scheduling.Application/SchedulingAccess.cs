using Medvya.Tenancy.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// What each scheduling action needs from the role matrix (Foundations §06), applied to the user's active membership at the
/// facility the request is about. Patients' "Own" access is the portal's, never the clinic calendar.
/// </summary>
public static class SchedulingAccess
{
    /// <summary>See the calendar: full or read access to "Register / book patients" (doctors, reception, admin, nurse, coordinator).</summary>
    public static bool CanView(Role role) => RoleMatrix.Of(role, Capability.RegisterBookPatients) is Access.Read or Access.Full;

    /// <summary>Hold slots and book, reschedule or cancel: full access to "Register / book patients" (doctors, reception, practice admin).</summary>
    public static bool CanBook(Role role) => RoleMatrix.Of(role, Capability.RegisterBookPatients) is Access.Full;

    /// <summary>See the reception board: read or full access to "Check-in & queue" (doctors, reception, nurse, coordinator, admin).</summary>
    public static bool CanViewBoard(Role role) => RoleMatrix.Of(role, Capability.CheckInQueue) is Access.Read or Access.Full;

    /// <summary>Check in and move visits along the board: full access to "Check-in & queue" (solo doctor, reception, nurse).</summary>
    public static bool CanRunQueue(Role role) => RoleMatrix.Of(role, Capability.CheckInQueue) is Access.Full;

    /// <summary>Change templates and resources: practice configuration (owner doctor, practice admin, hospital IT).</summary>
    public static bool CanConfigure(Role role) => RoleMatrix.Of(role, Capability.StaffRolesIntegrations) is Access.Config;
}
