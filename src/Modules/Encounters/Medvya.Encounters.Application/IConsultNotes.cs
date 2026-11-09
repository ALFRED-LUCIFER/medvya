using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>
/// An autosave. <see cref="BaseVersion"/> is the version the editor last saw (0 before the first save); a save on any other version is refused
/// so a second tab or device never overwrites silently. <see cref="Sections"/> is the whole note: a section left out is cleared.
/// </summary>
public sealed record SaveNote(string? TemplateKey, bool Manual, int BaseVersion, IReadOnlyDictionary<string, string>? Sections);

/// <summary>Version 0 and no sections: nothing saved yet (the default template is offered).</summary>
public sealed record NoteView(
    Guid EncounterId, string TemplateKey, int TemplateVersion, NoteStatus Status, bool Manual, IReadOnlyDictionary<string, string> Sections, int Version, DateTimeOffset? UpdatedAt);

/// <summary>Field/code pairs only, never the entered text.</summary>
public sealed class NoteValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Note input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The note moved on since the editor read it. <see cref="Current"/> is the version to reload; the unsaved text stays with the editor.</summary>
public sealed class StaleNoteException(int current) : Exception("The note changed since it was read.")
{
    public int Current { get; } = current;
}

/// <summary>
/// Consult notes (MDV-P2-009). Clinical access to the encounter's patient at its facility for Patient 360 at Full (role, care relationship,
/// facility), and one audit event with the patient and no note text. Null: no such encounter in this tenant.
/// </summary>
public interface IConsultNotes
{
    Task<NoteView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <exception cref="NoteValidationException"/>
    /// <exception cref="StaleNoteException"/>
    Task<NoteView?> SaveAsync(Guid encounterId, SaveNote command, CancellationToken cancellationToken = default);
}
