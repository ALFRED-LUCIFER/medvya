using System.Globalization;
using System.Net.Mail;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>Settings (section Invitations).</summary>
public sealed class InvitationOptions
{
    public TimeSpan Lifetime { get; set; } = InvitationRules.DefaultLifetime;
    /// <summary>Origin of the clinic app the email links to, e.g. https://in.medvya.example. Configured, never taken from the request's Host header.</summary>
    public string? LinkBaseUrl { get; set; }
}

public sealed class Invitations(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, IEmailSender mail, IOptions<InvitationOptions> options)
    : IInvitations
{
    public async Task<IReadOnlyList<FacilityOption>> FacilitiesAsync(CancellationToken cancellationToken = default) =>
        await db.Facilities.OrderBy(f => f.Name).Select(f => new FacilityOption(f.Id, f.Name)).ToListAsync(cancellationToken);

    public async Task<InvitationCreated?> CreateAsync(string email, Role role, Guid facilityId, CancellationToken cancellationToken = default)
    {
        email = email.Trim();
        // A bare address only: no display name, no list, at most 254 characters (RFC 5321).
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new ArgumentException("A single valid email address is required.", nameof(email));
        if (!InvitationRules.CanInvite(role)) throw new ArgumentException("This role cannot be invited.", nameof(role));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var inviter = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var baseUrl = options.Value.LinkBaseUrl ?? throw new InvalidOperationException("Invitations:LinkBaseUrl is not configured.");

        var facility = await db.Facilities.SingleOrDefaultAsync(f => f.Id == facilityId, cancellationToken);
        if (facility is null) return null; // another tenant's facility is invisible here (query filter + RLS)
        var clinic = await db.Tenants.Select(t => t.Name).SingleAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var (token, hash) = InvitationToken.New(tenantId);
        var membership = new Membership { Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = facilityId, Role = role, Status = MembershipStatus.Invited };
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, MembershipId = membership.Id, Email = email, TokenHash = hash,
            InvitedBy = inviter, CreatedAt = now, ExpiresAt = now + options.Value.Lifetime,
        };
        db.Memberships.Add(membership);
        db.Invitations.Add(invitation);

        // Saved, audited and emailed inside one transaction: a failed audit or email leaves no invitation behind.
        // A commit that fails after the email went out leaves a link that shows the expired state, which is harmless.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await Audit(tenantId, inviter, "invitation.create", invitation.Id, now, cancellationToken);
        await mail.SendAsync(Email(email, clinic, role, facility, $"{baseUrl.TrimEnd('/')}/invite/{token}", invitation.ExpiresAt), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new InvitationCreated(invitation.Id, invitation.ExpiresAt, InvitationRules.RequiresLicenceVerification(role));
    }

    public async Task<bool> RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var revoked = await db.Invitations.Where(i => i.Id == invitationId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), cancellationToken);
        if (revoked == 0) return false;
        await Audit(tenantId, user.UserId, "invitation.revoke", invitationId, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<InvitationPreview?> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        if (InvitationToken.Read(token) is not { } t || t.TenantId != tenant.TenantId) return null;
        var now = DateTimeOffset.UtcNow;
        var x = await (
            from i in db.Invitations
            where i.TokenHash == t.Hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now
            join m in db.Memberships on i.MembershipId equals m.Id
            join f in db.Facilities on m.FacilityId equals f.Id
            join c in db.Tenants on i.TenantId equals c.Id
            select new { c.Name, Facility = f.Name, i.InvitedBy, m.Role, i.ExpiresAt }).SingleOrDefaultAsync(cancellationToken);
        return x is null ? null : new InvitationPreview(x.Name, x.Facility, x.InvitedBy, x.Role, x.ExpiresAt, InvitationRules.RequiresLicenceVerification(x.Role));
    }

    public async Task<bool> AcceptAsync(string token, Guid userId, CancellationToken cancellationToken = default)
    {
        if (InvitationToken.Read(token) is not { } t || t.TenantId != tenant.TenantId) return false;
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // The conditional update is the single-use guard: of two concurrent acceptances, only one changes a row.
        var claimed = await db.Invitations.Where(i => i.TokenHash == t.Hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now), cancellationToken);
        if (claimed != 1) return false;

        var invitation = await db.Invitations.SingleAsync(i => i.TokenHash == t.Hash, cancellationToken);
        var membership = await db.Memberships.SingleAsync(m => m.Id == invitation.MembershipId, cancellationToken);
        if (membership.Status != MembershipStatus.Invited
            || await db.Memberships.AnyAsync(m => m.UserId == userId && m.FacilityId == membership.FacilityId, cancellationToken))
            return false; // already joined this facility: the unique index would refuse it anyway
        membership.UserId = userId;
        membership.Status = MembershipStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        await Audit(t.TenantId, userId, "invitation.accept", invitation.Id, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    // shortcut: the audit store is a separate DbContext without a shared transaction, as in FeatureFlags; a commit failing after
    // the audit write leaves an audit row for a change that did not land. Share the transaction when an outbox exists.
    Task Audit(Guid tenantId, Guid? actor, string action, Guid invitationId, DateTimeOffset at, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = actor, Action = action, ResourceType = "invitation",
            ResourceId = invitationId, Purpose = "support", Outcome = AuditOutcome.Allowed, OccurredAt = at,
        }, ct);

    // shortcut: English-only email; send it in the invitee's language once notification templates are localised (MDV-P1-032).
    // No patient data: clinic, role, facility, link and expiry only.
    static EmailMessage Email(string to, string clinic, Role role, Facility facility, string link, DateTimeOffset expires)
    {
        var local = TimeZoneInfo.ConvertTime(expires, TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone));
        var date = local.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        return new EmailMessage(to, $"Invitation to join {clinic} on Medvya",
            $"""
            You have been invited to join {clinic} on Medvya as {RoleName(role)} at {facility.Name}.

            Accept the invitation: {link}

            The link works once and expires on {date} ({facility.TimeZone}). If you were not expecting this invitation, ignore this email.
            """);
    }

    static string RoleName(Role role) => role switch
    {
        Role.SoloDoctor => "Doctor", Role.HospitalDoctor => "Hospital doctor", Role.Receptionist => "Receptionist", Role.Nurse => "Nurse",
        Role.CareCoordinator => "Care coordinator", Role.PracticeAdmin => "Practice admin", Role.HospitalIt => "Hospital IT",
        _ => role.ToString(),
    };
}
