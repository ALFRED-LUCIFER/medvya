using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>
/// The older adult medicine review of one patient: suggestions only (the doctor decides), what could not be reviewed, how many active chart
/// medicines were read, whether the patient is in the older adult band at all, and whether the rule set is signed off (false: a draft).
/// </summary>
public sealed record MedicineReviewView(
    IReadOnlyList<ReviewSuggestion> Suggestions, IReadOnlyList<ReviewNotChecked> NotChecked, int Medicines, bool OlderAdult, bool RulesSignedOff);

/// <summary>
/// The older adult medicine review (MDV-P2-037). Read only: it never changes a medicine; a planned change goes into the consultation plan.
/// Needs Prescriptions and orders at Sign (a doctor) and the care relationship at the facility (a ClinicalAccessDeniedException), and audits with the patient.
/// </summary>
public interface IMedicineReview
{
    /// <summary>Null: no such patient in this tenant.</summary>
    Task<MedicineReviewView?> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);
}
