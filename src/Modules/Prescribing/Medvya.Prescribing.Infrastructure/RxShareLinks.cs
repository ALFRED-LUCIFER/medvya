using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Notifications.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The public side of a share link (MDV-P2-020), run inside the link's tenant (row-level security on). A link opens only while it is live and its
/// prescription version is still the latest, and only with the latest one-time code sent to the patient's registered mobile: five wrong tries end a
/// code, five codes end a link. Every answer for a known link is audited with the patient and no actor (rx.share.code, rx.share.open).
/// </summary>
public sealed class RxShareLinks(PrescribingDbContext db, IEncounterDirectory encounters, ISecureLinkMessages messages, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock)
    : IRxShareLinks
{
    public const string CodeAction = "rx.share.code", OpenAction = "rx.share.open";

    public async Task<bool> SendCodeAsync(byte[] linkHash, CancellationToken cancellationToken = default)
    {
        if (await LiveAsync(linkHash, cancellationToken) is not { } share) return false;
        var patient = await PatientAsync(share, cancellationToken);
        await RefuseIfReplacedAsync(share, patient, CodeAction, cancellationToken);
        var now = clock.GetUtcNow();
        var sent = await db.RxShareCodes.AsNoTracking().Where(c => c.ShareId == share.Id).Select(c => c.SentAt).ToListAsync(cancellationToken);
        if (sent.Count >= RxShareRules.MaxCodes) await RefuseAsync(share, patient, CodeAction, "code_limit", cancellationToken);
        if (sent.Count > 0 && now - sent.Max() < RxShareRules.CodeInterval) await RefuseAsync(share, patient, CodeAction, "too_soon", cancellationToken);

        var code = RxShareRules.NewCode();
        var id = Guid.CreateVersion7();
        var row = new RxShareCode { Id = id, TenantId = share.TenantId, ShareId = share.Id, SentAt = now, ExpiresAt = now + RxShareRules.CodeLifetime, CodeHash = RxShareRules.HashCode(id, code) };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RxShareCodes.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await messages.SendCodeAsync(patient, share.FacilityId, code, row.Id, cancellationToken);
        }
        catch (SecureLinkNotSentException e)
        {
            await tx.RollbackAsync(cancellationToken);
            await RefuseAsync(share, patient, CodeAction, e.Code, cancellationToken);
        }
        await audit.WriteAsync(Event(CodeAction, share, patient, AuditOutcome.Allowed, "sent"), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<RxPdf?> OpenAsync(byte[] linkHash, string? code, CancellationToken cancellationToken = default)
    {
        if (await LiveAsync(linkHash, cancellationToken) is not { } share) return null;
        var patient = await PatientAsync(share, cancellationToken);
        await RefuseIfReplacedAsync(share, patient, OpenAction, cancellationToken);
        var now = clock.GetUtcNow();
        var live = await db.RxShareCodes.AsNoTracking().Where(c => c.ShareId == share.Id).OrderByDescending(c => c.SentAt).ThenByDescending(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        // Each try is claimed in the database first, so parallel guesses still stop at the limit.
        if (live is null || live.UsedAt is not null || live.ExpiresAt <= now
            || await db.RxShareCodes.Where(c => c.Id == live.Id && c.UsedAt == null && c.Attempts < RxShareRules.MaxAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken) == 0)
            await RefuseAsync(share, patient, OpenAction, "no_code", cancellationToken);
        if (!RxShareRules.CodeMatches(live!, code)) await RefuseAsync(share, patient, OpenAction, "wrong_code", cancellationToken);
        if (await db.RxShareCodes.Where(c => c.Id == live!.Id && c.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken) == 0)
            await RefuseAsync(share, patient, OpenAction, "no_code", cancellationToken);

        var pdf = await db.PrescriptionPdfs.AsNoTracking().SingleAsync(p => p.Id == share.PdfId, cancellationToken);
        await audit.WriteAsync(Event(OpenAction, share, patient, AuditOutcome.Allowed, $"recipient:{share.Recipient}"), cancellationToken);
        return new RxPdf(pdf.Content, pdf.Sha256);
    }

    Task<RxShare?> LiveAsync(byte[] linkHash, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.RxShares.AsNoTracking().SingleOrDefaultAsync(s => s.LinkHash == linkHash && s.ExpiresAt > now, ct);
    }

    async Task<Guid> PatientAsync(RxShare share, CancellationToken ct) =>
        (await encounters.FindAsync(share.EncounterId, ct) ?? throw new InvalidOperationException("Encounter not in this tenant.")).PatientId;

    /// <summary>The doctor cancelled the shared version, or a later authorisation replaced it: the link says so and opens nothing.</summary>
    async Task RefuseIfReplacedAsync(RxShare share, Guid patient, string action, CancellationToken ct)
    {
        if (await db.PrescriptionCancellations.AsNoTracking().AnyAsync(c => c.PrescriptionId == share.PrescriptionId, ct))
            await RefuseAsync(share, patient, action, "cancelled", ct);
        var shared = await db.AuthorisedPrescriptions.AsNoTracking().Where(p => p.Id == share.PrescriptionId).Select(p => p.VersionNo).SingleAsync(ct);
        if (await db.AuthorisedPrescriptions.AsNoTracking().AnyAsync(p => p.EncounterId == share.EncounterId && p.VersionNo > shared, ct))
            await RefuseAsync(share, patient, action, "replaced", ct);
    }

    async Task RefuseAsync(RxShare share, Guid patient, string action, string code, CancellationToken ct)
    {
        await audit.WriteAsync(Event(action, share, patient, AuditOutcome.Denied, code), ct);
        throw new PrescriptionConflictException(code);
    }

    AuditEvent Event(string action, RxShare share, Guid patient, AuditOutcome outcome, string detail) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), Action = action, ResourceType = "prescription_share",
        ResourceId = share.Id, PatientId = patient, FacilityId = share.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = outcome, OccurredAt = clock.GetUtcNow(), Detail = detail,
    };
}
