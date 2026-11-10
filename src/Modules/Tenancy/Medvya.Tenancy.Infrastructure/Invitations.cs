using System.Globalization;
using System.Net.Mail;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>Settings (section Invitations).</summary>
public sealed class InvitationOptions
{
    public TimeSpan Lifetime { get; set; } = InvitationRules.DefaultLifetime;
    /// <summary>Origin of the clinic app the email links to, e.g. https://in.medvya.example. Configured, never taken from the request's Host header.</summary>
    public string? LinkBaseUrl { get; set; }
}

public sealed partial class Invitations(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, IEmailSender mail, IOptions<InvitationOptions> options,
    IStaffAccess access, ILogger<Invitations>? log = null)
    : IInvitations
{
    public async Task<IReadOnlyList<FacilityOption>> FacilitiesAsync(CancellationToken cancellationToken = default) =>
        await db.Facilities.OrderBy(f => f.Name).Select(f => new FacilityOption(f.Id, f.Name)).ToListAsync(cancellationToken);

    public Task<InvitationCreated?> CreateAsync(string email, Role role, Guid facilityId, Guid? customRoleId = null, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(email, role, facilityId, customRoleId, null, false, cancellationToken);

    public Task<InvitationCreated?> CreateAsync(string email, Role role, Guid facilityId, Action<Membership> stage, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(email, role, facilityId, null, stage, false, cancellationToken);

    public Task<InvitationCreated?> CreateAndEmailLaterAsync(string email, Role role, Guid facilityId, CancellationToken cancellationToken = default) =>
        CreateCoreAsync(email, role, facilityId, null, null, true, cancellationToken);

    async Task<InvitationCreated?> CreateCoreAsync(string email, Role role, Guid facilityId, Guid? customRoleId, Action<Membership>? stage, bool emailLater, CancellationToken cancellationToken)
    {
        email = Valid(email);
        // Every route gets the same role rule (#875): staff roles only here; a doctor only through Settings → Doctors, which stages the
        // licence and profile rows with the membership.
        if (stage is null ? !CustomRoleRules.Assignable.Contains(role) : !InvitationRules.RequiresLicenceVerification(role))
            throw new ArgumentException("This role cannot be invited.", nameof(role));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

        var facility = await db.Facilities.SingleOrDefaultAsync(f => f.Id == facilityId, cancellationToken);
        if (facility is null) return null; // another tenant's facility is invisible here (query filter + RLS)
        string? extras = null;
        if (customRoleId is { } roleId)
        {
            if (await db.CustomRoles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken) is not { } custom) return null;
            role = custom.BaseRole; // the base role is what role-based checks elsewhere see
            extras = custom.Capabilities;
        }
        // Staff are administered where the inviter administers staff (#877), and never with more patient access than the inviter has (#875).
        if (await GranterAsync(facilityId, cancellationToken) is not { } granter) return null;
        var grantedRole = role;
        if (!InvitationRules.CanGrant(granter, c => CustomRoleRules.Effective(grantedRole, extras, c)))
            throw new ArgumentException("This role gives access the inviter does not have.", nameof(role));
        var membership = new Membership { Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = facilityId, Role = role, Status = MembershipStatus.Invited, CustomRoleId = customRoleId };
        db.Memberships.Add(membership);
        stage?.Invoke(membership);
        return await IssueAsync(membership, facility, email, null, emailLater, cancellationToken);
    }

    public async Task<InvitationCreated?> ResendAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        // A revoked invitation stays revoked (#912): the admin adds the person again instead.
        var old = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.AcceptedAt == null && i.RevokedAt == null, cancellationToken);
        // Only the newest invitation of a membership can be resent, so a membership never has two live links.
        if (old is null || await db.Invitations.AnyAsync(i => i.MembershipId == old.MembershipId && i.CreatedAt > old.CreatedAt, cancellationToken)) return null;
        var membership = await db.Memberships.SingleAsync(m => m.Id == old.MembershipId, cancellationToken);
        if (membership.Status != MembershipStatus.Invited || await GranterAsync(membership.FacilityId, cancellationToken) is null) return null;
        var facility = await db.Facilities.SingleAsync(f => f.Id == membership.FacilityId, cancellationToken);
        return await IssueAsync(membership, facility, old.Email, old, false, cancellationToken);
    }

    /// <summary>The inviter's access at <paramref name="facilityId"/>, or null when they do not administer staff there.</summary>
    async Task<Func<Capability, Access>?> GranterAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        var capabilities = Enum.GetValues<Capability>();
        var held = (await access.RoleAccessAsync(capabilities, facilityId, cancellationToken)).Zip(capabilities).ToDictionary(x => x.Second, x => x.First);
        return held[Capability.StaffRolesIntegrations] == Access.Config ? c => held[c] : null;
    }

    static string Valid(string email)
    {
        email = email.Trim();
        // A bare address only: no display name, no list, at most 254 characters (RFC 5321).
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new ArgumentException("A single valid email address is required.", nameof(email));
        return email;
    }

    /// <summary>Saves a fresh invitation (and the membership when new) with its audit row and sends the email; a resend revokes the old link in the same transaction.</summary>
    async Task<InvitationCreated> IssueAsync(Membership membership, Facility facility, string email, Invitation? superseded, bool emailLater, CancellationToken cancellationToken)
    {
        var tenantId = membership.TenantId;
        var inviter = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var baseUrl = options.Value.LinkBaseUrl ?? throw new InvalidOperationException("Invitations:LinkBaseUrl is not configured.");
        var clinic = await db.Tenants.Select(t => t.Name).SingleAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (await db.Invitations.CountAsync(i => i.CreatedAt > now.AddHours(-1), cancellationToken) >= InvitationRules.MaxPerHour)
            throw new InvitationLimitException();
        var (token, hash) = InvitationToken.New(tenantId);
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, MembershipId = membership.Id, Email = email, TokenHash = hash,
            InvitedBy = inviter, CreatedAt = now, ExpiresAt = now + options.Value.Lifetime,
        };
        db.Invitations.Add(invitation);
        if (superseded is not null) superseded.RevokedAt ??= now;

        // Saved, emailed, audited and committed as one unit: a failed email or audit leaves no invitation behind. The email goes before the
        // audit row (which is written outside this transaction) so a send that fails leaves no row saying an invitation was created.
        // A commit that fails after the email went out leaves a link that shows the expired state, which is harmless.
        var message = Email(email, clinic, membership.Role, facility, $"{baseUrl.TrimEnd('/')}/invite/{token}", invitation.ExpiresAt);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (superseded is null) await StarterPlan.EnsureRoomAsync(db, tenantId, membership.Role, cancellationToken); // a resend adds no seat
        await db.SaveChangesAsync(cancellationToken);
        if (emailLater)
        {
            await Audit(tenantId, inviter, "invitation.create", invitation.Id, now, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            // shortcut: in memory, so an instance restart before the send loses the email (the invitation stays and is resent from Staff);
            // a durable queue must store the message, link token included, so it waits for the jobs host and encrypted job data.
            _ = Task.Run(() => SendLaterAsync(message), CancellationToken.None);
            return new InvitationCreated(invitation.Id, invitation.ExpiresAt, InvitationRules.RequiresLicenceVerification(membership.Role));
        }
        try
        {
            await mail.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvitationEmailException(ex);
        }
        await Audit(tenantId, inviter, superseded is null ? "invitation.create" : "invitation.resend", invitation.Id, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new InvitationCreated(invitation.Id, invitation.ExpiresAt, InvitationRules.RequiresLicenceVerification(membership.Role));
    }

    // Off the request: a cold managed-identity token or a slow mail server delays the email, never the caller (#205).
    async Task SendLaterAsync(EmailMessage message)
    {
        try { await mail.SendAsync(message, CancellationToken.None); }
        catch (Exception ex) { if (log is not null) EmailLaterFailed(log, ex.GetType().Name); } // the type only: messages can carry the address
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An invitation email sent after its commit failed: {ErrorType}. The invitation is pending; resend it from Staff.")]
    static partial void EmailLaterFailed(ILogger logger, string errorType);

    public async Task<bool> RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var facilityId = await db.Invitations.Where(i => i.Id == invitationId).Join(db.Memberships, i => i.MembershipId, m => m.Id, (_, m) => (Guid?)m.FacilityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (facilityId is null || await GranterAsync(facilityId.Value, cancellationToken) is null) return false;
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
        Role.SoloDoctor => "Doctor", Role.HospitalDoctor => "Doctor", Role.Receptionist => "Receptionist", Role.Nurse => "Nurse",
        Role.CareCoordinator => "Care coordinator", Role.PracticeAdmin => "Practice admin", Role.HospitalIt => "Hospital IT",
        _ => role.ToString(),
    };
}
