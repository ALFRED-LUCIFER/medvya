using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Patients.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>The older adult medicine review; see <see cref="IMedicineReview"/>. Reads the chart through Encounters' contract, never its tables.</summary>
public sealed class MedicineReviewService(
    PrescribingDbContext db, IPrescribingChart chart, IClinicalAccess clinical, IStaffAccess staff, IPatientDirectory patients,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IMedicineReview
{
    public const string ReadAction = "patient.medicine-review.read";
    static readonly Access[] Doctors = [Access.Sign];

    public async Task<MedicineReviewView?> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        // Existence only comes before the access check; nothing about the patient is returned until it passes.
        if ((await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId) is not { } patient) return null;
        await clinical.RequireAsync(patientId, facilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        var facts = await chart.FactsAsync(patientId, cancellationToken);
        var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
        var now = clock.GetUtcNow();
        var context = ChartMatch.Patient(patient, country, facts, now);
        var generics = await ChartMatch.GenericsAsync(db, country, context.AgeYears >= SafetyRules.OlderAdultAge ? facts.ActiveMedicines.Count : 0, cancellationToken);
        var result = MedicineReview.Evaluate([.. facts.ActiveMedicines.Select(m => new ReviewMedicine(m.Id, m.Name, ChartMatch.Atc(m.Name, generics), m.RecordedAt))],
            context, facts.Potassium?.ValueHundredths, now);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ActorUserId = user.UserId, Action = ReadAction,
            ResourceType = "patient", ResourceId = patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = now,
            Detail = $"suggestions:{result.Suggestions.Count};not_checked:{result.NotChecked.Count}",
        }, cancellationToken);
        return new(result.Suggestions, result.NotChecked, facts.ActiveMedicines.Count, context.AgeYears >= SafetyRules.OlderAdultAge, MedicineReview.SignOff is not null);
    }
}
