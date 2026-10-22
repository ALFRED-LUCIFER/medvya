using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

public sealed record IdentifierInput(IdentifierScheme Scheme, string Value, DateOnly? ExpiresOn = null);

public sealed record PayerInput(PayerKind Kind, string? PayerName = null, string? Plan = null, string? MemberNumber = null, DateOnly? ValidTo = null, string? Network = null);

/// <summary>
/// Full or quick registration (MDV-P1-013/014/015). Quick mode takes the minimum safe fields; without address and payer the
/// patient is saved as <see cref="RegistrationStatus.Incomplete"/>.
/// </summary>
public sealed record RegisterPatient
{
    public required Guid FacilityId { get; init; }
    /// <summary>Tenant country, "IN" or "AE"; selects the country rules.</summary>
    public required string CountryCode { get; init; }
    public bool Quick { get; init; }
    public required string FullName { get; init; }
    public string? ArabicName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    /// <summary>India only: stated age when the date of birth is unknown; the date is then marked estimated.</summary>
    public int? AgeYears { get; init; }
    public required SexAtBirth SexAtBirth { get; init; }
    public string? GenderIdentity { get; init; }
    public string? PreferredLanguage { get; init; }
    public string? Nationality { get; init; }
    public required string Mobile { get; init; }
    public string? AlternatePhone { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<IdentifierInput> Identifiers { get; init; } = [];
    public PatientAddress? Address { get; init; }
    public PayerInput? Payer { get; init; }
}

/// <summary>What the success screen shows: MRN, time and actor. Warnings are codes (e.g. "emirates_id.expired") that never block.</summary>
public sealed record RegistrationResult(Guid PatientId, string Mrn, DateTimeOffset RegisteredAt, Guid? RegisteredBy, RegistrationStatus Status, IReadOnlyList<string> Warnings);

public sealed record PatientRecord(Patient Patient, IReadOnlyList<PatientIdentifier> Identifiers, PayerCoverage? Payer);

/// <summary>Medical record numbers: facility-scoped, zero-padded.</summary>
/// <remarks>shortcut: plain zero-padded number; add the facility prefix (e.g. DQ-11-) when facilities have codes.</remarks>
public static class Mrn
{
    public const int Digits = 6;
    public static string Format(int number) => number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>What a desk search box was read as (<see cref="CountryRules.ParseSearch"/>). Directory = no search text.</summary>
public enum SearchKind { Directory, Name, Phone, Mrn, Identifier }

public sealed record SearchTerm(SearchKind Kind, string? Value = null, IdentifierScheme? Scheme = null);

/// <summary>Recent = registered in the last <see cref="PatientSearch.RecentDays"/> days.</summary>
public enum DirectoryFilter { All, Incomplete, Recent }

public sealed record PatientSearch(string CountryCode, string? Query, DirectoryFilter Filter = DirectoryFilter.All, int Page = 1)
{
    public const int PageSize = 20;
    public const int RecentDays = 7;
    /// <summary>Directory paging stops here (1,000 rows): the desk searches beyond that; paging is not an export.</summary>
    public const int MaxPage = 50;
}

/// <summary>
/// A privacy-safe result row (MDV-P1-018): initials, age, sex and the MRN tail only. Full demographics need the record opened,
/// which is audited.
/// </summary>
public sealed record PatientSearchRow(Guid Id, string Initials, int AgeYears, bool AgeEstimated, SexAtBirth SexAtBirth, string MrnTail, RegistrationStatus Status);

public sealed record PatientSearchResult(SearchKind MatchedOn, IReadOnlyList<PatientSearchRow> Items, int Page, int PageSize, int Total);

/// <summary>
/// Patients of the current tenant at the current facility, scoped by tenant query filters and row-level security.
/// Writes audit inside their own transaction (fail closed). Reads are audited by the calling endpoint (<c>.Audited()</c>, written
/// before the read runs), so each request leaves exactly one audit event.
/// </summary>
public interface IPatientRegistry
{
    /// <exception cref="PatientValidationException">With field/code pairs only; never the entered values.</exception>
    Task<RegistrationResult> RegisterAsync(RegisterPatient command, CancellationToken cancellationToken = default);

    /// <summary>One patient of the current facility, or null (also for another facility's patient).</summary>
    Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directory page or search of the current facility's patients. Phone, MRN and identifiers (ABHA, ABHA address, Emirates ID, via
    /// deterministic Always Encrypted) match exactly; names match on contained text.
    /// </summary>
    /// <exception cref="PatientValidationException">Unreadable query (see <see cref="CountryRules.ParseSearch"/>) or page.</exception>
    Task<PatientSearchResult> SearchAsync(PatientSearch search, CancellationToken cancellationToken = default);

    /// <summary>Adds the missing address and payer; the registration becomes Complete (closing the desk's completion task) once both exist.</summary>
    Task<RegistrationStatus> CompleteRegistrationAsync(Guid patientId, PatientAddress? address, PayerInput? payer, CancellationToken cancellationToken = default);
}
