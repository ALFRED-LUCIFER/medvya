using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-011 template rules without a database: slots in the facility time zone (IST, GST and a DST zone), breaks, validation.</summary>
public class ScheduleRulesTests
{
    static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");
    static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    static TimeOnly T(int h, int m = 0) => new(h, m);

    static WorkingHours Hours(DayOfWeek d, TimeOnly start, TimeOnly end, string? room = null) => new() { Weekday = d, Start = start, End = end, Room = room };

    /// <summary>The ticket's example: Mon–Fri 09:00–13:00 and 17:00–20:00, 20-minute slots.</summary>
    internal static ScheduleTemplate MorningAndEvening(int slotMinutes = 20) => new()
    {
        CalendarKind = CalendarKind.Doctor, SlotMinutes = slotMinutes,
        Hours = [.. Weekdays.SelectMany(d => new[] { Hours(d, T(9), T(13), "Room 3"), Hours(d, T(17), T(20), "Room 3") })],
    };

    static string[] Local(IEnumerable<Slot> slots, TimeZoneInfo zone) =>
        [.. slots.Select(s => TimeZoneInfo.ConvertTime(s.Start, zone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))];

    static IReadOnlyList<ValidationError> Errors(ScheduleTemplate t) => Assert.Throws<SchedulingValidationException>(() => ScheduleRules.Validate(t)).Errors;

    [Fact]
    public void SlotsFillExactlyTheTemplateWindowsInFacilityLocalTime()
    {
        var monday = new DateOnly(2026, 10, 19);
        var day = ScheduleRules.Day(MorningAndEvening(), monday, Kolkata);

        Assert.Equal(["09:00", "09:20", "09:40", "10:00", "10:20", "10:40", "11:00", "11:20", "11:40", "12:00", "12:20", "12:40",
            "17:00", "17:20", "17:40", "18:00", "18:20", "18:40", "19:00", "19:20", "19:40"], Local(day.Slots, Kolkata));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 3, 30, 0, TimeSpan.Zero), day.Slots[0].Start); // IST is UTC+05:30
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 30, 0, TimeSpan.Zero), day.Slots[^1].End); // 20:00 IST
        Assert.All(day.Slots, s => Assert.Equal(TimeSpan.FromMinutes(20), s.End - s.Start));
        Assert.Equal(["Room 3", "Room 3"], day.Hours.Select(h => h.Room));

        Assert.Empty(ScheduleRules.Day(MorningAndEvening(), new DateOnly(2026, 10, 24), Kolkata).Slots); // Saturday: closed
    }

    [Fact]
    public void GulfStandardTimeHasNoDaylightSaving()
    {
        foreach (var date in new[] { new DateOnly(2026, 1, 5), new DateOnly(2026, 7, 6) })
            Assert.Equal(new DateTimeOffset(date.ToDateTime(T(5)), TimeSpan.Zero), ScheduleRules.Day(MorningAndEvening(), date, Dubai).Slots[0].Start);
    }

    [Fact]
    public void AZoneWithDaylightSavingKeepsWallClockHoursAcrossTheChange()
    {
        var template = MorningAndEvening();
        Assert.Equal(new DateTimeOffset(2026, 3, 23, 9, 0, 0, TimeSpan.Zero), ScheduleRules.Day(template, new(2026, 3, 23), London).Slots[0].Start); // GMT
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 8, 0, 0, TimeSpan.Zero), ScheduleRules.Day(template, new(2026, 3, 30), London).Slots[0].Start); // BST
        Assert.Equal(["09:00", "09:20"], Local(ScheduleRules.Day(template, new(2026, 3, 30), London).Slots.Take(2), London));
    }

    [Fact]
    public void ANightSessionOverTheDaylightSavingChangeHasItsRealLength()
    {
        // Spring forward (29 Mar 2026, 01:00 → 02:00): 00:00–04:00 is three real hours; 01:xx does not exist.
        var spring = new ScheduleTemplate { SlotMinutes = 30, Hours = [Hours(DayOfWeek.Sunday, T(0), T(4))] };
        var day = ScheduleRules.Day(spring, new(2026, 3, 29), London);
        Assert.Equal(["00:00", "00:30", "02:00", "02:30", "03:00", "03:30"], Local(day.Slots, London));

        // Fall back (25 Oct 2026, 02:00 → 01:00): 00:00–03:00 is four real hours; 01:00–02:00 happens twice.
        var autumn = new ScheduleTemplate { SlotMinutes = 30, Hours = [Hours(DayOfWeek.Sunday, T(0), T(3))] };
        Assert.Equal(["00:00", "00:30", "01:00", "01:30", "01:00", "01:30", "02:00", "02:30"], Local(ScheduleRules.Day(autumn, new(2026, 10, 25), London).Slots, London));
        Assert.Equal(8, ScheduleRules.Day(autumn, new(2026, 10, 25), London).Slots.Select(s => s.Start).Distinct().Count());

        // A wall-clock time in the gap moves later by the gap; an ambiguous one takes the earlier instant.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), ScheduleRules.Instant(new(2026, 3, 29), T(1, 30), London));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), ScheduleRules.Instant(new(2026, 10, 25), T(1, 30), London));
    }

    [Fact]
    public void BreaksAreNeverBookableAndSlotsRestartAfterThem()
    {
        var template = MorningAndEvening();
        template.Breaks = [.. Weekdays.Select(d => new ScheduleBreak { Weekday = d, Start = T(11), End = T(11, 15), Label = "Tea" })];
        var day = ScheduleRules.Day(template, new(2026, 10, 19), Kolkata);

        Assert.Equal(["10:20", "10:40", "11:15", "11:35", "11:55", "12:15", "12:35"], Local(day.Slots, Kolkata)[4..11]);
        var tea = Assert.Single(day.Breaks);
        Assert.DoesNotContain(day.Slots, s => s.Start < tea.End && tea.Start < s.End);
        Assert.DoesNotContain("12:55", Local(day.Slots, Kolkata)); // a partial slot before 13:00 is not offered
    }

    [Fact]
    public void UnavailableBlocksCoverEverythingOutsideHoursWithTheReason()
    {
        var template = MorningAndEvening();
        template.Breaks = [new ScheduleBreak { Weekday = DayOfWeek.Monday, Start = T(12), End = T(13), Label = "Lunch break" }];
        var monday = new DateOnly(2026, 10, 19);
        var day = ScheduleRules.Day(template, monday, Kolkata);
        DateTimeOffset At(int h) => ScheduleRules.Instant(monday, T(h), Kolkata);

        Assert.Equal(
            [new UnavailableBlock(At(12), At(13), UnavailableReason.Break, "Lunch break"), new UnavailableBlock(At(13), At(17), UnavailableReason.NotWorking, null)],
            ScheduleRules.Unavailable(day, At(9), At(20), hasTemplate: true));
        Assert.Equal([new UnavailableBlock(At(8), At(20), UnavailableReason.NotWorking, null)],
            ScheduleRules.Unavailable(ScheduleRules.Day(template, new(2026, 10, 25), Kolkata), At(8), At(20), hasTemplate: true)); // closed Sunday
        Assert.Equal([new UnavailableBlock(At(9), At(20), UnavailableReason.NoSchedule, null)], ScheduleRules.Unavailable(CalendarDay.Closed, At(9), At(20), hasTemplate: false));
    }

    [Fact]
    public void VisitTypesSpanWholeSlots()
    {
        var template = MorningAndEvening();
        template.VisitLengths = [new VisitLength { VisitType = VisitType.NewConsultation, Minutes = 40 }];
        ScheduleRules.Validate(template);
        Assert.Equal((2, 1), (ScheduleRules.SlotsFor(template, VisitType.NewConsultation), ScheduleRules.SlotsFor(template, VisitType.FollowUp)));

        template.VisitLengths = [new VisitLength { VisitType = VisitType.Procedure, Minutes = 30 }, new VisitLength { VisitType = VisitType.Procedure, Minutes = 40 }];
        Assert.Equal([new ValidationError("visitLengths[0]", "not_slot_multiple"), new ValidationError("visitLengths[1]", "duplicate")], Errors(template));
    }

    [Fact]
    public void InvalidTemplatesAreRejectedWithFieldCodesOnly()
    {
        Assert.Contains(new ValidationError("slotMinutes", "invalid"), Errors(MorningAndEvening(slotMinutes: 7)));
        Assert.Contains(new ValidationError("slotMinutes", "invalid"), Errors(MorningAndEvening(slotMinutes: 0)));

        var template = MorningAndEvening();
        template.Hours = [Hours(DayOfWeek.Monday, T(9), T(13)), Hours(DayOfWeek.Monday, T(12), T(14)), Hours(DayOfWeek.Tuesday, T(13), T(9))];
        template.Breaks = [new ScheduleBreak { Weekday = DayOfWeek.Monday, Start = T(13, 30), End = T(15) }, new ScheduleBreak { Weekday = DayOfWeek.Wednesday, Start = T(10), End = T(11) }];
        Assert.Equal([
            new ValidationError("hours[1]", "overlap"), new ValidationError("hours[2]", "range"),
            new ValidationError("breaks[0]", "outside_hours"), new ValidationError("breaks[1]", "outside_hours"),
        ], Errors(template));

        var tooLong = MorningAndEvening();
        tooLong.Hours = [Hours(DayOfWeek.Monday, T(9), T(13), "Room " + new string('9', 60))];
        var error = Assert.Throws<SchedulingValidationException>(() => ScheduleRules.Validate(tooLong));
        Assert.Equal([new ValidationError("hours[0].room", "too_long")], error.Errors);
        Assert.DoesNotContain("999", error.Message, StringComparison.Ordinal); // the entered value never reaches the exception
    }

    [Fact]
    public void TemplateTimesSitOnFiveMinuteBoundariesSoSlotUnitsLineUp()
    {
        var template = MorningAndEvening();
        template.Hours = [Hours(DayOfWeek.Monday, T(9, 3), T(13))];
        template.Breaks = [new ScheduleBreak { Weekday = DayOfWeek.Monday, Start = T(11), End = T(11, 12) }];
        Assert.Equal([new ValidationError("hours[0]", "five_minutes"), new ValidationError("breaks[0]", "five_minutes")], Errors(template));
    }

    [Fact]
    public void ADayRunsFromLocalMidnightToLocalMidnightInTheFacilityZone()
    {
        // IST (+05:30) and GST (+04:00): local midnight is the evening before in UTC, and a day is always 24 hours.
        Assert.Equal((new DateTimeOffset(2026, 10, 18, 18, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 18, 30, 0, TimeSpan.Zero)), ScheduleRules.DayBounds(new(2026, 10, 19), Kolkata));
        Assert.Equal((new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero)), ScheduleRules.DayBounds(new(2026, 10, 19), Dubai));
        // A daylight-saving zone: the spring change day is 23 hours, the autumn one 25, and consecutive days meet without a gap.
        Assert.Equal(TimeSpan.FromHours(23), Length(ScheduleRules.DayBounds(new(2026, 3, 29), London)));
        Assert.Equal(TimeSpan.FromHours(25), Length(ScheduleRules.DayBounds(new(2026, 10, 25), London)));
        Assert.Equal(ScheduleRules.DayBounds(new(2026, 3, 29), London).To, ScheduleRules.DayBounds(new(2026, 3, 30), London).From);
        static TimeSpan Length((DateTimeOffset From, DateTimeOffset To) d) => d.To - d.From;
    }

    [Theory]
    [InlineData("2026-10-19")] // Monday
    [InlineData("2026-10-21")] // Wednesday
    [InlineData("2026-10-25")] // Sunday ends the week, it does not start the next
    public void TheWeekRunsMondayToSunday(string date)
    {
        var week = ScheduleRules.Week(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal([.. Enumerable.Range(19, 7).Select(d => new DateOnly(2026, 10, d))], week);
    }

    [Fact]
    public void TheLocalDateIsTheFacilitysNotUtcs() =>
        Assert.Equal(new DateOnly(2026, 10, 20), ScheduleRules.LocalDate(new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero), Kolkata));
}
