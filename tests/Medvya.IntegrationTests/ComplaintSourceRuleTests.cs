using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-104, pure rule: where the note's chief complaint came from after a save. SAFETY: the doctor's own text is never marked as the nurse's,
/// and text from intake that the doctor changed always says so (mutation-checked: each branch of ComplaintSourceRule.Next fails a case here).
/// </summary>
public sealed class ComplaintSourceRuleTests
{
    const string Nurse = "Fever and sore throat for three days (synthetic)";
    const ComplaintSource Intake = ComplaintSource.NurseIntake, Edited = ComplaintSource.EditedByDoctor;

    [Theory]
    [InlineData(null, null, Nurse, Nurse, Intake)]                  // a blank field filled with the nurse's words: from nurse intake
    [InlineData("  ", null, Nurse, Nurse, Intake)]
    [InlineData(Nurse, Intake, Nurse, Nurse, Intake)]               // saved again unchanged: still from intake
    [InlineData(Nurse, Intake, Nurse + ", worse at night", Nurse, Edited)] // the doctor changed it: edited by the doctor
    [InlineData(Nurse + ", worse", Edited, Nurse, Nurse, Edited)]   // changed back to the nurse's words: it stays edited
    [InlineData(Nurse, Intake, "Cough", "Something else now", Edited)] // the nurse corrected intake later: the doctor's change still counts
    [InlineData(null, null, "Cough (doctor)", Nurse, null)]         // the doctor's own words
    [InlineData("Cough (doctor)", null, Nurse, Nurse, null)]        // the doctor's own text replaced: still the doctor's (not a blank field)
    [InlineData(null, null, Nurse, null, null)]                     // no intake complaint: nothing is from intake
    [InlineData(Nurse, Intake, "", Nurse, null)]                    // cleared: no source
    [InlineData(Nurse, Edited, null, Nurse, null)]
    public void TheSourceFollowsTheText(string? previous, ComplaintSource? source, string? text, string? intake, ComplaintSource? expected) =>
        Assert.Equal(expected, ComplaintSourceRule.Next(previous, source, text, intake));

    [Fact]
    public void ASignedSourceChangesTheHashAndANoteWithoutOneHashesAsBefore()
    {
        var sections = new Dictionary<string, string> { ["chiefComplaint"] = Nurse };
        var at = new DateTimeOffset(2026, 11, 13, 4, 30, 0, TimeSpan.Zero);
        var by = Guid.Parse("0199a000-0000-7000-8000-000000000001");
        var plain = SignedNoteVersion.Hash("general-opd", 1, sections);
        Assert.Equal(plain, SignedNoteVersion.Hash("general-opd", 1, sections, complaintSource: null));
        Assert.NotEqual(plain, SignedNoteVersion.Hash("general-opd", 1, sections, complaintSource: (Intake, by, at)));
        Assert.NotEqual(SignedNoteVersion.Hash("general-opd", 1, sections, complaintSource: (Intake, by, at)), SignedNoteVersion.Hash("general-opd", 1, sections, complaintSource: (Edited, by, at)));
    }
}
