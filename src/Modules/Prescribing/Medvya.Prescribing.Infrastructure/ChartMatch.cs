using Medvya.Encounters.Application;
using Medvya.Patients.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>How the prescription checks (MDV-P2-016, P2-040) and the older adult review (MDV-P2-037) read the chart: one place, so they never disagree.</summary>
static class ChartMatch
{
    /// <summary>
    /// The country's formulary generics with their ATC code (null when one generic has several). Read only when there are chart medicines to
    /// match (a clinic's formulary can hold thousands of rows).
    /// </summary>
    public static async Task<Dictionary<string, string?>> GenericsAsync(PrescribingDbContext db, string country, int chartMedicines, CancellationToken cancellationToken) =>
        chartMedicines == 0 ? new(StringComparer.OrdinalIgnoreCase)
        : (await db.FormularyItems.AsNoTracking().Where(f => f.Country == country && f.Active && f.Atc != null).Select(f => new { f.Generic, f.Atc }).ToListAsync(cancellationToken))
            .GroupBy(f => f.Generic, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(f => f.Atc!).Distinct().Count() == 1 ? g.First().Atc : null, StringComparer.OrdinalIgnoreCase);

    /// <summary>A chart medicine is matched only when its name names one formulary generic with one ATC code; otherwise null (not checked).</summary>
    public static string? Atc(string name, Dictionary<string, string?> generics) => SafetyRules.MatchGeneric(name, generics.Keys) is { } g ? generics[g] : null;

    /// <summary>
    /// The patient facts the rules read. eGFR in whole units, rounded down so a reading just under a threshold counts as under it.
    /// The pregnancy weeks and a "not pregnant" statement for today come from the chart (MDV-P2-034).
    /// shortcut: the latest eGFR at any age; an eGFR age limit when the clinical reviewer sets one (MDV-P2-028).
    /// </summary>
    public static PatientContext Patient(PatientSummary patient, string country, PrescribingFacts facts, ChartPregnancy pregnancy, DateTimeOffset now) =>
        new(CountryRules.AgeOn(CountryRules.For(country).Today(now), patient.DateOfBirth), patient.SexAtBirth == Medvya.Patients.Domain.SexAtBirth.Female,
            facts.Weight is { } w && w.TakenAt >= now.AddMonths(-1) ? w.ValueHundredths : null, facts.Egfr?.ValueHundredths / 100, pregnancy.GestationWeeks, pregnancy.RuledOut);
}
