using System.Text.RegularExpressions;
using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

/// <summary>India (P-02): DOB or age, ABHA optional, Aadhaar last 4 only, PIN 6 digits, four payer kinds.</summary>
public sealed partial class IndiaRules : CountryRules
{
    internal IndiaRules() { }

    protected override string TimeZoneId => "Asia/Kolkata";

    protected override IReadOnlySet<IdentifierScheme> Schemes { get; } =
        new HashSet<IdentifierScheme> { IdentifierScheme.Abha, IdentifierScheme.AbhaAddress, IdentifierScheme.AadhaarLast4, IdentifierScheme.Passport };

    protected override IReadOnlySet<PayerKind> PayerKinds { get; } = Enum.GetValues<PayerKind>().ToHashSet();

    protected override bool AllowsAge => true;

    protected override string? NormalizeMobile(string value) =>
        Mobile().Match(value) is { Success: true } m ? "+91" + m.Groups[1].Value : null;

    protected override string? NormalizeIdentifier(IdentifierScheme scheme, string value, string field, List<ValidationError> errors)
    {
        var compact = IdSeparators().Replace(value, "");
        var (normalized, valid) = scheme switch
        {
            IdentifierScheme.Abha => (compact, AbhaNumber().IsMatch(compact)), // stored as 14 digits; the UI shows XX-XXXX-XXXX-XXXX
            IdentifierScheme.AbhaAddress => (compact.ToLowerInvariant(), AbhaAddress().IsMatch(compact.ToLowerInvariant())),
            IdentifierScheme.AadhaarLast4 => (compact, LastFour().IsMatch(compact)),
            _ => (compact.ToUpperInvariant(), Passport().IsMatch(compact.ToUpperInvariant())),
        };
        if (scheme == IdentifierScheme.AadhaarLast4 && FullAadhaar().IsMatch(compact))
        {
            errors.Add(new(field, "aadhaar_full_number_not_allowed")); // only the last 4 digits may ever be stored
            return null;
        }
        if (!valid) { errors.Add(new(field, "format")); return null; }
        return normalized;
    }

    protected override PatientAddress CheckAddress(PatientAddress a, List<ValidationError> errors)
    {
        if (a.City is null) errors.Add(new("address.city", "required"));
        if (a.Region is null) errors.Add(new("address.region", "required"));
        if (a.PostalCode is null || !Pin().IsMatch(a.PostalCode)) errors.Add(new("address.postalCode", "format")); // "PIN code must be 6 digits"
        NotApplicable(a.Makani, "address.makani", errors);
        NotApplicable(a.PoBox, "address.poBox", errors);
        return a;
    }

    [GeneratedRegex(@"^(?:\+91|0091|91|0)?([6-9]\d{9})$")] private static partial Regex Mobile();
    [GeneratedRegex(@"^\d{14}$")] private static partial Regex AbhaNumber();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._]{2,49}@(abdm|sbx)$")] private static partial Regex AbhaAddress();
    [GeneratedRegex(@"^\d{4}$")] private static partial Regex LastFour();
    [GeneratedRegex(@"^\d{12}$")] private static partial Regex FullAadhaar();
    [GeneratedRegex(@"^\d{6}$")] private static partial Regex Pin();
}
