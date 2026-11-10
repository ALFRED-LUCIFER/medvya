using System.Text.Json;
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
/// Staff list and practice-defined roles (MDV-P1-008). A role change saves, records its before/after (SettingsChange, section
/// "staff.role") and writes its audit event in one transaction, so an unaudited permission change never commits.
/// </summary>
public sealed class StaffRoster(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock,
    IEnumerable<IStaffOffboardingParticipant> offboarding, IStaffAccess access, ICurrentFacility facility) : IStaffRoster
{
    const string Section = "staff.role";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public async Task<IReadOnlyList<StaffRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
            from m in db.Memberships.AsNoTracking()
            where m.Role != Role.Patient // portal accounts are patients, not staff
            join f in db.Facilities on m.FacilityId equals f.Id
            let latest = db.Invitations.Where(i => i.MembershipId == m.Id).OrderByDescending(i => i.CreatedAt).FirstOrDefault()
            let custom = db.CustomRoles.Where(r => r.Id == m.CustomRoleId).Select(r => r.Name).FirstOrDefault()
            orderby f.Name, m.Role
            select new { m, Facility = f.Name, latest, custom }).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return [.. rows.Select(x =>
        {
            var pending = x.m.Status == MembershipStatus.Invited ? x.latest : null; // the email is shown only until the person has joined
            var state = pending is null ? null : pending.RevokedAt is not null ? "revoked" : pending.ExpiresAt <= now ? "expired" : "pending";
            return new StaffRow(x.m.Id, x.m.UserId, pending?.Email, x.m.Role, x.m.CustomRoleId, x.custom, x.m.FacilityId, x.Facility,
                x.m.Status, pending?.Id, state, pending?.ExpiresAt);
        })];
    }

    public async Task<IReadOnlyList<CustomRoleView>> RolesAsync(CancellationToken cancellationToken = default) =>
        [.. (await db.CustomRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken)).Select(ViewOf)];

    public async Task<CustomRoleView> CreateRoleAsync(SaveCustomRole command, CancellationToken cancellationToken = default)
    {
        var (name, extras) = await ValidAsync(command, null, cancellationToken);
        var role = new CustomRole
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, Name = name, BaseRole = command.BaseRole,
            Capabilities = CustomRoleRules.Join(extras), CreatedAt = clock.GetUtcNow(),
        };
        db.CustomRoles.Add(role);
        await SaveAuditedAsync("staff.role.create", role.Id, null, ViewOf(role), cancellationToken);
        return ViewOf(role);
    }

    public async Task<CustomRoleView?> UpdateRoleAsync(Guid id, SaveCustomRole command, CancellationToken cancellationToken = default)
    {
        var role = await db.CustomRoles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null) return null;
        var (name, extras) = await ValidAsync(command, id, cancellationToken);
        var before = ViewOf(role);
        (role.Name, role.BaseRole, role.Capabilities) = (name, command.BaseRole, CustomRoleRules.Join(extras));
        await SaveAuditedAsync("staff.role.update", role.Id, before, ViewOf(role), cancellationToken);
        return ViewOf(role);
    }

    static string ValidReason(string? reason)
    {
        reason = reason?.Trim();
        SettingsValidationException.ThrowIfAny(string.IsNullOrEmpty(reason) ? [new("reason", "required")] : reason.Length > 500 ? [new("reason", "too_long")] : []);
        return reason!;
    }

    public async Task<OffboardResult> OffboardAsync(Guid membershipId, string? reason, bool offerRebooking = false, CancellationToken cancellationToken = default)
    {
        reason = ValidReason(reason);
        // Serializable: two administrators offboarding each other at once must not both pass the last-administrator check.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.Id == membershipId && m.Role != Role.Patient && m.Status != MembershipStatus.Offboarded, cancellationToken);
        // Only someone who administers staff at that member's facility offboards them (#877); elsewhere the member does not exist for them.
        if (member is null || await access.AccessAsync(Capability.StaffRolesIntegrations, member.FacilityId, cancellationToken) != Access.Config)
            return new(OffboardOutcome.NotFound);
        if (member.Status == MembershipStatus.Active && RoleMatrix.Of(member.Role, Capability.StaffRolesIntegrations) == Access.Config)
        {
            var others = await db.Memberships.Where(m => m.Status == MembershipStatus.Active && m.Id != membershipId).Select(m => m.Role).ToListAsync(cancellationToken);
            if (!others.Any(r => RoleMatrix.Of(r, Capability.StaffRolesIntegrations) == Access.Config)) return new(OffboardOutcome.LastAdmin);
        }
        var before = member.Status;
        var now = clock.GetUtcNow();
        // A never-joined invitee has no user, and the check constraint keeps such a row Invited: offboarding only cancels the invitation.
        if (member.UserId is not null) member.Status = MembershipStatus.Offboarded;
        await db.Invitations.Where(i => i.MembershipId == membershipId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), cancellationToken);
        // Their open requests for patient access at that site go off the owner's queue: nobody is left to use a grant.
        if (member.UserId is { } asker)
            await db.AccessRequests.Where(r => r.RequestedBy == asker && r.FacilityId == member.FacilityId && r.Status == AccessRequestStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AccessRequestStatus.Withdrawn).SetProperty(r => r.DecidedBy, user.UserId).SetProperty(r => r.DecidedAt, now), cancellationToken);
        db.SettingsChanges.Add(new SettingsChange
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Section = "staff.offboard", ResourceId = membershipId, OccurredAt = now,
            Before = JsonSerializer.Serialize(new { status = before }, Json), After = JsonSerializer.Serialize(new { status = member.Status, reason }, Json),
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "staff.offboard", ResourceType = "membership", ResourceId = membershipId,
            Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        // Other modules' share (a doctor's future visits to "needs rebooking") joins this transaction: all of it commits, or none.
        var afterCommit = new List<Func<Task>>();
        var open = 0;
        if (member.UserId is { } leaving)
            foreach (var part in offboarding)
            {
                var share = await part.OffboardedAsync(leaving, member.FacilityId, offerRebooking, tx.GetDbTransaction(), cancellationToken);
                if (share.AfterCommit is { } after) afterCommit.Add(after);
                open += share.OpenItems;
            }
        await tx.CommitAsync(cancellationToken);
        foreach (var after in afterCommit) await after();
        return new(OffboardOutcome.Done, member.UserId, open);
    }

    public async Task<ReactivateOutcome> ReactivateAsync(Guid membershipId, string? reason, CancellationToken cancellationToken = default)
    {
        reason = ValidReason(reason);
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.Id == membershipId && m.Role != Role.Patient && m.Status == MembershipStatus.Offboarded, cancellationToken);
        if (member is null || await access.AccessAsync(Capability.StaffRolesIntegrations, member.FacilityId, cancellationToken) != Access.Config)
            return ReactivateOutcome.NotFound;
        // Bringing someone back grants their role again: the same ceiling as inviting them (#875).
        var extras = member.CustomRoleId is { } id ? await db.CustomRoles.Where(r => r.Id == id).Select(r => r.Capabilities).SingleOrDefaultAsync(cancellationToken) : null;
        if (await GranterAsync(cancellationToken) is not { } granter || !InvitationRules.CanGrant(granter, c => CustomRoleRules.Effective(member.Role, extras, c)))
            return ReactivateOutcome.AboveGranter;
        var now = clock.GetUtcNow();
        member.Status = MembershipStatus.Active;
        db.SettingsChanges.Add(new SettingsChange
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Section = "staff.reactivate", ResourceId = membershipId, OccurredAt = now,
            Before = JsonSerializer.Serialize(new { status = MembershipStatus.Offboarded }, Json), After = JsonSerializer.Serialize(new { status = member.Status, reason }, Json),
        });
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await StarterPlan.EnsureRoomAsync(db, TenantId, member.Role, cancellationToken); // coming back takes a seat again
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "staff.reactivate", ResourceType = "membership", ResourceId = membershipId,
            Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ReactivateOutcome.Done;
    }

    async Task<(string Name, IReadOnlyList<Capability> Extras)> ValidAsync(SaveCustomRole command, Guid? self, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = command.Name?.Trim();
        var extras = command.Capabilities ?? [];
        var errors = new List<ValidationError>();
        if (string.IsNullOrEmpty(name)) errors.Add(new("name", "required"));
        else if (name.Length > 60) errors.Add(new("name", "too_long"));
        else if (await db.CustomRoles.AnyAsync(r => r.Name == name && r.Id != self, cancellationToken)) errors.Add(new("name", "duplicate"));
        if (!CustomRoleRules.Assignable.Contains(command.BaseRole)) errors.Add(new("baseRole", "invalid"));
        // The ceiling: a custom role never exceeds practice admin, and never gains a capability its base role holds or staff administration.
        else if (!CustomRoleRules.IsValid(command.BaseRole, extras)) errors.Add(new("capabilities", "exceeds_admin"));
        // A role definition reaches every member who holds it, so it may not give patient access its author lacks (#875).
        else if (await GranterAsync(cancellationToken) is not { } granter
            || !InvitationRules.CanGrant(granter, c => CustomRoleRules.Effective(command.BaseRole, CustomRoleRules.Join(extras), c)))
            errors.Add(new("baseRole", "above_granter"));
        SettingsValidationException.ThrowIfAny(errors);
        return (name!, extras);
    }

    async Task<Func<Capability, Access>?> GranterAsync(CancellationToken cancellationToken)
    {
        if (facility.FacilityId is not { } facilityId) return null;
        var capabilities = Enum.GetValues<Capability>();
        var held = (await access.RoleAccessAsync(capabilities, facilityId, cancellationToken)).Zip(capabilities).ToDictionary(x => x.Second, x => x.First);
        return c => held[c];
    }

    static CustomRoleView ViewOf(CustomRole r) => new(r.Id, r.Name, r.BaseRole, CustomRoleRules.Split(r.Capabilities));

    async Task SaveAuditedAsync(string action, Guid roleId, object? before, object after, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        db.SettingsChanges.Add(new SettingsChange
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Section = Section, ResourceId = roleId, OccurredAt = now,
            Before = before is null ? null : JsonSerializer.Serialize(before, Json), After = JsonSerializer.Serialize(after, Json),
        });
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = "role", ResourceId = roleId,
            Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
