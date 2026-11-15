namespace Medvya.Tenancy.Application;

/// <summary>The organisation's plan has no free doctor or staff seat (MDV-P1-069); answered with 409 and code plan_limit.</summary>
public sealed class PlanLimitException(int doctors, int staff)
    : InvalidOperationException($"Your plan allows {doctors} {(doctors == 1 ? "doctor" : "doctors")} and {staff} staff; contact Medvya to upgrade")
{
    /// <summary>The plan's limits, sent with the answer so the screen can say them in the reader's language.</summary>
    public int Doctors => doctors;
    public int Staff => staff;
}

/// <summary>The plan's limits and whether one more doctor or one more member of staff still fits, for the add wizards' first step (#1365 F-039).</summary>
public sealed record PlanSeats(int Doctors, int Staff, bool DoctorFree, bool StaffFree);
