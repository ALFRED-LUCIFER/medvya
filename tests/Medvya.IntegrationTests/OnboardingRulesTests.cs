using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-004: the wizard's per-country checks (no database).</summary>
public class OnboardingRulesTests
{
    static OnboardingData Valid(string number = "84213", string authority = "TNMC", string? taxId = null, string mode = "Solo") => new(
        new("Arjun Rao (synthetic)", mode, "General medicine", null, null),
        new(authority, number, null),
        new("Rao Clinic (synthetic)", "12 Test Street, Chennai", null, taxId),
        new([new("09:00", "13:00"), new("17:00", "20:00")], [], 20, 100_000, 60_000),
        ["general-opd"],
        []);

    static string[] Fields(OnboardingRules rules, int step, OnboardingData data) => [.. rules.ValidateStep(step, data).Select(e => $"{e.Field}:{e.Code}")];

    [Fact]
    public void AValidIndiaDraftPassesEveryStep() => Assert.Empty(OnboardingRules.India.ValidateAll(Valid()));

    [Theory]
    [InlineData("TNMC", "84213", true)]
    [InlineData("NMC", "A-1234/5", true)]
    [InlineData("TNMC", "12", false)]      // too short
    [InlineData("TNMC", "84 213", false)]  // spaces are not part of a registration number
    [InlineData("DHA", "84213", false)]    // a UAE authority is unknown in India
    public void IndiaLicenceNeedsAKnownCouncilAndARegistrationNumber(string authority, string number, bool ok) =>
        Assert.Equal(ok, OnboardingRules.India.ValidateStep(1, Valid(number, authority)).Count == 0);

    [Theory]
    [InlineData("DHA", "GD-29918", true)]
    [InlineData("MOHAP", "29918", true)]
    [InlineData("DoH", "x", false)]
    [InlineData("TNMC", "GD-29918", false)] // an Indian council is unknown in the UAE
    public void UaeLicenceNeedsDohDhaOrMohapAndALicenceNumber(string authority, string number, bool ok) =>
        Assert.Equal(ok, OnboardingRules.Uae.ValidateStep(1, Valid(number, authority)).Count == 0);

    [Theory]
    [InlineData("33AAAAA0000A1Z5", true)]
    [InlineData("", true)]                 // optional
    [InlineData("33AAAAA0000A1Z", false)]  // 14 characters
    [InlineData("33aaaaa0000a1z5", false)] // lower case
    public void GstinIsOptionalButChecked(string gstin, bool ok) =>
        Assert.Equal(ok, OnboardingRules.India.ValidateStep(1, Valid(taxId: gstin)).Count == 0);

    [Theory]
    [InlineData("100123456700003", true)]
    [InlineData("", true)]
    [InlineData("10012345670000", false)]   // 14 digits
    [InlineData("1001234567000A3", false)]
    public void TrnIsFifteenDigits(string trn, bool ok) =>
        Assert.Equal(ok, OnboardingRules.Uae.ValidateStep(1, Valid("GD-29918", "DHA", taxId: trn)).Count == 0);

    [Fact]
    public void HoursNeedOrderedNonOverlappingWindowsAndAKnownSlot()
    {
        var bad = Valid() with { Hours = new([new("13:00", "09:00")], [new("09:00", "13:00"), new("12:00", "14:00")], 25, -1, null) };
        Assert.Equal(["hours.weekdays:range", "hours.saturday:overlap", "hours.slotMinutes:range", "hours.newVisitFee:range", "hours.followUpFee:required"],
            Fields(OnboardingRules.India, 2, bad));
    }

    [Fact]
    public void AnOwnerWhoIsNotADoctorGivesNoLicenceAndNeedsNoSpecialty()
    {
        var clinic = Valid(mode: "Clinic") with { Licence = null, Profile = new("K. Lakshmi (synthetic)", "Clinic", null, null, null) };
        Assert.Empty(OnboardingRules.India.ValidateAll(clinic));
        // The same answers as a doctor: the practice step asks for the licence and a specialty.
        Assert.Equal(["profile.specialties:required", "licence.authority:required", "licence.number:required"],
            Fields(OnboardingRules.India, 1, clinic with { Profile = clinic.Profile! with { Mode = "Solo" } }));
    }

    [Fact]
    public void SpecialtiesAreChosenFromTheListAndKeptAsOneLineThatFits()
    {
        var chips = Valid() with { Profile = new("Arjun Rao (synthetic)", "Solo", null, null, null, ["general-practice", "diabetes"]) };
        Assert.Empty(Fields(OnboardingRules.India, 1, chips));
        Assert.Equal("General practice, Diabetes", OnboardingRules.SpecialtyText(chips.Profile!));
        Assert.Equal(["profile.specialties:unknown"], Fields(OnboardingRules.India, 1, chips with { Profile = chips.Profile! with { Specialties = ["diabetes", "made-up"] } }));
        Assert.Equal(["profile.specialties:unknown"], Fields(OnboardingRules.India, 1, chips with { Profile = chips.Profile! with { Specialties = ["ent", "ent"] } }));
        var all = OnboardingRules.SpecialtyText(chips.Profile! with { Specialties = [.. OnboardingRules.Specialties.Select(s => s.Code)] })!;
        Assert.InRange(all.Length, 1, 100); // the column holds 100: names that do not fit are left out whole, never cut
        Assert.EndsWith("Orthopaedics, Physiotherapy", all, StringComparison.Ordinal);
        Assert.Equal("ENT", OnboardingRules.SpecialtyText(chips.Profile! with { Specialty = " ENT " })); // a draft typed before the chips
    }

    [Fact]
    public void OpenDaysUseTheUsualHoursAndSaturdayItsOwn()
    {
        HoursWindow[] usual = [new("09:00", "13:00"), new("17:00", "20:00")], saturday = [new("09:00", "14:00")];
        var hours = new OnboardingHours(usual, saturday, 20, 1, 1, [DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Saturday]);
        Assert.Equal([(DayOfWeek.Sunday, 2), (DayOfWeek.Tuesday, 2), (DayOfWeek.Saturday, 1)], OnboardingRules.OpenDays(hours).Select(d => (d.Day, d.Windows.Count)));
        Assert.Equal(2, OnboardingRules.OpenDays(hours with { Saturday = [] }).Last().Windows.Count); // Saturday without its own hours opens like the others
        // Drafts saved before the day switches: Monday to Friday, plus Saturday only when it has hours.
        Assert.Equal(6, OnboardingRules.OpenDays(hours with { Days = null }).Count());
        Assert.Equal(5, OnboardingRules.OpenDays(hours with { Days = null, Saturday = [] }).Count());

        Assert.Equal(["hours.days:required"], Fields(OnboardingRules.India, 2, Valid() with { Hours = hours with { Days = [] } }));
        Assert.Equal(["hours.days:unknown"], Fields(OnboardingRules.India, 2, Valid() with { Hours = hours with { Days = [DayOfWeek.Monday, DayOfWeek.Monday] } }));
        Assert.Equal(["hours.weekdays:required"], Fields(OnboardingRules.India, 2, Valid() with { Hours = hours with { Weekdays = [] } })); // Sunday and Tuesday need the usual hours
        Assert.Empty(Fields(OnboardingRules.India, 2, Valid() with { Hours = hours with { Weekdays = [], Days = [DayOfWeek.Saturday] } }));
    }

    [Theory]
    [InlineData(null, true)] // skipped
    [InlineData("Upload", true)]
    [InlineData("Move", true)]
    [InlineData("Fresh", true)]
    [InlineData("fresh", false)]
    public void ThePatientsChoiceIsOneOfTheThree(string? choice, bool ok) =>
        Assert.Equal(ok, OnboardingRules.India.ValidateStep(4, Valid() with { Patients = choice }).Count == 0);

    [Fact]
    public void TeamIsAtMostFiveDistinctReceptionistsOrNurses()
    {
        static OnboardingData team(params (string Email, Role Role)[] m) => Valid() with { Team = [.. m.Select(x => new OnboardingTeamMember(x.Email, x.Role))] };
        Assert.Empty(OnboardingRules.India.ValidateStep(3, team(("joseph@example.test", Role.Receptionist), ("nurse@example.test", Role.Nurse))));
        Assert.Equal(["team.0.email:format", "team.0.role:unknown"], Fields(OnboardingRules.India, 3, team(("not an email", Role.PracticeAdmin))));
        Assert.Equal(["team:duplicate"], Fields(OnboardingRules.India, 3, team(("a@example.test", Role.Nurse), ("A@example.test", Role.Nurse))));
        Assert.Equal(["team:range"], Fields(OnboardingRules.India, 3, Valid() with { Team = [.. Enumerable.Range(0, 6).Select(i => new OnboardingTeamMember($"s{i}@example.test", Role.Nurse))] }));
    }

    [Fact]
    public void UnknownTemplatesAreRefused() =>
        Assert.Equal(["templates:unknown"], Fields(OnboardingRules.India, 2, Valid() with { Templates = ["general-opd", "made-up"] }));

    [Fact]
    public void ErrorsNameTheFieldNeverTheValue()
    {
        var errors = OnboardingRules.India.ValidateStep(1, Valid("secret number 1", "TNMC"));
        Assert.DoesNotContain(errors, e => e.Field.Contains("secret", StringComparison.Ordinal) || e.Code.Contains("secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Capability.ClinicalNotes, true)]
    [InlineData(Capability.PrescriptionsOrders, true)]
    [InlineData(Capability.RecordingConsentCapture, true)]
    [InlineData(Capability.AiDraftsEvidence, true)]
    [InlineData(Capability.ResultReviewAcknowledge, true)]
    [InlineData(Capability.RegisterBookPatients, false)]
    [InlineData(Capability.CheckInQueue, false)]
    [InlineData(Capability.InvoicesPayments, false)]
    public void ARecordingOrSigningCapabilityTurnsOffWhileTheLicenceIsPendingAndBookingDoesNot(Capability capability, bool gated)
    {
        var full = RoleMatrix.Of(Role.SoloDoctor, capability);
        Assert.Equal(gated ? Access.None : full, LicenceRules.Apply(full, capability, licencePending: true));
        Assert.Equal(full, LicenceRules.Apply(full, capability, licencePending: false));
    }
}
