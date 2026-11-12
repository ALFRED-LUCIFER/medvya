using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>One saved version of a history section, with who wrote it and when. Lists are newest first: the first of a section is its current text.</summary>
public sealed record HistoryEntryView(HistorySection Section, string Text, string? Author, DateTimeOffset RecordedAt);

public sealed record SaveHistory(string? Text);

/// <summary>A history text the server refuses. Field/code pairs only, never the text.</summary>
public sealed class HistoryValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("History input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>
/// The chart's history sections (MDV-P2-032). Saving needs clinical access to the patient for "Vitals and allergies entry" at Full (a doctor
/// or a nurse with a care relationship at the facility; a refusal is a ClinicalAccessDeniedException) and writes its audit event with the
/// patient. A save adds a version; nothing is overwritten.
/// </summary>
public interface IPatientHistory
{
    /// <summary>Saves a new version of the section and answers the patient's whole history.</summary>
    /// <exception cref="HistoryValidationException">"text: required | too_long | invalid" (blood group not one of <see cref="HistoryRules.BloodGroups"/>), "section: invalid".</exception>
    Task<IReadOnlyList<HistoryEntryView>> SaveAsync(Guid patientId, Guid facilityId, HistorySection section, SaveHistory command, CancellationToken cancellationToken = default);
}
