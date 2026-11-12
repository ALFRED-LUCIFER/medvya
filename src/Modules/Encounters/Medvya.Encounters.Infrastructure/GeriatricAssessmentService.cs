using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>The older adult review (MDV-P2-037); see <see cref="IGeriatricAssessments"/>. Input is validated here against <see cref="GeriatricRules"/>.</summary>
public sealed class GeriatricAssessmentService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IGeriatricAssessments
{
    public const string ReadAction = "patient.geriatric.read", RecordAction = "patient.geriatric.record";
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    static readonly Access[] Records = [Access.Full];

    public async Task<IReadOnlyList<GeriatricAssessmentView>> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, patientId), cancellationToken);
        var rows = await db.GeriatricAssessments.AsNoTracking().Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AssessedAt).ThenByDescending(a => a.Id).Take(IGeriatricAssessments.MaxHistory).ToListAsync(cancellationToken);
        return [.. rows.Select(View)];
    }

    public async Task<GeriatricAssessmentView> RecordAsync(Guid patientId, Guid facilityId, RecordGeriatricAssessment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Records, cancellationToken);
        // A value too large to hold is out of scale too, so the conversion can never overflow.
        int? tug = command.TimedUpAndGoSeconds is { } s ? s is >= 0 and <= 1000 ? (int)Math.Round(s * 10, MidpointRounding.AwayFromZero) : -1 : null;
        var row = new GeriatricAssessment
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), PatientId = patientId, FacilityId = facilityId,
            FallsLast12Months = command.FallsLast12Months, TimedUpAndGoTenths = tug,
            LyingSystolic = command.LyingSystolic, LyingDiastolic = command.LyingDiastolic, StandingSystolic = command.StandingSystolic, StandingDiastolic = command.StandingDiastolic,
            ClinicalFrailtyScale = command.ClinicalFrailtyScale, MiniCog = command.MiniCog, Adl = command.Adl, Iadl = command.Iadl, Phq2 = command.Phq2,
            NutritionNote = command.NutritionNote?.Trim() is { Length: > 0 } note ? note : null,
            RecordedBy = user.UserId ?? throw new InvalidOperationException("No user."), AssessedAt = clock.GetUtcNow(),
        };
        if (GeriatricRules.Validate(row) is { Count: > 0 } errors) throw new GeriatricValidationException(errors);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.GeriatricAssessments.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(RecordAction, patientId), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(row);
    }

    static GeriatricAssessmentView View(GeriatricAssessment a)
    {
        var drop = a is { LyingSystolic: { } ls, LyingDiastolic: { } ld, StandingSystolic: { } ss, StandingDiastolic: { } sd } ? GeriatricRules.Drop(ls, ld, ss, sd) : ((int, int, bool)?)null;
        return new(a.Id, a.AssessedAt, a.RecordedBy, a.FallsLast12Months, a.TimedUpAndGoTenths / 10m, a.TimedUpAndGoTenths is { } t ? GeriatricRules.TugAtRisk(t) : null,
            a.LyingSystolic, a.LyingDiastolic, a.StandingSystolic, a.StandingDiastolic, drop?.Item1, drop?.Item2, drop?.Item3,
            a.ClinicalFrailtyScale, a.MiniCog, a.Adl, a.Iadl, a.Phq2, a.NutritionNote);
    }

    AuditEvent Event(string action, Guid patientId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ActorUserId = user.UserId, Action = action,
        ResourceType = "patient", ResourceId = patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };
}
