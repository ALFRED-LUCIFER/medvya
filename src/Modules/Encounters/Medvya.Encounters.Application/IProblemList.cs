using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>Adds a problem. <see cref="Code"/> blank: uncoded (never guessed); the description then is required. Verification blank: refused.</summary>
public sealed record AddProblem(string? Code, string? Description, ProblemVerification? Verification, DateOnly? OnsetOn);

/// <summary>Changes a problem's verification, status and onset. The code and description are not edited: a different diagnosis is a new problem.</summary>
public sealed record UpdateProblem(ProblemVerification? Verification, ProblemStatus? Status, DateOnly? OnsetOn);

/// <summary>
/// One problem. <see cref="AddedHere"/>: recorded in this consultation ("Added today · verified" when verified). <see cref="Confirmed"/>: the only
/// problems that may leave Medvya as a diagnosis (letters, later claims); a provisional or patient-reported one never is.
/// </summary>
public sealed record ProblemRow(
    Guid Id, string? Code, string? CodeSystem, string? CodeSystemVersion, string Description, ProblemVerification Verification, ProblemStatus Status,
    DateOnly? OnsetOn, ChartSource Source, bool AddedHere, bool Confirmed, DateTimeOffset RecordedAt);

/// <summary>The patient's problem list from one consultation. <see cref="Locked"/>: the consultation has ended and the list is read-only here.</summary>
public sealed record ProblemListView(Guid EncounterId, Guid PatientId, IReadOnlyList<ProblemRow> Rows, bool Locked);

public sealed class ProblemValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Problem input is invalid.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>"ended" (the consultation is over) or "duplicate" (the code is already an active problem of this patient).</summary>
public sealed class ProblemConflictException(string code) : Exception("Problem conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// The problem list in a consultation (MDV-P2-010, E-07). A doctor's capability: every call needs clinical access to the encounter's patient
/// and writes its audit event (no codes or descriptions in it). Writes are refused once the consultation has ended ("ended").
/// </summary>
public interface IProblemList
{
    /// <summary>The code set of the clinic's country (system, version and codes). Holds no patient data.</summary>
    Task<DiagnosisCodeSet> CodeSetAsync(CancellationToken cancellationToken = default);

    /// <summary>Null: no such encounter in this tenant.</summary>
    Task<ProblemListView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <exception cref="ProblemValidationException"/>
    /// <exception cref="ProblemConflictException"/>
    Task<ProblemListView?> AddAsync(Guid encounterId, AddProblem command, CancellationToken cancellationToken = default);

    /// <summary>Null: no such encounter, or no such problem of the encounter's patient.</summary>
    /// <exception cref="ProblemValidationException"/>
    /// <exception cref="ProblemConflictException"/>
    Task<ProblemListView?> UpdateAsync(Guid encounterId, Guid problemId, UpdateProblem command, CancellationToken cancellationToken = default);
}
