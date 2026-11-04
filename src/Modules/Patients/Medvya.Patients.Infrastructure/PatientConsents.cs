using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Patients.Infrastructure;

/// <summary>Consent receipts (MDV-P1-019): written with the registration, changed by new receipts, read by other modules through <see cref="IPatientConsents"/>.</summary>
public sealed class PatientConsents(PatientsDbContext db, ICurrentTenant tenant, ICurrentFacility facility, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : IPatientConsents
{
    internal static IEnumerable<ConsentReceipt> Receipts(Guid tenantId, Guid patientId, ConsentSet set, string jurisdiction, DateTimeOffset now, Guid? actor) =>
        set.Decisions.Select(d => new ConsentReceipt
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patientId, Type = d.Type, Channel = d.Channel, PolicyVersion = set.PolicyVersion,
            Jurisdiction = jurisdiction, Granted = d.Granted, DecidedAt = now, DecidedBy = actor,
        });

    public async Task<bool> IsGrantedAsync(Guid patientId, ConsentType type, ConsentChannel? channel = null, CancellationToken cancellationToken = default)
    {
        // Newest receipt wins; none means no consent.
        var newest = await db.ConsentReceipts.AsNoTracking().Where(r => r.PatientId == patientId && r.Type == type && r.Channel == channel)
            .OrderByDescending(r => r.DecidedAt).Select(r => (bool?)r.Granted).FirstOrDefaultAsync(cancellationToken);
        return newest == true;
    }

    public async Task<IReadOnlyList<ConsentDecisionInput>?> ChangeAsync(Guid patientId, ConsentSet consents, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var facilityId = facility.FacilityId ?? throw new InvalidOperationException("No facility context.");
        // Facility-scoped like the registration reads: another facility's patient is not found and nothing is written.
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId && p.FacilityId == facilityId)
            .Select(p => new { p.CountryCode, Region = p.Address != null ? p.Address.Region : null }).SingleOrDefaultAsync(cancellationToken);
        if (patient is null) return null;
        var set = ConsentRules.ForChange(consents);
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ConsentReceipts.AddRange(Receipts(tenantId, patientId, set, ConsentRules.Jurisdiction(patient.CountryCode, patient.Region), now, user.UserId));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = user.UserId, Action = "patient.consent.record", ResourceType = "patient",
            ResourceId = patientId, PatientId = patientId, Purpose = "registration", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return set.Decisions;
    }
}
