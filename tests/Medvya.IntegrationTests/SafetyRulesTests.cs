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

    // An adult man with a normal eGFR: none of the patient rules has anything to say, so the MDV-P2-016 tests read as before.
    static readonly PatientContext Adult = new(40, false, null, 90, null);

    static SafetyResult Run(IReadOnlyList<CheckedMedicine> lines, IReadOnlyList<CheckedMedicine>? chart = null, ChartAllergies state = ChartAllergies.Listed, params CheckedAllergy[] allergies) =>
        SafetyRules.Evaluate(lines, chart ?? [], state, allergies, Adult);

    static SafetyResult For(PatientContext patient, IReadOnlyList<CheckedMedicine> lines, IReadOnlyList<CheckedMedicine>? chart = null) =>
        SafetyRules.Evaluate(lines, chart ?? [], ChartAllergies.NoKnownAllergies, [], patient);

    static SigParts Sig(string dose, string unit, string? frequency) => new(dose, unit, "oral", frequency, null, null, null, null);

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
        // MDV-P2-039: once in the morning or at night is once a day, so the maximum still applies (9 x 500 mg = 4.5 g).
        foreach (var once in new[] { "MORNING", "NIGHT" })
            Assert.Equal(4_500_000L, Assert.Single(Run([Line("N02BE01", "Paracetamol", "500 mg", Tabs("9", once))]).Alerts).DailyMicrograms);

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

    // MDV-P2-040: the seeded patient set. Each SAFETY test names its mutation check.

    /// <summary>SAFETY (mutation check: drop the pregnancy rule, or move the line from 20 weeks, and this fails).</summary>
    [Fact]
    public void IbuprofenAt24WeeksIsSevereAndBefore20WeeksIsNot()
    {
        var ibuprofen = Line("M01AE01", "Ibuprofen", "400 mg", Sig("1", "tablet", "BD"));
        var alert = Assert.Single(For(new(31, true, null, 95, 24), [ibuprofen]).Alerts);
        Assert.Equal((RxAlertKind.Pregnancy, RxSeverity.Severe, "nsaid_from_20_weeks", (int?)24, (Guid?)ibuprofen.Id), (alert.Kind, alert.Severity, alert.Code, alert.Measure, alert.ItemId));
        Assert.Single(For(new(31, true, null, 95, 20), [ibuprofen]).Alerts);
        Assert.Empty(For(new(31, true, null, 95, 19), [ibuprofen]).Alerts);
        Assert.Empty(For(new(31, true, null, 95, 24), [Line("N02BE01", "Paracetamol", "500 mg", Sig("1", "tablet", "QID"))]).Alerts); // not an NSAID
    }

    /// <summary>SAFETY (mutation check: stop reporting an unrecorded pregnancy, and this fails). Unknown is never read as "not pregnant".</summary>
    [Fact]
    public void AnNsaidForAWomanOfChildbearingAgeWithNoPregnancyRecordedIsNotChecked()
    {
        var ibuprofen = Line("M01AE01", "Ibuprofen", "400 mg", Sig("1", "tablet", "BD"));
        Assert.Contains(For(new(31, true, null, 95, null), [ibuprofen]).NotChecked, n => n.Id == ibuprofen.Id && n.Reason == NotCheckedReason.PregnancyNotRecorded);
        Assert.DoesNotContain(For(new(31, false, null, 95, null), [ibuprofen]).NotChecked, n => n.Reason == NotCheckedReason.PregnancyNotRecorded);
        Assert.DoesNotContain(For(new(70, true, null, 95, null), [ibuprofen]).NotChecked, n => n.Reason == NotCheckedReason.PregnancyNotRecorded);
        // MDV-P2-034: a "not pregnant" statement for today is a recorded state (mutation check: ignore PregnancyRuledOut, and this fails).
        var ruledOut = For(new(31, true, null, 95, null, PregnancyRuledOut: true), [ibuprofen]);
        Assert.DoesNotContain(ruledOut.NotChecked, n => n.Reason == NotCheckedReason.PregnancyNotRecorded);
        Assert.DoesNotContain(ruledOut.Alerts, a => a.Kind == RxAlertKind.Pregnancy);
    }

    /// <summary>
    /// SAFETY (mutation check: drop the child dose rule, the concentration, or the 0.1 mL rounding, and this fails). Paracetamol 15 mg/kg at
    /// 14.2 kg is 213 mg, 8.9 mL of 120 mg/5 mL, at most 4 doses (852 mg) a day; 8.9 mL every 6 hours is within it, more is severe.
    /// </summary>
    [Fact]
    public void AChildsParacetamolIsWorkedOutByWeightInMgAndMlAndAboveItIsSevere()
    {
        var child = new PatientContext(4, false, 1420, null, null);
        CheckedMedicine Syrup(string ml, string frequency) => Line("N02BE01", "Paracetamol syrup", "120 mg/5 mL", Sig(ml, "ml", frequency));
        var line = Syrup("8.9", "QID");
        var r = For(child, [line]);
        var dose = Assert.Single(r.WeightDoses);
        Assert.Equal((line.Id, 1420, 15, 213_000L, (int?)89, 4, 852_000L), (dose.ItemId, dose.WeightHundredthsKg, dose.MgPerKg, dose.DoseMicrograms, dose.DoseTenthsMl, dose.MaxDosesPerDay, dose.MaxDailyMicrograms));
        Assert.Empty(r.Alerts);
        Assert.Empty(r.NotChecked);
        Assert.Empty(For(child, [Syrup("8.9", "SOS")]).NotChecked); // when needed: the dose is checked, the doses a day are on the label

        var big = Assert.Single(For(child, [Syrup("10", "QID")]).Alerts);
        Assert.Equal((RxAlertKind.ChildDose, RxSeverity.Severe, "per_dose"), (big.Kind, big.Severity, big.Code));
        var often = Assert.Single(For(child, [Line("M01AE01", "Ibuprofen syrup", "100 mg/5 mL", Sig("7.1", "ml", "QID"))]).Alerts); // 10 mg/kg, up to 3 a day
        Assert.Equal((RxAlertKind.ChildDose, "doses_a_day"), (often.Kind, often.Code));
        var tablet = For(child, [Line("N02BE01", "Paracetamol", "500 mg", Sig("1", "tablet", "TDS"))]);
        Assert.Equal((213_000L, (int?)null), (tablet.WeightDoses[0].DoseMicrograms, tablet.WeightDoses[0].DoseTenthsMl));
        Assert.Equal("per_dose", Assert.Single(tablet.Alerts).Code); // 500 mg is above 213 mg though within the adult maximum
    }

    /// <summary>SAFETY (mutation check: check a child's dose without a recent weight, or by the adult maximum alone, and this fails).</summary>
    [Fact]
    public void AChildsDoseWithoutAWeightFromTheLastMonthIsNotCheckedAndSaysSo()
    {
        var line = Line("N02BE01", "Paracetamol syrup", "120 mg/5 mL", Sig("20", "ml", "QID"));
        var r = For(new(4, false, null, null, null), [line]);
        Assert.Equal(NotCheckedReason.NoRecentWeight, Assert.Single(r.NotChecked).Reason);
        Assert.Empty(r.Alerts);
        Assert.Empty(r.WeightDoses);
        // A medicine with no rule by weight is not checked for a child, even within the adult maximum.
        Assert.Equal(NotCheckedReason.DoseNotChecked, Assert.Single(For(new(4, false, 1420, null, null), [Line("J01CA04", "Amoxicillin", "250 mg", Sig("1", "tablet", "TDS"))]).NotChecked).Reason);
    }

    /// <summary>
    /// SAFETY (mutation check: drop the NSAID eGFR rule, its RAAS step, or the Beers subset, and this fails). Diclofenac tablets at 76 with eGFR
    /// 52 and losartan on the chart: kidney severe (with the RAAS blocker), the existing interaction, and the older-adult finding.
    /// </summary>
    [Fact]
    public void DiclofenacTabletsWithEgfr52AndLosartanAt76AreSevereForTheKidney()
    {
        var diclofenac = Line("M01AB05", "Diclofenac", "50 mg", Sig("1", "tablet", "BD"));
        var older = new PatientContext(76, true, null, 52, null);
        var r = For(older, [diclofenac], [Chart("C09CA01", "Losartan 50 mg")]);
        var kidney = Assert.Single(r.Alerts, a => a.Kind == RxAlertKind.Kidney);
        Assert.Equal((RxSeverity.Severe, "nsaid_egfr_under_60_raas", (int?)52), (kidney.Severity, kidney.Code, kidney.Measure));
        Assert.Contains(r.Alerts, a => a is { Kind: RxAlertKind.Interaction, Code: "renal_function" });
        Assert.Contains(r.Alerts, a => a is { Kind: RxAlertKind.OlderAdult, Severity: RxSeverity.Moderate, Code: "systemic_nsaid" });

        Assert.Equal((RxSeverity.Moderate, "nsaid_egfr_under_60"), Kidney(For(older, [diclofenac])));
        Assert.Equal((RxSeverity.Severe, "nsaid_egfr_under_30"), Kidney(For(older with { Egfr = 29 }, [diclofenac])));
        Assert.DoesNotContain(For(older with { Egfr = 60 }, [diclofenac]).Alerts, a => a.Kind == RxAlertKind.Kidney);
        Assert.Empty(For(older, [Line("M02AA15", "Diclofenac gel 1 %")], [Chart("C09CA01")]).Alerts); // a gel is not systemic
    }

    /// <summary>SAFETY (mutation check: drop either metformin threshold, and this fails).</summary>
    [Fact]
    public void MetforminIsSevereUnderEgfr30AndModerateUnder45()
    {
        var metformin = Line("A10BA02", "Metformin", "500 mg", Sig("1", "tablet", "BD"));
        Assert.Equal((RxSeverity.Severe, "metformin_egfr_under_30"), Kidney(For(new(60, false, null, 29, null), [metformin])));
        Assert.Equal((RxSeverity.Moderate, "metformin_egfr_under_45"), Kidney(For(new(60, false, null, 44, null), [metformin])));
        Assert.Empty(For(new(60, false, null, 45, null), [metformin]).Alerts);
    }

    /// <summary>SAFETY (mutation check: read a missing eGFR as normal, and this fails).</summary>
    [Fact]
    public void AKidneyRuleWithoutAnEgfrIsNotChecked()
    {
        var metformin = Line("A10BA02", "Metformin", "500 mg", Sig("1", "tablet", "BD"));
        Assert.Equal(NotCheckedReason.NoEgfr, Assert.Single(For(new(60, false, null, null, null), [metformin]).NotChecked).Reason);
        Assert.Empty(For(new(60, false, null, null, null), [Line("C09CA01", "Losartan", "50 mg", Sig("1", "tablet", "OD"))]).NotChecked); // no kidney rule
    }

    /// <summary>SAFETY (mutation check: drop the benzodiazepine entry, or move the age line from 65, and this fails).</summary>
    [Fact]
    public void DiazepamAt76IsAModerateOlderAdultFinding()
    {
        var diazepam = Line("N05BA01", "Diazepam", "2 mg", Sig("1", "tablet", "OD"));
        var alert = Assert.Single(For(new(76, true, null, 52, null), [diazepam]).Alerts);
        Assert.Equal((RxAlertKind.OlderAdult, RxSeverity.Moderate, "benzodiazepine", (int?)76), (alert.Kind, alert.Severity, alert.Code, alert.Measure));
        Assert.Single(For(new(65, false, null, 90, null), [diazepam]).Alerts);
        Assert.Empty(For(new(64, false, null, 90, null), [diazepam]).Alerts);
    }

    static (RxSeverity, string?) Kidney(SafetyResult r) => Assert.Single(r.Alerts, a => a.Kind == RxAlertKind.Kidney) is var a ? (a.Severity, a.Code) : default;
}
