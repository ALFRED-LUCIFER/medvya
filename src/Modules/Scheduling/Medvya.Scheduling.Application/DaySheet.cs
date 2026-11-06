using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>The date is the facility-local day; today when null.</summary>
public sealed record PrintDaySheet(Guid FacilityId, DateOnly? Date);

/// <summary>One line of the paper sheet: time, first name, visit type and state. Nothing else leaves the system on paper.</summary>
public sealed record DaySheetRow(DateTimeOffset Start, string? Token, string FirstName, int? AgeYears, VisitType VisitType, AppointmentState State);

/// <summary>One page: a doctor's visits of the day in time order.</summary>
public sealed record DaySheetPage(Guid CalendarId, string DoctorName, IReadOnlyList<DaySheetRow> Rows);

/// <summary>
/// The printable day sheet (MDV-P1-050): the minimum a desk needs through an outage. No phone number, national or insurance ID,
/// reason for visit, notes or patient id. Printing is audited with who printed it (`schedule.print`).
/// </summary>
public sealed record DaySheet(Guid FacilityId, string TimeZone, DateOnly Date, DateTimeOffset PrintedAt, IReadOnlyList<DaySheetPage> Pages)
{
    /// <summary>Age is on the sheet's date; the token and age come from the caller (queue and patient lookups), keyed by appointment id.</summary>
    public static DaySheet From(ScheduleDay day, DateTimeOffset printedAt, IReadOnlyDictionary<Guid, string> tokens, IReadOnlyDictionary<Guid, int> ages) => new(day.FacilityId, day.TimeZone, day.Date, printedAt,
        [.. day.Columns.Where(c => c.CalendarKind == CalendarKind.Doctor).Select(c => new DaySheetPage(c.CalendarId, c.Name,
            [.. c.Appointments.OrderBy(a => a.Start).Select(a => new DaySheetRow(a.Start, tokens.GetValueOrDefault(a.Id), FirstName(a.PatientName), ages.TryGetValue(a.PatientId, out var age) ? age : null, a.VisitType, a.State))]))]);

    static string FirstName(string name) => name.Trim().Split(' ', 2)[0];
}
