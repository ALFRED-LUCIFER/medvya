using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Scheduling.Domain;

/// <summary>Waiting: wants a slot. Offered: a slot is held for the patient until OfferExpiresAt. Booked: took the slot. Removed: left the list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WaitlistState>))]
public enum WaitlistState { Waiting, Offered, Booked, Removed }

/// <summary>
/// WAITLIST_ENTRY (MDV-P1-027): a patient who wants an earlier or any slot, with what suits them. Preferences are ids, a day
/// mask and a facility-local time window, never reasons in words. Ordered by <see cref="RequestedAt"/>. An offer holds one slot
/// (a SlotHold keyed by this entry's id) until <see cref="OfferExpiresAt"/>; after that the entry counts as Waiting again.
/// </summary>
public sealed class WaitlistEntry
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>The doctor asked for; null means any doctor.</summary>
    [Classification(DataClass.Internal)] public Guid? CalendarId { get; init; }
    [Classification(DataClass.Internal)] public VisitType VisitType { get; init; }
    [Classification(DataClass.Internal)] public ReasonCategory ReasonCategory { get; init; }
    /// <summary>Bit n set = DayOfWeek n suits (Sunday = bit 0); 0 means any day.</summary>
    [Classification(DataClass.Internal)] public int Days { get; init; }
    /// <summary>Facility-local earliest start of day; null means no limit.</summary>
    [Classification(DataClass.Internal)] public TimeOnly? From { get; init; }
    /// <summary>Facility-local latest start of day (inclusive); null means no limit.</summary>
    [Classification(DataClass.Internal)] public TimeOnly? Until { get; init; }
    [Classification(DataClass.Internal)] public WaitlistState State { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset RequestedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? RequestedBy { get; init; }
    [Classification(DataClass.Internal)] public Guid? OfferCalendarId { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? OfferStart { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? OfferExpiresAt { get; set; }
    /// <summary>The appointment made when the offer was accepted.</summary>
    [Classification(DataClass.Internal)] public Guid? AppointmentId { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }

    public const int AnyDay = 0, AllDays = 0b1111111;

    public static int DayBit(DayOfWeek day) => 1 << (int)day;

    /// <summary>Waiting, or Offered with the offer run out: the entry can be offered a slot.</summary>
    public bool Open(DateTimeOffset now) => State == WaitlistState.Waiting || (State == WaitlistState.Offered && OfferExpiresAt <= now);

    /// <summary>Whether a slot starting at this instant in this calendar suits the preferences, judged in the facility's time zone.</summary>
    public bool Suits(Guid calendarId, DateTimeOffset start, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(start, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        return (CalendarId is null || CalendarId == calendarId) && (Days == AnyDay || (Days & DayBit(local.DayOfWeek)) != 0)
            && (From is null || time >= From) && (Until is null || time <= Until);
    }
}
