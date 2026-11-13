using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>Start (or reopen) the consultation of one visit. <see cref="LeaveOpen"/>: the doctor chose to leave their other open consultation as it is.</summary>
public sealed record StartEncounter(Guid AppointmentId, bool LeaveOpen = false);

/// <summary>
/// An encounter with its visit's room. Ids and codes only; the screen adds the patient's identity from the Patients module.
/// <see cref="IdentityConfirmedAt"/> and <see cref="IdentityConfirmedBy"/>: when and by whom identity was confirmed with the patient (MDV-P1-091).
/// </summary>
public sealed record EncounterView(
    Guid Id, Guid AppointmentId, Guid PatientId, Guid FacilityId, Guid LeadClinicianId, EncounterStatus Status, AuthorityMode AuthorityMode,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Room, DateTimeOffset? IdentityConfirmedAt, Guid? IdentityConfirmedBy);

/// <summary>The doctor has another consultation open (with this or another patient): end it, or say to leave it open.</summary>
public sealed class OpenEncounterException(Guid encounterId) : Exception("Another consultation is open.")
{
    public Guid EncounterId { get; } = encounterId;
}

/// <summary>The visit's consultation has ended; it is not reopened (notes are amended by addendum, P2).</summary>
public sealed class EncounterEndedException() : Exception("The consultation of this visit has ended.");

/// <summary>
/// Encounters (MDV-P2-001). Every call needs clinical access to the patient (role, care relationship and facility; IClinicalAccess) and
/// writes its audit event (purpose treatment, with the patient) before it answers.
/// </summary>
public interface IEncounters
{
    /// <summary>The visit's encounter, created on the first start (the visit moves to In consult) and returned on every later one. Null: no such visit.</summary>
    /// <exception cref="OpenEncounterException"/>
    /// <exception cref="EncounterEndedException"/>
    Task<EncounterView?> StartAsync(StartEncounter command, CancellationToken cancellationToken = default);

    /// <summary>Null: no such encounter in this tenant.</summary>
    Task<EncounterView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>The allergy answer for the encounter's banner; same access as <see cref="GetAsync"/>, whose audit event covers it. Null: no such encounter.</summary>
    Task<AllergyBanner?> AllergiesAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The treating doctor (the clinician who leads this encounter, with clinical access) confirmed name and date of birth with the patient
    /// (MDV-P1-091). The first confirmation stands: confirming again answers it unchanged. Null: no such encounter.
    /// </summary>
    /// <exception cref="Medvya.Tenancy.Application.ClinicalAccessDeniedException">Not the clinician who leads this encounter.</exception>
    /// <exception cref="EncounterEndedException"/>
    Task<EncounterView?> ConfirmIdentityAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Ends the encounter (the visit moves on to Billing); ending an ended one returns it. Null: no such encounter.</summary>
    Task<EncounterView?> EndAsync(Guid encounterId, CancellationToken cancellationToken = default);
}
