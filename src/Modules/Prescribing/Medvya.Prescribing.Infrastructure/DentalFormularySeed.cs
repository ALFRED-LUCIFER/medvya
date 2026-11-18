using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// Dental medicines for the shared starter list (MDV-P8-015, ADR-015): local anaesthetics in dental cartridges, the mouthwash and the antibiotics and
/// analgesics dentists prescribe that the general starter list does not already hold (amoxicillin, ibuprofen, paracetamol and omeprazole are there).
/// Doses, frequencies and durations follow DentalERP's India seed (prisma/seed-data/IN/medications.ts, MIT). NOT authoritative: a dentist approves the
/// list and a regulatory advisor confirms each class before a clinic relies on it (the intake record, docs/05-delivery/intake). Kept apart from
/// <see cref="FormularySeed"/> because the Formulary migration reads that class when it runs: rows added there would be inserted twice.
/// </summary>
public static class DentalFormularySeed
{
    const RegulatoryClass N = RegulatoryClass.None, H = RegulatoryClass.ScheduleH;

    static readonly (string Generic, string Strength, string Form, RegulatoryClass India, RegulatoryClass Uae, string Atc, string? Sig)[] Medicines =
    [
        ("Lidocaine with adrenaline", "2% with 1:80,000", "Injection", H, N, "N01BB52", null),
        ("Articaine with adrenaline", "4% with 1:100,000", "Injection", H, N, "N01BB58", null),
        ("Chlorhexidine", "0.2%", "Solution", N, N, "A01AB03", "Rinse with 10 ml for 1 minute twice a day for 7 days; do not swallow"),
        ("Metronidazole", "400 mg", "Tablet", H, N, "P01AB01", "1 tablet three times a day after food for 5 days"),
        ("Clindamycin", "300 mg", "Capsule", H, N, "J01FF01", "1 capsule three times a day for 7 days"),
        ("Diclofenac", "50 mg", "Tablet", H, N, "M01AB05", "1 tablet twice a day after food for 3 days"),
    ];

    public static IReadOnlyList<FormularySeed.Row> Rows { get; } =
    [
        .. Medicines.Select(m => new FormularySeed.Row("IN", m.Generic, m.Strength, m.Form, m.India, m.Atc, m.Sig)),
        .. Medicines.Select(m => new FormularySeed.Row("AE", m.Generic, m.Strength, m.Form, m.Uae, m.Atc, m.Sig)),
    ];
}
