using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

public sealed class OrganisationStates(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IOrganisationState
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
        var before = row.State;
        row.State = state;
        await SettingsAudit.SaveAsync(db, audit, tenantId, user.UserId, clock.GetUtcNow(), $"organisation.state.{state}", "organisation", tenantId,
            new { state = before.ToString() }, new { state = state.ToString(), reason }, cancellationToken);
    }
}
