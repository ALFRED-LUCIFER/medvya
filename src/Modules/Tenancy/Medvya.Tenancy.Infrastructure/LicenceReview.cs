using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>The licence decision of Medvya operations (MDV-P1-007); see <see cref="ILicenceReview"/>.</summary>
public sealed class LicenceReview(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, IEmailSender mail, TimeProvider clock) : ILicenceReview
{
    public async Task<bool> DecideAsync(Guid doctorId, bool verified, string? reason, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var reviewer = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var errors = new List<ValidationError>();
        if (!verified && reason is null) errors.Add(new("reason", "required"));
        else if (reason?.Length > 500) errors.Add(new("reason", "too_long"));
        SettingsValidationException.ThrowIfAny(errors);

        var licence = await db.DoctorLicences.SingleOrDefaultAsync(l => l.MembershipId == doctorId, cancellationToken);
        if (licence is null) return false;
        var now = clock.GetUtcNow();
        var before = new { licence = LicenceRules.StatusOf(licence, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).ToString() };
        licence.VerifiedAt = verified ? now : null;
        licence.VerifiedBy = verified ? reviewer : null;
        licence.RejectedAt = verified ? null : now;
        licence.RejectionReason = verified ? null : reason;
        // The owner hears why by email (the practice contact address, when set) and sees the reason on the Doctors tab. Sent before the
        // commit: a failed email undoes the decision, so operations repeat it rather than leave an owner uninformed.
        var contact = verified ? null : await db.Profiles.AsNoTracking().Select(p => p.Email).SingleOrDefaultAsync(cancellationToken);
        var clinic = await db.Tenants.AsNoTracking().Select(t => t.Name).SingleAsync(cancellationToken);
        await SettingsAudit.SaveAsync(db, audit, tenantId, reviewer, now, verified ? "licence.verify" : "licence.reject", "licence", doctorId,
            before, new { licence = verified ? "Verified" : "Rejected" }, cancellationToken,
            contact is null ? null : ct => mail.SendAsync(new EmailMessage(contact, $"A doctor's licence at {clinic} was not verified",
                $"""
                Medvya could not verify a doctor's licence at {clinic}.

                Reason: {reason}

                Open Settings, Doctors in Medvya to correct the licence and send it again. Until it is verified the doctor can book and register patients but cannot record or sign.
                """), ct));
        return true;
    }
}
