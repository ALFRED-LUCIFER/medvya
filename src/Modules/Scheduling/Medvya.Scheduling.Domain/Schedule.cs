using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Scheduling.Domain;

/// <summary>Whose calendar a schedule column is: a doctor (the Medvya user id of a doctor membership) or a resource such as a procedure room.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarKind>))]
public enum CalendarKind { Doctor, Resource }

/// <summary>
/// Visit types and their default length in slots (MDV-P1-011).
/// shortcut: a fixed list until the service catalogue defines services with durations (P4); per-template lengths cover P1.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VisitType>))]
public enum VisitType { FollowUp, NewConsultation, Procedure, Teleconsult }

/// <summary>A bookable resource that is not a person (e.g. a procedure room); scheduled like a doctor column.</summary>
public sealed class ScheduleResource
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>e.g. "Procedure room". Names a place, never a person or patient.</summary>
    [Classification(DataClass.Internal)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// The weekly template of one calendar (doctor or resource) at one location (MDV-P1-011). Times are facility wall-clock
/// times; bookable slots are generated from it per day in the facility time zone and never stored (decision in
/// docs/03-architecture/modules.md). Changing it affects only slots that are not booked.
/// </summary>
public sealed class ScheduleTemplate
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>The location of this calendar.</summary>
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
    [Classification(DataClass.Internal)] public CalendarKind CalendarKind { get; init; }
    /// <summary>Default slot length in minutes (15, 20, 30 …); a multiple of 5.</summary>
    [Classification(DataClass.Internal)] public int SlotMinutes { get; set; }
    [Classification(DataClass.Internal)] public List<WorkingHours> Hours { get; set; } = [];
    [Classification(DataClass.Internal)] public List<ScheduleBreak> Breaks { get; set; } = [];
    /// <summary>Length per visit type when it is not one slot, e.g. new consultation 40 min = 2 slots of 20.</summary>
    [Classification(DataClass.Internal)] public List<VisitLength> VisitLengths { get; set; } = [];
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>One working session on a weekday, e.g. Monday 09:00–13:00 in Room 3. End is exclusive and on the same day.</summary>
public sealed record WorkingHours
{
    /// <summary>0 = Sunday … 6 = Saturday (System.DayOfWeek).</summary>
    [Classification(DataClass.Internal)] public DayOfWeek Weekday { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly Start { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly End { get; init; }
    [Classification(DataClass.Internal)] public string? Room { get; init; }
}

/// <summary>A break inside working hours (e.g. lunch): shown as unavailable with its label and never bookable.</summary>
public sealed record ScheduleBreak
{
    [Classification(DataClass.Internal)] public DayOfWeek Weekday { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly Start { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly End { get; init; }
    /// <summary>e.g. "Lunch break". Describes time, never a person or patient.</summary>
    [Classification(DataClass.Internal)] public string? Label { get; init; }
}

public sealed record VisitLength
{
    [Classification(DataClass.Internal)] public VisitType VisitType { get; init; }
    [Classification(DataClass.Internal)] public int Minutes { get; init; }
}
