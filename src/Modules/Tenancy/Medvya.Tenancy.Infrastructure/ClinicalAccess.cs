using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Clinical authorisation (MDV-P2-024): the role matrix at the facility (<see cref="IStaffAccess"/>: membership, custom role, licence gate),
/// then a live grant or a care relationship from the modules that know one, under the clinic's policy. Every input is read on each call,
/// so a new visit, an ended grant or a changed policy applies to the next request.
/// </summary>
public sealed class ClinicalAccess(
    TenancyDbContext db, IStaffAccess access, ICurrentUser user, IEnumerable<ICareRelationshipSource> sources, TimeProvider clock) : IClinicalAccess
{
    readonly IReadOnlyList<ICareRelationshipSource> sources = [.. sources];

    public async Task<Access> RequireAsync(Guid patientId, Guid facilityId, Capability capability, IReadOnlyList<Access> levels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (user.UserId is not { } me) throw new ClinicalAccessDeniedException(false, false);
        var granted = await access.AccessAsync([capability, Capability.Patient360Chart], facilityId, cancellationToken);
        var (level, patient360) = (granted[0], granted[1]);
        // The role alone must reach the capability and be one that treats patients; only then can a relationship (or a request) help.
        if (!levels.Contains(level) || !ClinicalAccessRules.Treats(patient360)) throw new ClinicalAccessDeniedException(false, false);

        var now = clock.GetUtcNow();
        if (await db.AccessRequests.AnyAsync(r => r.PatientId == patientId && r.FacilityId == facilityId && r.RequestedBy == me
                && r.Status == AccessRequestStatus.Granted && r.ExpiresAt > now, cancellationToken))
            return level;
        var scope = ClinicalAccessRules.Scope(await db.Tenants.Select(t => t.ClinicalAccess).SingleAsync(cancellationToken), patient360);
        foreach (var source in sources)
            if (await source.RelatedAsync(patientId, facilityId, me, scope, cancellationToken)) return level;

        var pending = await db.AccessRequests.AnyAsync(r => r.PatientId == patientId && r.FacilityId == facilityId && r.RequestedBy == me
            && r.Status == AccessRequestStatus.Pending, cancellationToken);
        throw new ClinicalAccessDeniedException(true, pending);
    }
}

/// <summary>
/// Request access (MDV-P2-024). A request and each decision write their audit event in the transaction of the change; a new request
/// also raises the owner's notification there (<see cref="IAccessRequestParticipant"/>). A decision is a conditional update from Pending,
/// so of two owners deciding at once exactly one wins.
/// </summary>
public sealed class AccessRequests(
    TenancyDbContext db, IStaffAccess access, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock,
    IEnumerable<IAccessRequestParticipant> participants) : IAccessRequests
{
    public const string RequestAction = "patient.access.request", GrantAction = "patient.access.grant", DeclineAction = "patient.access.decline";
    public const string Resource = "access_request";

    public async Task<AccessRequestView> RequestAsync(Guid patientId, Guid facilityId, AccessRequestReason reason, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } me || !ClinicalAccessRules.Treats(await access.AccessAsync(Capability.Patient360Chart, facilityId, cancellationToken)))
            throw new ClinicalAccessDeniedException(false, false);
        if (!Enum.IsDefined(reason)) throw new SettingsValidationException([new("reason", "invalid")]);

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = await PendingOfAsync(patientId, facilityId, me, cancellationToken);
        if (request is null)
        {
            request = new AccessRequest
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = facilityId, PatientId = patientId, RequestedBy = me, Reason = reason,
                RequestedAt = now, Status = AccessRequestStatus.Pending,
            };
            db.AccessRequests.Add(request);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // A second tab asked at the same moment: its request stands, and this one answers with it.
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var winner = await PendingOfAsync(patientId, facilityId, me, cancellationToken) ?? throw new InvalidOperationException("Pending request vanished.", e);
                await audit.WriteAsync(Event(RequestAction, AuditPurpose.Treatment, winner), cancellationToken); // this attempt still leaves its row
                return View(winner);
            }
            foreach (var p in participants) await p.RequestedAsync(request.Id, facilityId, tx.GetDbTransaction(), cancellationToken);
        }
        await AuditAsync(RequestAction, AuditPurpose.Treatment, request, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(request);
    }

    public async Task<IReadOnlyList<AccessRequestView>> PendingAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(facilityId, cancellationToken);
        return [.. (await db.AccessRequests.AsNoTracking().Where(r => r.FacilityId == facilityId && r.Status == AccessRequestStatus.Pending)
            .OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(cancellationToken)).Select(View)];
    }

    public Task<AccessRequestView?> GrantAsync(Guid requestId, int days, CancellationToken cancellationToken = default)
    {
        if (!ClinicalAccessRules.GrantDays.Contains(days)) throw new SettingsValidationException([new("days", "invalid")]);
        return DecideAsync(requestId, AccessRequestStatus.Granted, days, GrantAction, cancellationToken);
    }

    public Task<AccessRequestView?> DeclineAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, AccessRequestStatus.Declined, null, DeclineAction, cancellationToken);

    async Task<AccessRequestView?> DecideAsync(Guid requestId, AccessRequestStatus to, int? days, string action, CancellationToken cancellationToken)
    {
        var request = await db.AccessRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return null;
        await RequireOwnerAsync(request.FacilityId, cancellationToken);
        var me = user.UserId!.Value;
        // Nobody approves their own access, owners included: a second person decides, or the owner changes the clinic policy (audited).
        if (request.RequestedBy == me) throw new AccessRequestRejectedException("own_request");
        var now = clock.GetUtcNow();
        DateTimeOffset? expires = days is { } d ? now.AddDays(d) : null;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.AccessRequests.Where(r => r.Id == requestId && r.Status == AccessRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to).SetProperty(r => r.DecidedBy, me).SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.ExpiresAt, expires), cancellationToken);
        if (changed == 0) throw new AccessRequestRejectedException("not_pending");
        (request.Status, request.DecidedBy, request.DecidedAt, request.ExpiresAt) = (to, me, now, expires);
        await AuditAsync(action, AuditPurpose.Operations, request, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(request);
    }

    /// <summary>Deciding is staff administration (Staff, roles, integrations: Config) at the request's own facility.</summary>
    async Task RequireOwnerAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        if (user.UserId is null || await access.AccessAsync(Capability.StaffRolesIntegrations, facilityId, cancellationToken) != Access.Config)
            throw new ClinicalAccessDeniedException(false, false);
    }

    Task<AccessRequest?> PendingOfAsync(Guid patientId, Guid facilityId, Guid me, CancellationToken cancellationToken) =>
        db.AccessRequests.AsNoTracking().SingleOrDefaultAsync(r => r.PatientId == patientId && r.FacilityId == facilityId && r.RequestedBy == me
            && r.Status == AccessRequestStatus.Pending, cancellationToken);

    Task AuditAsync(string action, string purpose, AccessRequest request, IDbContextTransaction tx, CancellationToken cancellationToken) =>
        audit.WriteAsync(Event(action, purpose, request), tx.GetDbTransaction(), cancellationToken);

    AuditEvent Event(string action, string purpose, AccessRequest request) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = request.Id,
        PatientId = request.PatientId, Purpose = purpose, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    static AccessRequestView View(AccessRequest r) => new(r.Id, r.FacilityId, r.PatientId, r.RequestedBy, r.Reason, r.Status, r.RequestedAt, r.ExpiresAt);
}
