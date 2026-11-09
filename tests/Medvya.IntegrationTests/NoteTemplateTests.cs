using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Xunit;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P2-025: six base templates, released versions that cannot be edited in place, and each doctor's own choice on real PostgreSQL.</summary>
public sealed class NoteTemplateTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public Task InitializeAsync() => h.SeedAsync();

    public Task DisposeAsync() => h.CloseAsync();

    [Fact]
    public void TheSixDesignedTemplatesExistWithTheRequiredSections()
    {
        Assert.Equal(["general-opd", "diabetes-review", "hypertension", "paediatrics", "antenatal", "teleconsult"], NoteTemplates.Keys);
        foreach (var t in NoteTemplates.History)
            Assert.Equal(["chiefComplaint", "assessment", "plan"], t.Sections.Where(s => s.Required).Select(s => s.Id));
    }

    // SAFETY mutation check: edit a section of a released version and this fails; the change must be a new version (existing notes keep theirs).
    [Fact]
    public void AReleasedVersionIsPinnedAndAnyChangeIsANewVersion()
    {
        var pinned = new Dictionary<string, string>
        {
            ["general-opd@1"] = "general-opd@1|chiefComplaint!,subjective,objective,assessment!,plan!|noRedFlags,reviewIfWorse,adviceFluidsRest",
            ["diabetes-review@1"] = "diabetes-review@1|chiefComplaint!,subjective,selfMonitoring,footAndEyeCare,objective,assessment!,plan!|adherenceDiscussed,dietAdvice,hba1cDue",
            ["hypertension@1"] = "hypertension@1|chiefComplaint!,subjective,homeBloodPressure,objective,assessment!,plan!|saltAdvice,adherenceDiscussed,reviewBpIn2Weeks",
            ["paediatrics@1"] = "paediatrics@1|chiefComplaint!,subjective,growthAndDevelopment,immunisations,objective,assessment!,plan!|feedingDiscussed,immunisationsUpToDate,guardianUnderstands",
            ["antenatal@1"] = "antenatal@1|chiefComplaint!,subjective,obstetricHistory,fetalWellbeing,objective,assessment!,plan!|fetalMovementsFelt,supplementsContinued,dangerSignsExplained",
            ["teleconsult@1"] = "teleconsult@1|chiefComplaint!,subjective,remoteContext,objective,assessment!,plan!|identityConfirmed,limitsOfRemoteExam,inPersonIfWorse",
        };
        foreach (var t in NoteTemplates.History.Where(t => t.Version == 1)) Assert.Equal(pinned[$"{t.Key}@1"], t.Fingerprint);
        foreach (var g in NoteTemplates.History.GroupBy(t => t.Key)) Assert.Equal(Enumerable.Range(1, g.Count()), g.Select(t => t.Version).Order());
        Assert.Equal(NoteTemplates.History.Count, NoteTemplates.History.Select(t => (t.Key, t.Version)).Distinct().Count());
        Assert.Equal(1, NoteTemplates.Find("antenatal", 1)!.Version);
        Assert.Null(NoteTemplates.Find("antenatal", 99));
    }

    [Fact]
    public async Task ADoctorWorksWithGeneralOpdUntilTheyChooseAndTheChoiceIsTheirOwn()
    {
        var mine = h.Get<INoteTemplates>(h.Doctor);
        Assert.Equal(["general-opd"], (await mine.CatalogAsync()).Mine);
        Assert.Equal(6, (await mine.CatalogAsync()).Templates.Count);

        Assert.Equal(["diabetes-review", "hypertension"], await mine.ChooseAsync(new(["hypertension", "diabetes-review", "hypertension"])));
        Assert.Equal(["hypertension"], await h.Get<INoteTemplates>(h.Doctor).ChooseAsync(new(["hypertension"])));
        Assert.Equal(["hypertension"], (await h.Get<INoteTemplates>(h.Doctor).CatalogAsync()).Mine);
        Assert.Equal(["general-opd"], (await h.Get<INoteTemplates>(h.SecondDoctor).CatalogAsync()).Mine);
    }

    [Fact]
    public async Task AnUnknownOrEmptyChoiceIsRefusedAndKeepsTheOldOne()
    {
        var mine = h.Get<INoteTemplates>(h.Doctor);
        await mine.ChooseAsync(new(["antenatal"]));
        foreach (var bad in new IReadOnlyList<string>?[] { null, [], ["nope"], ["antenatal", "nope"] })
            Assert.Null(await h.Get<INoteTemplates>(h.Doctor).ChooseAsync(new(bad)));
        Assert.Equal(["antenatal"], (await h.Get<INoteTemplates>(h.Doctor).CatalogAsync()).Mine);
    }
}
