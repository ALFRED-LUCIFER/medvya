using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The QR verify check (MDV-P2-017), run inside the token's tenant (row-level security on). It answers validity, the paper's number and date, the
/// clinic and doctor, and the number of medicines: what the paper itself shows, never the patient or the medicines. Each answer is audited with the
/// patient (no actor: the scanner is not signed in), so the patient's access log shows the check.
/// </summary>
public sealed class PrescriptionVerification(PrescribingDbContext db, IEncounterDirectory encounters, IFacilityDirectory facilities, ICurrentTenant tenant, IAuditLog audit, TimeProvider clock)
    : IPrescriptionVerification
{
    public const string VerifyAction = "rx.verify";

    public async Task<RxVerifyView?> VerifyAsync(string verifyCode, CancellationToken cancellationToken = default)
    {
        if (await db.AuthorisedPrescriptions.AsNoTracking().SingleOrDefaultAsync(p => p.VerifyCode == verifyCode, cancellationToken) is not { } rx) return null;
        var latest = await db.AuthorisedPrescriptions.AsNoTracking().Where(p => p.EncounterId == rx.EncounterId).MaxAsync(p => p.VersionNo, cancellationToken);
        var status = latest == rx.VersionNo ? RxValidity.Valid : RxValidity.Replaced;
        var facility = await facilities.FindAsync(rx.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var doctor = await facilities.PrescriberAsync(rx.AuthorisedBy, rx.FacilityId, cancellationToken);
        var encounter = await encounters.FindAsync(rx.EncounterId, cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), Action = VerifyAction, ResourceType = PrescriptionService.Resource,
            ResourceId = rx.Id, PatientId = encounter?.PatientId, FacilityId = rx.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed,
            OccurredAt = clock.GetUtcNow(), Detail = $"status:{status};version:{rx.VersionNo}",
        }, cancellationToken);
        return new RxVerifyView(status, rx.Number(), rx.VersionNo, rx.AuthorisedAt, facility.TimeZone, facility.Name, doctor?.FullName ?? "", rx.Lines.Count(l => !l.Discontinue));
    }
}
