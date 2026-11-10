using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

public sealed class OrganisationStates(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, IEmailSender mail, TimeProvider clock) : IOrganisationState
{
    public Task<OrganisationState> CurrentAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.State).SingleAsync(cancellationToken);

    public async Task SetAsync(OrganisationState state, string? reason, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var errors = new List<ValidationError>();
        if (reason is null && state is OrganisationState.NeedsChanges or OrganisationState.Rejected or OrganisationState.Suspended) errors.Add(new("reason", "required"));
        else if (reason?.Length > 500) errors.Add(new("reason", "too_long"));
        SettingsValidationException.ThrowIfAny(errors);

        var row = await db.Tenants.SingleAsync(cancellationToken);
        if (row.State == state) return;
        if (!OrganisationRules.CanMove(row.State, state)) throw new SettingsValidationException([new("state", "transition")]);
        var before = row.State;
        row.State = state;
        // The applicant reads the last reason on the status page and by email; approving clears it.
        var request = await db.OrganisationRequests.SingleOrDefaultAsync(cancellationToken);
        if (request is not null) request.LastReason = state is OrganisationState.NeedsChanges or OrganisationState.Rejected or OrganisationState.Suspended ? reason : null;
        // Medvya's decisions are emailed to the practice contact (a failed email undoes the change, so the operator repeats it rather than leave the applicant
        // uninformed). The applicant's own submit and an organisation without a contact address send nothing.
        var contact = state is OrganisationState.Submitted ? null : await db.Profiles.AsNoTracking().Select(p => p.Email).SingleOrDefaultAsync(cancellationToken);
        await SettingsAudit.SaveAsync(db, audit, tenantId, user.UserId, clock.GetUtcNow(), $"organisation.state.{state}", "organisation", tenantId,
            new { state = before.ToString() }, new { state = state.ToString(), reason }, cancellationToken,
            contact is null ? null : ct => mail.SendAsync(Message(contact, row.Name, state, reason), ct));
    }

    // No patient data: the organisation's name, the outcome and the operator's reason (plain words, never personal details). English only until templates are localised (MDV-P1-032).
    static EmailMessage Message(string to, string organisation, OrganisationState state, string? reason) => state switch
    {
        OrganisationState.Approved => new(to, $"{organisation} is approved on Medvya", $"{organisation} is approved. Sign in to Medvya to register patients and book visits."),
        OrganisationState.NeedsChanges => new(to, $"Medvya needs changes to the request for {organisation}",
            $"Medvya reviewed the request for {organisation} and needs changes.\n\nReason: {reason}\n\nSign in to Medvya, update the request and send it again."),
        OrganisationState.Rejected => new(to, $"The request for {organisation} was not approved",
            $"Medvya could not approve the request for {organisation}.\n\nReason: {reason}\n\nYou may start a new request."),
        _ => new(to, $"{organisation} is suspended on Medvya",
            $"Medvya has suspended {organisation}. Existing records stay readable; new registration, booking and signing are paused.\n\nReason: {reason}"),
    };
}
