using System.Text.RegularExpressions;
using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

/// <summary>UAE (P-02): DOB required, Emirates ID or passport (EID in quick mode), an expired EID warns only, emirate required.</summary>
public sealed partial class UaeRules : CountryRules
{
    internal UaeRules() { }

    protected override string TimeZoneId => "Asia/Dubai";

    /// <summary>Emirate codes and the health information exchange each one uses (shown in consents; no connection in P1).</summary>
    static readonly Dictionary<string, string> Emirates = new(StringComparer.Ordinal)
    {
        ["AUH"] = "Malaffi", ["DXB"] = "NABIDH", ["SHJ"] = "Riayati", ["AJM"] = "Riayati", ["UAQ"] = "Riayati", ["RAK"] = "Riayati", ["FUJ"] = "Riayati",
    };

    public static string HieFor(string emirate) =>
        Emirates.TryGetValue(emirate, out var hie) ? hie : throw new ArgumentOutOfRangeException(nameof(emirate), "Unknown emirate code.");

    protected override IReadOnlySet<IdentifierScheme> Schemes { get; } =
        new HashSet<IdentifierScheme> { IdentifierScheme.EmiratesId, IdentifierScheme.Passport, IdentifierScheme.Visa };

    protected override IReadOnlySet<PayerKind> PayerKinds { get; } =
        new HashSet<PayerKind> { PayerKind.PrivateInsurance, PayerKind.SelfPay, PayerKind.Corporate };

    protected override string? NormalizeMobile(string value) =>
        Mobile().Match(value) is { Success: true } m ? "+971" + m.Groups[1].Value : null;

    protected override string? NormalizeIdentifier(IdentifierScheme scheme, string value, string field, List<ValidationError> errors)
    {
        var upper = value.Trim().ToUpperInvariant();
        var (normalized, valid) = scheme switch
        {
            // Accept 15 digits without dashes and store the printed form 784-YYYY-NNNNNNN-N.
            IdentifierScheme.EmiratesId => EidDigits().Match(upper) is { Success: true } m
                ? ($"784-{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}", true)
                : (upper, EmiratesIdFormat().IsMatch(upper)),
            IdentifierScheme.Passport => (upper, Passport().IsMatch(upper)),
            _ => (upper, VisaFile().IsMatch(upper)),
        };
        if (!valid) { errors.Add(new(field, "format")); return null; } // "Format 784-YYYY-NNNNNNN-N"
        return normalized;
    }

    protected override PatientAddress CheckAddress(PatientAddress a, List<ValidationError> errors)
    {
        var emirate = a.Region?.ToUpperInvariant();
        if (emirate is null || !Emirates.ContainsKey(emirate)) errors.Add(new("address.region", "emirate_required"));
        if (a.Makani is not null && !Makani().IsMatch(a.Makani)) errors.Add(new("address.makani", "format"));
        if (a.PoBox is not null && !PoBox().IsMatch(a.PoBox)) errors.Add(new("address.poBox", "format"));
        NotApplicable(a.PostalCode, "address.postalCode", errors);
        NotApplicable(a.District, "address.district", errors);
        return a with { Region = emirate };
    }

    protected override void CheckRegistration(RegisterPatient c, DateOnly today, List<ValidationError> errors, List<string> warnings)
    {
        var eid = c.Identifiers.FirstOrDefault(i => i.Scheme == IdentifierScheme.EmiratesId);
        if (c.Quick && eid is null) errors.Add(new("identifiers", "emirates_id_required"));
        if (!c.Quick && eid is null && !c.Identifiers.Any(i => i.Scheme == IdentifierScheme.Passport)) errors.Add(new("identifiers", "emirates_id_or_passport_required"));
        if (eid?.ExpiresOn < today) warnings.Add("emirates_id.expired"); // warns, never blocks care
    }

    [GeneratedRegex(@"^(?:\+971|00971|971|0)?(5\d{8})$")] private static partial Regex Mobile();
    [GeneratedRegex(@"^784-\d{4}-\d{7}-\d$")] private static partial Regex EmiratesIdFormat();
    [GeneratedRegex(@"^784(\d{4})(\d{7})(\d)$")] private static partial Regex EidDigits();
    [GeneratedRegex(@"^[A-Z0-9/-]{5,30}$")] private static partial Regex VisaFile();
    [GeneratedRegex(@"^\d{10}$")] private static partial Regex Makani();
    [GeneratedRegex(@"^\d{1,10}$")] private static partial Regex PoBox();
}
