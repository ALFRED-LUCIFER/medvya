namespace Medvya.Encounters.Application;

/// <summary>A null <see cref="Period"/> takes the follow-up off the front desk's list.</summary>
public sealed record SetFollowUp(string? Period);

/// <summary>The consultation's follow-up: the period chosen, the day from which the desk offers a slot, and where the desk is with it
/// ("Waiting", "Offered" or "Booked", the waitlist's states). All null: none.</summary>
public sealed record FollowUpView(string? Period, DateOnly? DueOn, string? State);

/// <summary>Not one of the follow-up periods.</summary>
public sealed class FollowUpValidationException() : Exception("Follow-up period is invalid.");

/// <summary>
/// The follow-up of a consultation's plan (MDV-P2-043), which the front desk sees on its waitlist as a booking to make for that patient. A doctor's
/// capability: every call needs clinical access to the encounter's patient and writes its audit event. Null: no such encounter in this tenant.
/// </summary>
public interface IFollowUps
{
    Task<FollowUpView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <exception cref="FollowUpValidationException"/>
    /// <summary>Refused (the scheduling VisitTransitionException) with "ended", "follow_up_booked", "already_waiting" or "changed".</summary>
    Task<FollowUpView?> SetAsync(Guid encounterId, SetFollowUp command, CancellationToken cancellationToken = default);
}
