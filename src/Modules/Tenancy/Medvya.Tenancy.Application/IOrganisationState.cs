using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// The one place an organisation's state changes (ADR-016, MDV-P1-063). The approval endpoint of MDV-P1-064 calls it from Medvya
/// operations, inside the organisation's tenant scope; it does no authorisation itself. Every change is audited with the actor and
/// a reason (required for NeedsChanges, Rejected and Suspended; plain words, never personal details).
/// </summary>
public interface IOrganisationState
{
    Task<OrganisationState> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the state and audits it in one transaction. A no-op when it is already that state.</summary>
    /// <exception cref="SettingsValidationException">"reason:required" or "reason:too_long".</exception>
    Task SetAsync(OrganisationState state, string? reason, CancellationToken cancellationToken = default);
}
