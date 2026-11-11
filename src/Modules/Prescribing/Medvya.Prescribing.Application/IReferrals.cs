using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

public sealed record ReferralView(
    Guid Id, Guid PatientId, ReferralDirection Direction, string Provider, ReferralUrgency Urgency, string? Reason, IReadOnlyList<ReferralSection> Sections,
    ReferralState State, ReferralChannel? Channel, DateTimeOffset? SentAt, bool Open, Guid? LetterDocumentId, IReadOnlyList<ReferralStep> History);

/// <summary>A step of the status, with the document that is its evidence (the report that came in) and a short note.</summary>
public sealed record ReferralStep(ReferralState State, DateTimeOffset At, Guid? DocumentId, string? Note);

/// <summary>A referral out, written in a consultation. Blank stays blank: no reason is made up.</summary>
public sealed record CreateReferral(string? Provider, ReferralUrgency? Urgency, string? Reason, IReadOnlyList<ReferralSection>? Sections);

/// <summary>A letter received from another provider, filed with its document (already uploaded to the patient's documents).</summary>
public sealed record FileReferralIn(Guid? PatientId, string? Provider, string? Reason, Guid? DocumentId);

public sealed record SendReferral(ReferralChannel? Channel);

/// <summary>Moves the status one allowed step, optionally with evidence: a document of the patient and a short note.</summary>
public sealed record AdvanceReferral(ReferralState? State, Guid? DocumentId, string? Note);

public sealed record LetterPatient(string FullName, string Mrn, DateOnly DateOfBirth, string SexAtBirth);

/// <summary>What the letter says: who it is about and from, to whom, why, and only the chart sections the doctor selected (each as lines of text).</summary>
public sealed record ReferralLetter(
    LetterPatient Patient, string FacilityName, string? DoctorName, string Provider, ReferralUrgency Urgency, string? Reason, DateTimeOffset At,
    IReadOnlyList<LetterSection> Sections);

/// <summary>One fact of the chart in the letter, with where it came from (a ChartSource name the screen words), so an unverified fact is never passed off as verified.</summary>
public sealed record LetterLine(string Text, string Source);

public sealed record LetterSection(ReferralSection Section, IReadOnlyList<LetterLine> Lines);

public sealed class ReferralValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Referral input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>"consent_required" (the channel needs the patient's sharing consent and there is none), "not_allowed" (that status move), "not_a_draft" or "already_sent".</summary>
public sealed class ReferralConflictException(string code) : Exception("Referral conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Referrals (MDV-P2-019). Writes need "Prescriptions and orders" at Sign and a care relationship; reads for Patient 360 need the chart's access.
/// Every call writes its audit event with the patient (writes in the transaction of the change). A null answer: no such patient, encounter or referral here.
/// </summary>
public interface IReferrals
{
    Task<ReferralView?> CreateAsync(Guid encounterId, CreateReferral command, CancellationToken cancellationToken = default);

    /// <summary>Files a letter received at the session's facility (<paramref name="facilityId"/>). Null: no such patient here.</summary>
    Task<ReferralView?> FileInAsync(Guid facilityId, FileReferralIn command, CancellationToken cancellationToken = default);

    /// <summary>Draft to Sent, with the channel and the time. An emailed letter needs the patient's information-exchange consent now (never remembered).</summary>
    Task<ReferralView?> SendAsync(Guid referralId, SendReferral command, CancellationToken cancellationToken = default);

    Task<ReferralView?> AdvanceAsync(Guid referralId, AdvanceReferral command, CancellationToken cancellationToken = default);

    /// <summary>The patient's referrals, open ones first; for Patient 360. Null: no such patient in this tenant.</summary>
    Task<IReadOnlyList<ReferralView>?> ForPatientAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The letter of one referral out, with only its selected sections.</summary>
    Task<ReferralLetter?> LetterAsync(Guid referralId, CancellationToken cancellationToken = default);

    /// <summary>The letter as a server PDF with the clinic logo, the doctor's signature, its content hash and a QR verify code, stored once in Blob.</summary>
    Task<PaperPdf?> LetterPdfAsync(Guid referralId, CancellationToken cancellationToken = default);
}
