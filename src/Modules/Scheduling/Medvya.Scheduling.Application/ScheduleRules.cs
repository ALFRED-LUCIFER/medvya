using System.Text.Json.Serialization;
using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>A working session of one day as UTC instants.</summary>
public sealed record WorkingWindow(DateTimeOffset Start, DateTimeOffset End, string? Room);

public sealed record BreakWindow(DateTimeOffset Start, DateTimeOffset End, string? Label);

/// <summary>A bookable slot; End is exclusive.</summary>
public sealed record Slot(DateTimeOffset Start, DateTimeOffset End);

/// <summary>One calendar's day: working windows, breaks and bookable slots, sorted, as UTC instants.</summary>
public sealed record CalendarDay(IReadOnlyList<WorkingWindow> Hours, IReadOnlyList<BreakWindow> Breaks, IReadOnlyList<Slot> Slots)
{
    public static readonly CalendarDay Closed = new([], [], []);
}

/// <summary>NotWorking: outside the template's hours (incl. closed days). Break: a template break. NoSchedule: no template yet.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UnavailableReason>))]
public enum UnavailableReason { NotWorking, Break, NoSchedule }

/// <summary>Time shown as unavailable (hatched) with its reason; never bookable.</summary>
public sealed record UnavailableBlock(DateTimeOffset Start, DateTimeOffset End, UnavailableReason Reason, string? Label);

/// <summary>
/// Schedule template rules (MDV-P1-011): validation and on-the-fly slot generation in the facility time zone. Pure, so the
/// time-zone cases (IST and GST have no daylight saving; a DST zone is tested too) are unit-tested without a database.
/// </summary>
public static class ScheduleRules
{
    public const int MaxSlotMinutes = 120;
    const int MaxSessions = 50, MaxRoom = 50, MaxLabel = 60, MaxVisitMinutes = 480;

    public static void Validate(ScheduleTemplate t)
    {
        var errors = new List<ValidationError>();
        if (t.SlotMinutes is < 5 or > MaxSlotMinutes || t.SlotMinutes % 5 != 0) errors.Add(new("slotMinutes", "invalid"));
        if (!Enum.IsDefined(t.CalendarKind)) errors.Add(new("calendarKind", "invalid"));
        if (t.Hours.Count > MaxSessions) errors.Add(new("hours", "too_many"));
        if (t.Breaks.Count > MaxSessions) errors.Add(new("breaks", "too_many"));

        for (var i = 0; i < t.Hours.Count; i++)
        {
            var (h, field) = (t.Hours[i], $"hours[{i}]");
            if (!Enum.IsDefined(h.Weekday)) errors.Add(new(field, "weekday"));
            else if (h.Start >= h.End) errors.Add(new(field, "range")); // same-day sessions only
            else if (t.Hours.Take(i).Any(o => o.Weekday == h.Weekday && o.Start < h.End && h.Start < o.End)) errors.Add(new(field, "overlap"));
            if (h.Room?.Length > MaxRoom) errors.Add(new(field + ".room", "too_long"));
        }
        for (var i = 0; i < t.Breaks.Count; i++)
        {
            var (b, field) = (t.Breaks[i], $"breaks[{i}]");
            if (!Enum.IsDefined(b.Weekday)) errors.Add(new(field, "weekday"));
            else if (b.Start >= b.End) errors.Add(new(field, "range"));
            else if (!t.Hours.Any(h => h.Weekday == b.Weekday && h.Start <= b.Start && b.End <= h.End)) errors.Add(new(field, "outside_hours"));
            else if (t.Breaks.Take(i).Any(o => o.Weekday == b.Weekday && o.Start < b.End && b.Start < o.End)) errors.Add(new(field, "overlap"));
            if (b.Label?.Length > MaxLabel) errors.Add(new(field + ".label", "too_long"));
        }
        for (var i = 0; i < t.VisitLengths.Count; i++)
        {
            var (v, field) = (t.VisitLengths[i], $"visitLengths[{i}]");
            if (!Enum.IsDefined(v.VisitType)) errors.Add(new(field, "visit_type"));
            else if (t.VisitLengths.Take(i).Any(o => o.VisitType == v.VisitType)) errors.Add(new(field, "duplicate"));
            if (v.Minutes <= 0 || v.Minutes > MaxVisitMinutes || t.SlotMinutes <= 0 || v.Minutes % t.SlotMinutes != 0) errors.Add(new(field, "not_slot_multiple"));
        }
        SchedulingValidationException.ThrowIfAny(errors);
    }

    /// <summary>Slots a visit of this type takes: its configured length in slots, else one slot.</summary>
    public static int SlotsFor(ScheduleTemplate t, VisitType type) =>
        t.VisitLengths.FirstOrDefault(v => v.VisitType == type) is { } v ? v.Minutes / t.SlotMinutes : 1;

    /// <summary>
    /// The calendar's day in the facility time zone. Slots run from each session start in steps of the slot length and restart
    /// after a break, so a break never wastes part of a slot; a final partial slot is not offered.
    /// </summary>
    public static CalendarDay Day(ScheduleTemplate t, DateOnly date, TimeZoneInfo zone)
    {
        var hours = t.Hours.Where(h => h.Weekday == date.DayOfWeek).OrderBy(h => h.Start)
            .Select(h => new WorkingWindow(Instant(date, h.Start, zone), Instant(date, h.End, zone), h.Room))
            .Where(w => w.Start < w.End).ToList(); // a session entirely inside a DST gap has no time
        var breaks = t.Breaks.Where(b => b.Weekday == date.DayOfWeek).OrderBy(b => b.Start)
            .Select(b => new BreakWindow(Instant(date, b.Start, zone), Instant(date, b.End, zone), b.Label))
            .Where(b => b.Start < b.End).ToList();

        var step = TimeSpan.FromMinutes(t.SlotMinutes);
        var slots = new List<Slot>();
        void Grid(DateTimeOffset from, DateTimeOffset to)
        {
            for (var s = from; s + step <= to; s += step) slots.Add(new(s, s + step));
        }
        foreach (var w in hours)
        {
            var start = w.Start;
            foreach (var b in breaks.Where(b => b.End > w.Start && b.Start < w.End))
            {
                Grid(start, b.Start < w.End ? b.Start : w.End);
                if (b.End > start) start = b.End;
            }
            Grid(start, w.End);
        }
        return new(hours, breaks, slots);
    }

    /// <summary>End of a visit of <paramref name="slots"/> slots from <paramref name="start"/>, or null when start is not a slot of the day or the slots are not back to back.</summary>
    public static DateTimeOffset? SpanEnd(CalendarDay day, DateTimeOffset start, int slots)
    {
        var first = -1;
        for (var i = 0; i < day.Slots.Count && first < 0; i++) if (day.Slots[i].Start == start) first = i;
        if (first < 0 || slots < 1 || first + slots > day.Slots.Count) return null;
        for (var k = first; k < first + slots - 1; k++) if (day.Slots[k].End != day.Slots[k + 1].Start) return null;
        return day.Slots[first + slots - 1].End;
    }

    /// <summary>True when [start, end) lies inside one working session and touches no break.</summary>
    public static bool Covers(CalendarDay day, DateTimeOffset start, DateTimeOffset end) =>
        day.Hours.Any(w => w.Start <= start && end <= w.End) && !day.Breaks.Any(b => b.Start < end && start < b.End);

    /// <summary>Unavailable blocks of a calendar within the day frame shown: everything outside its hours, plus its breaks.</summary>
    public static IReadOnlyList<UnavailableBlock> Unavailable(CalendarDay day, DateTimeOffset frameStart, DateTimeOffset frameEnd, bool hasTemplate)
    {
        var reason = hasTemplate ? UnavailableReason.NotWorking : UnavailableReason.NoSchedule;
        var blocks = new List<UnavailableBlock>();
        var cursor = frameStart;
        foreach (var w in day.Hours)
        {
            if (w.Start > cursor) blocks.Add(new(cursor, w.Start < frameEnd ? w.Start : frameEnd, reason, null));
            if (w.End > cursor) cursor = w.End;
        }
        if (cursor < frameEnd) blocks.Add(new(cursor, frameEnd, reason, null));
        blocks.AddRange(day.Breaks.Select(b => new UnavailableBlock(b.Start, b.End, UnavailableReason.Break, b.Label)));
        return [.. blocks.Where(b => b.Start < b.End).OrderBy(b => b.Start)];
    }

    /// <summary>
    /// The UTC instant of a facility wall-clock time. In a daylight-saving gap the time moves later by the gap length; in an
    /// overlap the earlier instant wins (the java.time rules), so every wall-clock time maps to exactly one instant.
    /// </summary>
    public static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(local) ? zone.GetUtcOffset(local.AddDays(-1)) // the offset in force before the gap
            : zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The facility-local calendar date of an instant.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
