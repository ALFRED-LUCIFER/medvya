using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

public sealed record IdentifierInput(IdentifierScheme Scheme, string Value, DateOnly? ExpiresOn = null);

/// <summary>An emergency contact or guardian: who to call. Name, relationship and phone are all needed.</summary>
public sealed record ContactInput(string? Name, RelationshipKind? Relationship, string? Phone);

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
    /// <summary>Required for full registration, optional in quick mode (MDV-P1-020).</summary>
    public ContactInput? EmergencyContact { get; init; }
    /// <summary>Required whenever the patient is under 18 on the registration day, in quick mode too (MDV-P1-020).</summary>
    public ContactInput? Guardian { get; init; }
    /// <summary>The desk's decision about the possible matches it was shown (MDV-P1-016). Required while any exist.</summary>
    public DuplicateResolution? Duplicates { get; init; }
}

/// <summary>What the desk has typed so far (MDV-P1-016). A stated age is not a date of birth: without a date, only identifiers match.</summary>
public sealed record DuplicateQuery(string CountryCode, string FullName, DateOnly? DateOfBirth, IReadOnlyList<IdentifierInput> Identifiers);

/// <summary>Last four characters of an identifier of the existing record; never the full value before the record is opened.</summary>
public sealed record MaskedIdentifier(IdentifierScheme Scheme, string LastFour);

/// <summary>
/// A possible existing record: privacy-safe like a search row (initials, age, sex, MRN tail, masked identifiers), plus why it
/// matched. A shared phone number is never a reason.
/// </summary>
public sealed record DuplicateCandidate(
    Guid PatientId, string Initials, int AgeYears, SexAtBirth SexAtBirth, string MrnTail,
    bool MatchedOnNameAndDateOfBirth, IReadOnlyList<IdentifierScheme> MatchedIdentifiers, IReadOnlyList<MaskedIdentifier> Identifiers);

/// <summary>"Not the same person · continue" or "Request merge review (P-03)". "Use existing record" registers nothing.</summary>
public enum DuplicateDecision { NotSamePerson, MergeReview }

public sealed record DuplicateResolution(DuplicateDecision Decision, IReadOnlyList<Guid> CandidateIds);

/// <summary>What the success screen shows: MRN, time and actor. Warnings are codes (e.g. "emirates_id.expired") that never block.</summary>
public sealed record RegistrationResult(Guid PatientId, string Mrn, DateTimeOffset RegisteredAt, Guid? RegisteredBy, RegistrationStatus Status, IReadOnlyList<string> Warnings);

public sealed record PatientRecord(Patient Patient, IReadOnlyList<PatientIdentifier> Identifiers, PayerCoverage? Payer, IReadOnlyList<PatientRelationship> Relationships);

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
/// Writes audit inside their own database transaction: the event commits with the change, and a failed audit write rolls it back
/// (fail closed). Reads are audited by the calling endpoint (<c>.Audited()</c>, written before the answer is sent), so each request
/// leaves exactly one audit event.
/// </summary>
public interface IPatientRegistry
{
    /// <summary>
    /// Registers the patient at the current facility (a command for any other facility is refused). Dates (age, the under-18
    /// guardian rule, "today") are taken on the facility's calendar day in its time zone.
    /// Possible duplicates are checked again here, not only while typing: while any match is not covered by
    /// <see cref="RegisterPatient.Duplicates"/>, nothing is saved ("duplicates"/"unresolved"). Each decision is stored as a
    /// <see cref="MergeRequest"/> and audited with the actor, in the same transaction as the registration.
    /// </summary>
    /// <exception cref="PatientValidationException">With field/code pairs only; never the entered values.</exception>
    Task<RegistrationResult> RegisterAsync(RegisterPatient command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Possible existing records at the current facility: an exact identifier match (ABHA number or address, Emirates ID, passport;
    /// deterministic Always Encrypted), or a similar name with the identical, non-estimated date of birth. Phone is never used.
    /// </summary>
    Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(DuplicateQuery query, CancellationToken cancellationToken = default);

    /// <summary>One patient of the current facility, or null (also for another facility's patient).</summary>
    Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Directory page or search of the current facility's patients. Phone, MRN and identifiers (ABHA, ABHA address, Emirates ID, via
    /// deterministic Always Encrypted) match exactly; names match on contained text.
    /// </summary>
    /// <exception cref="PatientValidationException">Unreadable query (see <see cref="CountryRules.ParseSearch"/>) or page.</exception>
    Task<PatientSearchResult> SearchAsync(PatientSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the missing address, payer and/or emergency contact to a patient of the current facility; the registration becomes
    /// Complete (closing the desk's completion task) once all three exist. Null when there is no such patient at this facility (also another facility's
    /// patient): nothing is changed or audited, and the endpoint answers 404.
    /// </summary>
    /// <exception cref="PatientValidationException">Nothing given ("completion"/"required"), or a part is invalid.</exception>
    Task<RegistrationStatus?> CompleteRegistrationAsync(Guid patientId, PatientAddress? address, PayerInput? payer, ContactInput? emergencyContact = null, CancellationToken cancellationToken = default);
}
