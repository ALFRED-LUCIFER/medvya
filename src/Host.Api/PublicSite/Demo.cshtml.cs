using System.Globalization;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.PublicSite;

/// <summary>
/// Book a demo (MDV-P1-003, MDV-P1-057): buyer contact details and a preferred time, never patient data. The time is a request the
/// team confirms by email or phone; it is offered and shown in the region's time and stored in UTC.
/// </summary>
public sealed class DemoModel(SiteText text, PublicFormGuard guard) : PublicFormPage(text, guard)
{
    public sealed class DemoForm : ContactForm
    {
        public string? Organisation { get; set; }
        public string? Role { get; set; }
        public string? Country { get; set; }
        public string? Message { get; set; }
        /// <summary>A UTC minute in <see cref="SlotFormat"/>, from one of the offered slots.</summary>
        public string? PreferredAt { get; set; }
    }

    /// <summary>A slot's value and the confirmation's ?at=: a UTC minute, e.g. 2026-11-09T04:30Z. Never a contact value.</summary>
    public const string SlotFormat = "yyyy-MM-dd'T'HH:mm'Z'";
    // Local start times offered on each of the next working days.
    // shortcut: fixed times, not the team's real availability; read a sales calendar if one is ever added (ticket: no calendar provider).
    static readonly TimeOnly[] SlotTimes = [new(10, 0), new(12, 30), new(16, 0)];
    const int SlotDays = 3;

    [BindProperty] public DemoForm Form { get; set; } = new();
    public override ContactForm Contact => Form;
    public override string FormId => "demo";
    public override IReadOnlyList<(string Field, string Id)> Fields { get; } =
    [
        ("Name", "demo-name"), ("Organisation", "demo-organisation"), ("Role", "demo-role"), ("Country", "demo-country"),
        ("Contact", "demo-email"), ("Email", "demo-email"), ("Phone", "demo-phone"), ("PreferredAt", "demo-time-0"), ("Message", "demo-message"),
        ("Consent", "demo-consent"),
    ];
    public override IReadOnlyList<string> NoticeFields { get; } =
        ["site.form.name", "site.demo.organisation", "site.demo.role", "site.demo.country", "site.form.email", "site.form.phone", "site.demo.time.notice", "site.demo.message"];
    public override string NoticePurpose => T("site.notice.purpose.demo");
    protected override string ErrorKey(string field) => field switch
    {
        "Organisation" or "Role" => $"site.demo.error.{field}",
        "PreferredAt" => $"site.demo.error.PreferredAt.{Errors[field]}",
        _ => base.ErrorKey(field),
    };

    TimeZoneInfo Zone => PublicSite.RegionTimeZones[Region];
    public string ZoneName => TRegion("site.demo.zone");

    /// <summary>The time on the confirmation, from ?at=; null when missing or malformed.</summary>
    public DateTimeOffset? Requested => Parse(Request.Query["at"]);

    public void OnGet() => NewForm();

    public Task<IActionResult> OnPostAsync([FromServices] IDemoRequests requests, CancellationToken cancellationToken)
    {
        var preferred = Parse(Form.PreferredAt);
        var request = new DemoRequest
        {
            Name = Clean(Form.Name) ?? "", Organisation = Clean(Form.Organisation) ?? "", Role = Clean(Form.Role) ?? "",
            Country = Clean(Form.Country) ?? "", Email = Clean(Form.Email), Phone = Clean(Form.Phone), Message = Clean(Form.Message),
            PreferredAt = preferred, ConsentedAt = ConsentedAt, NoticeVersion = NoticeVersion,
        };
        var errors = new Dictionary<string, string>(DemoRequestRules.Validate(request, DateTimeOffset.UtcNow), StringComparer.Ordinal);
        if (preferred is null && !string.IsNullOrWhiteSpace(Form.PreferredAt)) errors["PreferredAt"] = "invalid"; // crafted, not missing
        return SubmitAsync(errors, () => requests.SubmitAsync(request, cancellationToken),
            $"{Href(PublicSite.Demo)}?sent=1&at={(preferred is { } at ? Value(at) : "")}");
    }

    /// <summary>The next <see cref="SlotDays"/> weekdays from tomorrow in the region's time, each at <see cref="SlotTimes"/>, as UTC instants.</summary>
    public IEnumerable<(DateOnly Day, DateTimeOffset[] Times)> Slots()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime);
        return Enumerable.Range(1, 7).Select(today.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Take(SlotDays)
            .Select(day => (day, SlotTimes.Select(time =>
            {
                var local = day.ToDateTime(time);
                return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
            }).ToArray()));
    }

    public static string Value(DateTimeOffset at) => at.ToString(SlotFormat, CultureInfo.InvariantCulture);
    public string Day(DateOnly day) => day.ToString("dddd d MMMM", Culture);
    public string Time(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone).ToString("HH:mm", CultureInfo.InvariantCulture);
    /// <summary>A requested time in the region's time, with the zone named: "Monday 9 November, 10:00 India time (IST)".</summary>
    public string Full(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, Zone);
        return $"{local.ToString("dddd d MMMM", Culture)}{(Lang == "ar" ? "،" : ",")} {local.ToString("HH:mm", CultureInfo.InvariantCulture)} {ZoneName}"; // Arabic takes its own comma
    }

    static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParseExact(value, SlotFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;
}
