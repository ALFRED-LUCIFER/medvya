using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

/// <summary>One invalid field. Field is the command path (e.g. "address.postalCode"), Code a stable key the UI translates.</summary>
public sealed record ValidationError(string Field, string Code);

/// <summary>Registration input was rejected. The message lists field/code pairs only, never what was entered (no PHI in exceptions).</summary>
public sealed class PatientValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("Patient data is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>A registration after validation: normalised values, the resolved date of birth and non-blocking warnings.</summary>
public sealed record ValidatedRegistration(RegisterPatient Command, DateOnly DateOfBirth, bool DateOfBirthEstimated, IReadOnlyList<string> Warnings);

/// <summary>
/// Registration rules per country (AGENTS.md 3.3): one subclass per country, no country checks elsewhere.
/// Formats only; national-ID checksums are deliberately not checked (the design asks for format, and synthetic IDs fail them).
/// </summary>
public abstract partial class CountryRules
{
    public static readonly CountryRules India = new IndiaRules();
    public static readonly CountryRules Uae = new UaeRules();

    public static CountryRules For(string countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => throw new PatientValidationException([new("countryCode", "unsupported")]),
    };

    const int MaxText = 200;

    /// <summary>IANA time zone of the country (one per country for India and the UAE); "today" for date checks is taken there.</summary>
    protected abstract string TimeZoneId { get; }
    protected abstract IReadOnlySet<IdentifierScheme> Schemes { get; }
    protected abstract IReadOnlySet<PayerKind> PayerKinds { get; }
    protected virtual bool AllowsAge => false;
    /// <summary>E.164 form of a mobile number, or null when it is not a valid mobile number for the country.</summary>
    protected abstract string? NormalizeMobile(string value);
    /// <summary>Normalised identifier value, or null after adding an error.</summary>
    protected abstract string? NormalizeIdentifier(IdentifierScheme scheme, string value, string field, List<ValidationError> errors);
    /// <summary>Country checks on a trimmed address; returns it normalised.</summary>
    protected abstract PatientAddress CheckAddress(PatientAddress a, List<ValidationError> errors);
    /// <summary>Country minimums and warnings that depend on several fields.</summary>
    protected virtual void CheckRegistration(RegisterPatient c, DateOnly today, List<ValidationError> errors, List<string> warnings) { }

    /// <summary>Age in whole years on <paramref name="today"/> (the birthday counts).</summary>
    public static int AgeOn(DateOnly today, DateOnly dateOfBirth)
    {
        var age = today.Year - dateOfBirth.Year;
        return dateOfBirth.AddYears(age) > today ? age - 1 : age;
    }

    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).DateTime);

    public ValidatedRegistration Validate(RegisterPatient c, DateOnly today)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<string>();

        var fullName = Text(c.FullName, "fullName", required: true, errors);
        var arabicName = Text(c.ArabicName, "arabicName", required: false, errors);
        if (!Enum.IsDefined(c.SexAtBirth)) errors.Add(new("sexAtBirth", "required"));

        DateOnly dob = default;
        var estimated = false;
        if (c.DateOfBirth is { } d)
        {
            if (d > today || d < today.AddYears(-130)) errors.Add(new("dateOfBirth", "out_of_range"));
            dob = d;
        }
        else if (AllowsAge && c.AgeYears is { } age)
        {
            if (age is < 0 or > 130) errors.Add(new("ageYears", "out_of_range"));
            else (dob, estimated) = (today.AddYears(-age), true);
        }
        else errors.Add(new("dateOfBirth", "required"));

        var mobile = Phone(c.Mobile, "mobile", required: true, errors);
        var alternate = Phone(c.AlternatePhone, "alternatePhone", required: false, errors);

        var email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email.Trim();
        if (email is not null && (email.Length > 254 || !MailAddress.TryCreate(email, out _))) errors.Add(new("email", "format"));

        var gender = Text(c.GenderIdentity, "genderIdentity", required: false, errors, 50);
        var language = Text(c.PreferredLanguage, "preferredLanguage", required: false, errors, 35);
        var nationality = string.IsNullOrWhiteSpace(c.Nationality) ? null : c.Nationality.Trim().ToUpperInvariant();
        if (nationality is not null && !TwoLetters().IsMatch(nationality)) errors.Add(new("nationality", "format"));

        var identifiers = Identifiers(c.Identifiers, errors);
        var address = c.Address is null ? null : Address(c.Address, errors);
        var payer = c.Payer is null ? null : Payer(c.Payer, errors);

        var normalized = c with
        {
            FullName = fullName ?? "", ArabicName = arabicName, Mobile = mobile ?? "", AlternatePhone = alternate, Email = email,
            GenderIdentity = gender, PreferredLanguage = language, Nationality = nationality,
            Identifiers = identifiers, Address = address, Payer = payer,
        };
        CheckRegistration(normalized, today, errors, warnings);
        if (errors.Count > 0) throw new PatientValidationException(errors);
        return new(normalized, dob, estimated, warnings);
    }

    /// <summary>Validates the details added later to complete a quick registration.</summary>
    public (PatientAddress? Address, PayerInput? Payer) ValidateCompletion(PatientAddress? address, PayerInput? payer)
    {
        var errors = new List<ValidationError>();
        var result = (address is null ? null : Address(address, errors), payer is null ? null : Payer(payer, errors));
        if (errors.Count > 0) throw new PatientValidationException(errors);
        return result;
    }

    /// <summary>
    /// Reads one desk search box (MDV-P1-018): a full ABHA / Emirates ID, a full mobile number, an MRN, or a name. Identifiers and
    /// phones are normalised exactly as registration stored them, so deterministic encryption and equality match.
    /// Numbers only ever match exactly; a number that is none of these is refused ("full_number_required"), never partially matched.
    /// </summary>
    public SearchTerm ParseSearch(string? query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0) return new(SearchKind.Directory);
        if (q.Length > 100) throw new PatientValidationException([new("query", "too_long")]);

        var probe = new List<ValidationError>(); // format misses are expected while probing and dropped
        foreach (var scheme in Schemes.Where(PatientIdentifier.IsDeskSearchKey).Order())
            if (NormalizeIdentifier(scheme, q, "query", probe) is { } id) return new(SearchKind.Identifier, id, scheme);

        if (NumberLike().IsMatch(q))
        {
            var compact = PhoneSeparators().Replace(q, "");
            if (NormalizeMobile(compact) is { } mobile) return new(SearchKind.Phone, mobile);
            if (compact.Length <= Mrn.Digits && compact.All(char.IsAsciiDigit)) return new(SearchKind.Mrn, Mrn.Format(int.Parse(compact, CultureInfo.InvariantCulture)));
            throw new PatientValidationException([new("query", "full_number_required")]);
        }
        if (q.Any(char.IsDigit)) return new(SearchKind.Mrn, q.ToUpperInvariant()); // e.g. DQ-10-4482
        if (q.Count(char.IsLetter) < 2) throw new PatientValidationException([new("query", "too_short")]);
        return new(SearchKind.Name, q);
    }

    /// <summary>
    /// The identifiers that can be matched exactly (deterministic encryption), normalised as stored. Incomplete or invalid values
    /// are skipped, not rejected: the duplicate check runs while the desk is still typing.
    /// </summary>
    public IReadOnlyList<IdentifierInput> MatchKeys(IReadOnlyList<IdentifierInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var probe = new List<ValidationError>();
        return [.. inputs
            .Where(i => PatientIdentifier.IsLookup(i.Scheme) && Schemes.Contains(i.Scheme))
            .Select(i => NormalizeIdentifier(i.Scheme, i.Value ?? "", "identifier", probe) is { } v ? i with { Value = v } : null)
            .OfType<IdentifierInput>()];
    }

    List<IdentifierInput> Identifiers(IReadOnlyList<IdentifierInput> inputs, List<ValidationError> errors)
    {
        var result = new List<IdentifierInput>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var (scheme, value, expires) = inputs[i];
            var field = $"identifiers[{i}]";
            if (!Schemes.Contains(scheme)) { errors.Add(new(field, "scheme_not_allowed")); continue; }
            if (result.Exists(r => r.Scheme == scheme)) { errors.Add(new(field, "duplicate_scheme")); continue; }
            if (NormalizeIdentifier(scheme, value ?? "", field, errors) is { } normalized) result.Add(new(scheme, normalized, expires));
        }
        return result;
    }

    PatientAddress Address(PatientAddress a, List<ValidationError> errors)
    {
        string? T(string? v, string name) => Text(v, "address." + name, required: false, errors);
        var address = new PatientAddress
        {
            Line1 = Text(a.Line1, "address.line1", required: true, errors) ?? "", Line2 = T(a.Line2, "line2"), City = T(a.City, "city"), District = T(a.District, "district"),
            Region = T(a.Region, "region"),
            PostalCode = T(a.PostalCode, "postalCode"), Makani = T(a.Makani, "makani"), PoBox = T(a.PoBox, "poBox"),
        };
        return CheckAddress(address, errors);
    }

    PayerInput Payer(PayerInput p, List<ValidationError> errors)
    {
        if (!PayerKinds.Contains(p.Kind)) errors.Add(new("payer.kind", "not_allowed"));
        var name = Text(p.PayerName, "payer.payerName", required: p.Kind != PayerKind.SelfPay, errors);
        var member = string.IsNullOrWhiteSpace(p.MemberNumber) ? null : p.MemberNumber.Trim().ToUpperInvariant();
        if (member is null && p.Kind == PayerKind.PrivateInsurance) errors.Add(new("payer.memberNumber", "required"));
        if (member is not null && !MemberNumber().IsMatch(member)) errors.Add(new("payer.memberNumber", "format"));
        return new(p.Kind, name, Text(p.Plan, "payer.plan", required: false, errors, 100), member, p.ValidTo, Text(p.Network, "payer.network", required: false, errors, 100));
    }

    string? Phone(string? value, string field, bool required, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) errors.Add(new(field, "required"));
            return null;
        }
        var normalized = NormalizeMobile(PhoneSeparators().Replace(value, ""));
        if (normalized is null) errors.Add(new(field, "format"));
        return normalized;
    }

    protected static string? Text(string? value, string field, bool required, List<ValidationError> errors, int max = MaxText)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is null && required) errors.Add(new(field, "required"));
        if (trimmed?.Length > max) errors.Add(new(field, "too_long"));
        return trimmed;
    }

    protected static void NotApplicable(string? value, string field, List<ValidationError> errors)
    {
        if (value is not null) errors.Add(new(field, "not_applicable"));
    }

    [GeneratedRegex(@"[\s\-().]")] protected static partial Regex PhoneSeparators();
    [GeneratedRegex(@"^\+?[\d\s\-().]+$")] private static partial Regex NumberLike();
    [GeneratedRegex(@"[\s\-]")] protected static partial Regex IdSeparators();
    [GeneratedRegex("^[A-Z]{2}$")] private static partial Regex TwoLetters();
    [GeneratedRegex("^[A-Z0-9/-]{1,50}$")] private static partial Regex MemberNumber();
    [GeneratedRegex("^[A-Z0-9]{6,20}$")] protected static partial Regex Passport();
}
