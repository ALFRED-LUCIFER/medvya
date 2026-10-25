namespace Medvya.Scheduling.Application;

/// <summary>One invalid field. Field is the command path (e.g. "hours[1]"), Code a stable key the UI translates.</summary>
public sealed record ValidationError(string Field, string Code);

/// <summary>Input was rejected. The message lists field/code pairs only, never the entered values.</summary>
public sealed class SchedulingValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("Schedule input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;

    public static void ThrowIfAny(List<ValidationError> errors)
    {
        if (errors.Count > 0) throw new SchedulingValidationException(errors);
    }
}

/// <summary>The user has no active membership at the facility, or the role lacks the capability. Never says which.</summary>
public sealed class SchedulingAccessDeniedException() : Exception("Not allowed at this facility.");

/// <summary>
/// The slot cannot be held or booked: taken, being booked by someone else, not a slot of the template, in the past, or the
/// hold expired, or "busy" when the doctor-day lock stayed taken through every retry. Code is a stable key ("slot_unavailable",
/// "hold_expired", …) the UI translates; no patient data.
/// </summary>
public sealed class SlotUnavailableException(string code) : Exception("Slot no longer available: " + code)
{
    public string Code { get; } = code;
}

/// <summary>No appointment with this id in the tenant (tenant filters and row-level security hide other tenants' rows).</summary>
public sealed class AppointmentNotFoundException() : Exception("Appointment not found.");

/// <summary>
/// The visit cannot make this move: not allowed from its state, not on the visit's day, not started yet, or changed by someone
/// else meanwhile. Code is a stable key ("invalid_transition", "not_today", "not_started", "changed") the UI translates; no patient data.
/// </summary>
public sealed class VisitTransitionException(string code) : Exception("Visit cannot move: " + code)
{
    public string Code { get; } = code;
}
