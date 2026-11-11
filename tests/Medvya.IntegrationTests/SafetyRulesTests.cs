using System.Globalization;
using Medvya.Prescribing.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-016 exit gate 2: the seeded conflict set (every allergy class and its words, every cross-sensitivity, every interacting pair, duplicates
/// and every maximum dose) raises its expected alert in 100 % of cases. The rule set is pure, so no database is needed here; PrescriptionCheckTests
/// runs the same checks through the service on PostgreSQL. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class SafetyRulesTests
{
    static int next;

    static CheckedMedicine Line(string atc, string name = "Synthetic medicine", string? strength = null, SigParts? sig = null) =>
        new(Id(), name, atc, strength, sig ?? new SigParts(null, null, null, null, null, null, null, null), false, false);

    static CheckedMedicine Chart(string? atc, string name = "Synthetic chart medicine") => new(Id(), name, atc, null, null, true, false);

    static Guid Id() => new(Interlocked.Increment(ref next), 0, 0, new byte[8]);

    static string Atc(string prefix) => prefix.PadRight(7, '0');

    static SafetyResult Run(IReadOnlyList<CheckedMedicine> lines, IReadOnlyList<CheckedMedicine>? chart = null, ChartAllergies state = ChartAllergies.Listed, params CheckedAllergy[] allergies) =>
        SafetyRules.Evaluate(lines, chart ?? [], state, allergies);

    static CheckedAllergy Allergy(string substance, string status = "Confirmed") => new(Id(), substance, status);

    /// <summary>SAFETY (mutation check: drop the class match, or a class's words, in SafetyRules and this fails).</summary>
    [Fact]
    public void EveryAllergyClassWordRaisesASevereAllergyAlertOnEveryMedicineOfTheClass()
    {
        var misses = new List<string>();
        foreach (var cls in SafetyRules.Classes)
            foreach (var term in cls.Terms)
                foreach (var prefix in cls.Atc)
                {
                    var allergy = Allergy($"{term} (synthetic)");
                    var alert = Run([Line(Atc(prefix))], allergies: allergy).Alerts.SingleOrDefault();
                    if (alert is not { Kind: RxAlertKind.Allergy, Severity: RxSeverity.Severe } || alert.Code != cls.Id || alert.Substance != allergy.Substance || alert.AllergyStatus != "Confirmed")
                        misses.Add($"{cls.Id}/{term}/{prefix}");
                }
        Assert.Empty(misses);
    }

    /// <summary>SAFETY (mutation check: drop the cross-sensitivity branch and this fails).</summary>
    [Fact]
    public void EveryCrossSensitivityRaisesAModerateAlert()
    {
        var misses = new List<string>();
        foreach (var cls in SafetyRules.Classes)
            foreach (var prefix in cls.Cross)
            {
                var alert = Run([Line(Atc(prefix))], allergies: Allergy(cls.Terms[0])).Alerts.SingleOrDefault();
                if (alert is not { Kind: RxAlertKind.CrossSensitivity, Severity: RxSeverity.Moderate } || alert.Code != cls.Id) misses.Add($"{cls.Id}/{prefix}");
            }
        Assert.Empty(misses);
        Assert.Contains(SafetyRules.Classes, c => c.Cross.Count > 0);
    }

    /// <summary>SAFETY (mutation check: match the name on a part of a word, or drop the ingredient match, and this fails).</summary>
    [Fact]
    public void AnAllergyToTheIngredientItselfAlertsAndOnlyWholeWordsMatch()
    {
        var alert = Assert.Single(Run([Line("N02BE01", "Paracetamol")], allergies: Allergy("Paracetamol", "PatientReported")).Alerts);
        Assert.Equal((RxAlertKind.Allergy, RxSeverity.Severe, "ingredient", "PatientReported"), (alert.Kind, alert.Severity, alert.Code, alert.AllergyStatus));
        Assert.Empty(Run([Line("N02BE01", "Paracetamol")], allergies: Allergy("Paracetamolx")).Alerts);
        Assert.Empty(Run([Line("C10AA05", "Atorvastatin")], allergies: Allergy("Penicillin")).Alerts); // another class: nothing
    }

    /// <summary>SAFETY (mutation check: drop a rule's reverse order, or the chart medicines, from the pair check and this fails).</summary>
    [Fact]
    public void EveryInteractingPairAlertsInEitherOrderAndAgainstTheChart()
    {
        var misses = new List<string>();
        foreach (var rule in SafetyRules.Interactions)
            foreach (var a in rule.A)
                foreach (var b in rule.B)
                {
                    var cases = new[] { Run([Line(Atc(a)), Line(Atc(b))]), Run([Line(Atc(b)), Line(Atc(a))]), Run([Line(Atc(a))], [Chart(Atc(b))]), Run([Line(Atc(b))], [Chart(Atc(a))]) };
                    for (var i = 0; i < cases.Length; i++)
                        if (!cases[i].Alerts.Any(x => x.Kind == RxAlertKind.Interaction && x.Severity == rule.Severity && x.Code == rule.Effect && x.OtherOnChart == i >= 2))
                            misses.Add($"{rule.Id}/{a}/{b}/case {i}");
                }
        Assert.Empty(misses);
        Assert.Contains(SafetyRules.Interactions, r => r.Severity == RxSeverity.Severe);
    }

    [Fact]
    public void TwoChartMedicinesTogetherAreNotThisPrescriptionsFinding() =>
        Assert.DoesNotContain(Run([Line("N02BE01")], [Chart("N02AX02"), Chart("N05BA12")]).Alerts, a => a.Kind == RxAlertKind.Interaction);

    /// <summary>SAFETY (mutation check: drop the duplicate check and this fails).</summary>
    [Fact]
    public void DuplicateTherapyAlertsForTheSameClassAndForTheSameMedicineTwiceButNotForAChartContinuation()
    {
        var sameClass = Assert.Single(Run([Line("A02BC01", "Omeprazole"), Line("A02BC02", "Pantoprazole")]).Alerts);
        Assert.Equal((RxAlertKind.Duplicate, RxSeverity.Moderate, "same_class", "Omeprazole", "Pantoprazole"), (sameClass.Kind, sameClass.Severity, sameClass.Code, sameClass.Medicine, sameClass.Other));
        Assert.Equal("same_medicine", Assert.Single(Run([Line("C10AA05"), Line("C10AA05")]).Alerts).Code);
        Assert.True(Assert.Single(Run([Line("A02BC01")], [Chart("A02BC02")]).Alerts).OtherOnChart);
        Assert.Empty(Run([Line("A10BA02")], [Chart("A10BA02")]).Alerts); // the chart's own medicine prescribed again is a continuation
    }

    /// <summary>SAFETY (mutation check: compare with >= instead of >, or drop the dose check, and this fails).</summary>
    [Fact]
    public void EveryMaximumDailyDoseAlertsAboveItAndNotAtIt()
    {
        var misses = new List<string>();
        foreach (var (atc, max) in SafetyRules.MaxDailyMicrograms)
        {
            SigParts Mg(long micrograms) => new((micrograms / 1000m).ToString(CultureInfo.InvariantCulture), "mg", "oral", "OD", null, null, null, null);
            var above = Run([Line(atc, sig: Mg(max + 1))]).Alerts.SingleOrDefault();
            if (above is not { Kind: RxAlertKind.DoseAboveMaximum, Severity: RxSeverity.Severe } || above.DailyMicrograms != max + 1 || above.MaxDailyMicrograms != max) misses.Add($"{atc} above");
            if (Run([Line(atc, sig: Mg(max))]).Alerts.Count > 0) misses.Add($"{atc} at");
        }
        Assert.Empty(misses);
    }

    [Fact]
    public void TheDailyDoseIsCountedFromTabletsAndStrengthAndAnUncountableDoseIsReportedNotGuessed()
    {
        static SigParts Tabs(string dose, string frequency) => new(dose, "tablet", "oral", frequency, null, null, null, null);
        Assert.Empty(Run([Line("N02BE01", "Paracetamol", "500 mg", Tabs("2", "QID"))]).Alerts); // 4 g: the maximum, not above it
        var over = Assert.Single(Run([Line("N02BE01", "Paracetamol", "500 mg", Tabs("3", "QID"))]).Alerts);
        Assert.Equal((6_000_000L, 4_000_000L), (over.DailyMicrograms!.Value, over.MaxDailyMicrograms!.Value));
        Assert.StartsWith($"dose:{over.ItemId}:6000000", over.Key, StringComparison.Ordinal); // a different dose is a different finding
        Assert.Equal(1_000L, SafetyRules.DailyMicrograms(Line("H03AA01", strength: "50 mcg", sig: new("10", "tablet", null, "BD", null, null, null, null))));

        foreach (var sig in new SigParts[] { Tabs("8", "SOS"), Tabs("8", "WEEKLY"), new("50", "ml", null, "QID", null, null, null, null), Tabs("8", null!), new(null, "tablet", null, "QID", null, null, null, null) })
        {
            var r = Run([Line("N02BE01", "Paracetamol", "500 mg", sig)]);
            Assert.Empty(r.Alerts);
            Assert.Equal(NotCheckedReason.DoseNotChecked, Assert.Single(r.NotChecked).Reason);
        }
        Assert.Equal(NotCheckedReason.DoseNotChecked, Assert.Single(Run([Line("R03AC02")]).NotChecked).Reason); // no maximum in the rule set
    }

    /// <summary>SAFETY (mutation check: drop the not-recorded alert, or raise it for "no known allergies", and this fails).</summary>
    [Fact]
    public void NotRecordedAllergiesAreSaidAndNeverReadAsNone()
    {
        var alert = Assert.Single(Run([Line("N02BE01")], state: ChartAllergies.NotRecorded).Alerts);
        Assert.Equal((RxAlertKind.AllergiesNotRecorded, RxSeverity.Moderate, (Guid?)null), (alert.Kind, alert.Severity, alert.ItemId));
        Assert.Empty(Run([Line("N02BE01")], state: ChartAllergies.NoKnownAllergies).Alerts);
        Assert.Empty(Run([], state: ChartAllergies.NotRecorded).Alerts); // nothing prescribed yet, nothing to say
    }

    /// <summary>SAFETY (mutation check: check a free-text line or one without an ATC code, or stop reporting it, and this fails).</summary>
    [Fact]
    public void FreeTextAndUncodedMedicinesAreReportedAsNotCheckedAndRaiseNothing()
    {
        var free = new CheckedMedicine(Id(), "Amoxicillin syrup (typed)", null, null, null, false, true);
        var uncoded = Line(null!, "Clinic medicine");
        var r = Run([free, uncoded, Line("J01CA04")], [Chart(null, "Herbal tonic")], ChartAllergies.Listed, Allergy("Penicillin"));
        Assert.Equal([NotCheckedReason.FreeText, NotCheckedReason.NoIngredientCode, NotCheckedReason.DoseNotChecked, NotCheckedReason.NotOnFormulary], r.NotChecked.Select(n => n.Reason));
        Assert.True(r.NotChecked[^1].OnChart);
        Assert.DoesNotContain(r.Alerts, a => a.ItemId == free.Id || a.ItemId == uncoded.Id);
        Assert.Single(r.Alerts); // the coded amoxicillin only
    }

    [Fact]
    public void AChartMedicineIsMatchedToAGenericOnlyOnAWholeWord()
    {
        string[] generics = ["Metformin", "Aspirin", "Atorvastatin"];
        Assert.Equal("Metformin", SafetyRules.MatchGeneric(" metformin 500 mg ", generics));
        Assert.Equal("Aspirin", SafetyRules.MatchGeneric("Aspirin", generics));
        Assert.Null(SafetyRules.MatchGeneric("Metformina", generics));
        Assert.Null(SafetyRules.MatchGeneric("Low-dose aspirin", generics)); // not guessed from the middle of a name
    }
}
