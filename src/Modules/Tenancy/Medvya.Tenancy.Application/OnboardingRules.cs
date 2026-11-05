using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>A registration authority a doctor can choose in step 2 (code is stored; the name is shown, in English).</summary>
public sealed record LicenceAuthority(string Code, string Name);

/// <summary>
/// Onboarding rules per country (AGENTS.md 3.3): one subclass per country, no country checks elsewhere.
/// Errors carry field paths and stable codes only, never what was typed.
/// </summary>
public abstract partial class OnboardingRules
{
    public static readonly OnboardingRules India = new IndiaOnboardingRules();
    public static readonly OnboardingRules Uae = new UaeOnboardingRules();

    public static OnboardingRules? For(string? country) => country switch { "IN" => India, "AE" => Uae, _ => null };

    public const string Solo = "Solo", Clinic = "Clinic";
    public static readonly IReadOnlyList<int> SlotMinutes = [10, 15, 20, 30, 45, 60];
    public static readonly IReadOnlyList<string> TemplateIds = ["general-opd", "diabetes-review", "hypertension", "paediatrics", "antenatal", "teleconsult"];
    public static readonly IReadOnlyList<Role> TeamRoles = [Role.Receptionist, Role.Nurse];
    public const int MaxTeam = 5;
    const long MaxFeeMinor = 100_000_000;

    public abstract string Currency { get; }
    public abstract string TimeZone { get; }
    public abstract IReadOnlyList<LicenceAuthority> Authorities { get; }
    /// <summary>Format of the licence number given to the authority; null when it matches.</summary>
    protected abstract Regex LicenceNumber { get; }
    protected abstract Regex? TaxIdFormat { get; }

    /// <summary>The steps this draft goes through: an owner who is not a doctor skips the licence step (2).</summary>
    public static IReadOnlyList<int> StepsFor(OnboardingData data) => data.Profile?.Mode == Clinic ? [1, 3, 4, 5, 6] : [1, 2, 3, 4, 5, 6];

    /// <summary>Every step of the draft's path; used before the practice is created.</summary>
    public List<OnboardingError> ValidateAll(OnboardingData data)
    {
        var errors = new List<OnboardingError>();
        foreach (var step in StepsFor(data)) errors.AddRange(ValidateStep(step, data));
        return errors;
    }

    public List<OnboardingError> ValidateStep(int step, OnboardingData d)
    {
        var e = new List<OnboardingError>();
        switch (step)
        {
            case 1: Profile(d.Profile, e); break;
            case 2: Licence(d.Licence, e); break;
            case 3: ClinicStep(d.Clinic, e); break;
            case 4: Hours(d.Hours, e); break;
            case 5: Templates(d.Templates, e); break;
            case 6: Team(d.Team, e); break;
            default: e.Add(new("step", "unknown")); break;
        }
        return e;
    }

    /// <summary>
    /// The licence of the Add doctor wizard (MDV-P1-007): the sign-up rules for authority, number and certificate name, plus the
    /// category and an expiry date that must still be ahead. Same field paths ("licence.number"); codes only, never the values.
    /// </summary>
    public List<OnboardingError> ValidateLicence(string? authority, string? number, string? category, DateOnly? expiresOn, string? certificateName, DateOnly today)
    {
        var e = new List<OnboardingError>();
        Licence(new OnboardingLicence(authority, number, certificateName), e);
        Text(category, "licence.category", 40, true, e);
        if (expiresOn is not { } expiry) e.Add(new("licence.expiresOn", "required"));
        else if (expiry <= today) e.Add(new("licence.expiresOn", "past"));
        return e;
    }

    static void Profile(OnboardingProfile? p, List<OnboardingError> e)
    {
        Text(p?.FullName, "profile.fullName", 200, true, e);
        var mode = p?.Mode;
        if (mode is null) e.Add(new("profile.mode", "required"));
        else if (mode is not (Solo or Clinic)) e.Add(new("profile.mode", "format"));
        Text(p?.Specialty, "profile.specialty", 100, mode != Clinic, e);
        Text(p?.Qualifications, "profile.qualifications", 200, false, e);
        Text(p?.Languages, "profile.languages", 200, false, e);
    }

    void Licence(OnboardingLicence? l, List<OnboardingError> e)
    {
        if (string.IsNullOrWhiteSpace(l?.Authority)) e.Add(new("licence.authority", "required"));
        else if (Authorities.All(a => a.Code != l.Authority)) e.Add(new("licence.authority", "unknown"));
        var number = l?.Number?.Trim();
        if (string.IsNullOrEmpty(number)) e.Add(new("licence.number", "required"));
        else if (!LicenceNumber.IsMatch(number)) e.Add(new("licence.number", "format"));
        Text(l?.CertificateName, "licence.certificate", 200, false, e);
    }

    void ClinicStep(OnboardingClinic? c, List<OnboardingError> e)
    {
        Text(c?.Name, "clinic.name", 200, true, e);
        Text(c?.Address, "clinic.address", 500, true, e);
        Text(c?.RegistrationNumber, "clinic.registrationNumber", 50, false, e);
        var tax = c?.TaxId?.Trim();
        if (!string.IsNullOrEmpty(tax) && TaxIdFormat is { } format && !format.IsMatch(tax)) e.Add(new("clinic.taxId", "format"));
        else if (tax is { Length: > 20 }) e.Add(new("clinic.taxId", "tooLong"));
    }

    static void Hours(OnboardingHours? h, List<OnboardingError> e)
    {
        Windows(h?.Weekdays, "hours.weekdays", e);
        Windows(h?.Saturday, "hours.saturday", e);
        if (h?.Weekdays is not { Count: > 0 } && h?.Saturday is not { Count: > 0 }) e.Add(new("hours.weekdays", "required"));
        if (h?.SlotMinutes is not { } slot) e.Add(new("hours.slotMinutes", "required"));
        else if (!SlotMinutes.Contains(slot)) e.Add(new("hours.slotMinutes", "range"));
        Fee(h?.NewVisitFee, "hours.newVisitFee", e);
        Fee(h?.FollowUpFee, "hours.followUpFee", e);
    }

    static void Fee(long? minor, string field, List<OnboardingError> e)
    {
        if (minor is null) e.Add(new(field, "required"));
        else if (minor is < 0 or > MaxFeeMinor) e.Add(new(field, "range"));
    }

    /// <summary>At most two windows a day (morning and evening), each start before end, none overlapping.</summary>
    static void Windows(IReadOnlyList<HoursWindow>? windows, string field, List<OnboardingError> e)
    {
        if (windows is null or { Count: 0 }) return;
        if (windows.Count > 2) { e.Add(new(field, "range")); return; }
        var parsed = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var w in windows)
        {
            if (!TimeOnly.TryParseExact(w.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
                || !TimeOnly.TryParseExact(w.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            { e.Add(new(field, "format")); return; }
            if (s >= t) { e.Add(new(field, "range")); return; }
            // The same 5-minute grid ScheduleRules.Validate enforces (a module may not reference Scheduling, so the number is repeated here).
            if (s.Minute % 5 != 0 || t.Minute % 5 != 0) { e.Add(new(field, "fiveMinutes")); return; }
            parsed.Add((s, t));
        }
        if (parsed.OrderBy(p => p.Start).ToArray() is [var a, var b] && a.End > b.Start) e.Add(new(field, "overlap"));
    }

    static void Templates(IReadOnlyList<string>? ids, List<OnboardingError> e)
    {
        if (ids is not null && (ids.Count != ids.Distinct().Count() || ids.Any(i => !TemplateIds.Contains(i)))) e.Add(new("templates", "unknown"));
    }

    static void Team(IReadOnlyList<OnboardingTeamMember>? team, List<OnboardingError> e)
    {
        if (team is null) return;
        if (team.Count > MaxTeam) { e.Add(new("team", "range")); return; }
        for (var i = 0; i < team.Count; i++)
        {
            var email = team[i].Email?.Trim();
            if (string.IsNullOrEmpty(email)) e.Add(new($"team.{i}.email", "required"));
            // A bare address only, as the invitation flow requires (MDV-P0-013).
            else if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) e.Add(new($"team.{i}.email", "format"));
            if (team[i].Role is not { } role || !TeamRoles.Contains(role)) e.Add(new($"team.{i}.role", "unknown"));
        }
        var emails = team.Select(m => m.Email?.Trim().ToLowerInvariant()).Where(m => !string.IsNullOrEmpty(m)).ToList();
        if (emails.Count != emails.Distinct().Count()) e.Add(new("team", "duplicate"));
    }

    static void Text(string? value, string field, int max, bool required, List<OnboardingError> e)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { if (required) e.Add(new(field, "required")); }
        else if (v.Length > max) e.Add(new(field, "tooLong"));
    }
}

internal sealed partial class IndiaOnboardingRules : OnboardingRules
{
    public override string Currency => "INR";
    public override string TimeZone => "Asia/Kolkata";
    protected override Regex LicenceNumber => RegistrationNumber();
    // GSTIN: 2-digit state, 10-character PAN, entity number, "Z", check character. Format only; the checksum is not verified.
    protected override Regex? TaxIdFormat => Gstin();

    // shortcut: the National Medical Commission and the state medical councils; names stay English and the list is not exhaustive
    // (no council for union territories without one). Replace with the NMC register lookup when verification ships (MDV-P1-007).
    public override IReadOnlyList<LicenceAuthority> Authorities { get; } =
    [
        new("NMC", "National Medical Commission"), new("APMC", "Andhra Pradesh Medical Council"), new("ASMC", "Assam Medical Council"),
        new("BMC", "Bihar Medical Council"), new("CGMC", "Chhattisgarh Medical Council"), new("DMC", "Delhi Medical Council"),
        new("GMC", "Gujarat Medical Council"), new("HMC", "Haryana Medical Council"), new("HPMC", "Himachal Pradesh Medical Council"),
        new("JKMC", "Jammu and Kashmir Medical Council"), new("JMC", "Jharkhand Medical Council"), new("KMC", "Karnataka Medical Council"),
        new("TCMC", "Travancore-Cochin Medical Council"), new("MPMC", "Madhya Pradesh Medical Council"), new("MMC", "Maharashtra Medical Council"),
        new("OCMR", "Odisha Council of Medical Registration"), new("PMC", "Punjab Medical Council"), new("RMC", "Rajasthan Medical Council"),
        new("TNMC", "Tamil Nadu Medical Council"), new("TSMC", "Telangana State Medical Council"), new("UPMC", "Uttar Pradesh Medical Council"),
        new("UKMC", "Uttarakhand Medical Council"), new("WBMC", "West Bengal Medical Council"),
    ];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9/\-]{2,19}$")] private static partial Regex RegistrationNumber();
    [GeneratedRegex(@"^\d{2}[A-Z]{5}\d{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")] private static partial Regex Gstin();
}

internal sealed partial class UaeOnboardingRules : OnboardingRules
{
    public override string Currency => "AED";
    public override string TimeZone => "Asia/Dubai";
    protected override Regex LicenceNumber => Licence();
    protected override Regex? TaxIdFormat => Trn();

    public override IReadOnlyList<LicenceAuthority> Authorities { get; } =
    [
        new("DoH", "Department of Health Abu Dhabi"), new("DHA", "Dubai Health Authority"), new("MOHAP", "Ministry of Health and Prevention"),
    ];

    // Licences look like GD-29918 (prefix, dash, digits); other forms are letters and digits with dashes.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9/\-]{3,19}$")] private static partial Regex Licence();
    // shortcut: TRN is optional (only VAT-registered facilities have one); when given it is exactly 15 digits.
    [GeneratedRegex(@"^\d{15}$")] private static partial Regex Trn();
}
