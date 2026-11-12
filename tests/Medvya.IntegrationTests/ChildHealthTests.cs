using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-036: the country schedule is a rule class, due and overdue are worked out from the date of birth on the facility's day (tested at
/// the band edges), a deferral needs a reason, nothing is overwritten or deleted, only a clinician with a care relationship reads and only a
/// doctor or nurse records, an adult has no child card, and the rows stay inside their tenant (EF filter and row-level security).
/// SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class ChildHealthTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    // Six weeks before the harness's Monday (19 Oct 2026): the 6-week band is due today, the birth band overdue.
    static readonly DateOnly SixWeeksOld = new(2026, 9, 7);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient); // the doctor's care relationship
        await BornAsync(h, h.Patient, SixWeeksOld);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    async Task BornAsync(SchedulingHarness at, Guid patient, DateOnly born)
    {
        await using var db = sql.Patients(at.Tenant);
        await db.Patients.Where(p => p.Id == patient).ExecuteUpdateAsync(s => s.SetProperty(p => p.DateOfBirth, born));
    }

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    IChildHealth Service(Guid user) => h.Get<IChildHealth>(user);

    static DoseView Dose(ChildHealthView v, string vaccine, int dose) => v.Bands.SelectMany(b => b.Doses).Single(d => d.Vaccine == vaccine && d.Dose == dose);

    static RecordDose Given(string vaccine, int dose, DateOnly on, string? batch = "B-2210 (synthetic)") => new(vaccine, dose, DoseOutcome.Given, on, batch, null, null);

    [Fact]
    public void ADoseIsUpcomingDueAndOverdueExactlyAtTheBandEdges()
    {
        var w6 = ImmunisationSchedule.India.Bands().Single(b => b.Key == "w6");
        var born = SixWeeksOld;
        Assert.Equal(DoseState.Upcoming, w6.StateOn(born, born.AddDays(41), given: false));
        Assert.Equal(DoseState.DueNow, w6.StateOn(born, born.AddDays(42), given: false)); // 6 weeks to the day
        Assert.Equal(DoseState.DueNow, w6.StateOn(born, born.AddDays(69), given: false));
        Assert.Equal(DoseState.Overdue, w6.StateOn(born, born.AddDays(70), given: false)); // 10 weeks: the next band starts
        Assert.Equal(DoseState.Given, w6.StateOn(born, born.AddDays(70), given: true));

        // Months are calendar months: a child born on 31 Jan 2024 reaches 9 months on 31 Oct and 1 month on 29 Feb.
        var m9 = ImmunisationSchedule.India.Bands().Single(b => b.Key == "m9");
        var leap = new DateOnly(2024, 1, 31);
        Assert.Equal(DoseState.Upcoming, m9.StateOn(leap, new DateOnly(2024, 10, 30), false));
        Assert.Equal(DoseState.DueNow, m9.StateOn(leap, new DateOnly(2024, 10, 31), false));
        Assert.Equal(DoseState.Overdue, m9.StateOn(leap, new DateOnly(2025, 1, 31), false));
        Assert.Equal(new DateOnly(2024, 2, 29), AgeAt.M(1).On(leap));

        // The facility's day: 00:15 in Chennai is still the day before in UTC.
        var justAfterMidnight = new DateTimeOffset(2026, 10, 18, 18, 45, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 19), ChildHealthService.DayIn(TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), justAfterMidnight));
        Assert.Equal(new DateOnly(2026, 10, 18), ChildHealthService.DayIn(TimeZoneInfo.Utc, justAfterMidnight));

        var y4 = DevelopmentalMilestones.Bands.Single();
        Assert.Null(DevelopmentalMilestones.BandOn(born, AgeAt.Y(4).On(born).AddDays(-1)));
        Assert.Equal(y4, DevelopmentalMilestones.BandOn(born, AgeAt.Y(4).On(born)));
        Assert.Null(DevelopmentalMilestones.BandOn(born, AgeAt.Y(5).On(born)));
    }

    [Fact]
    public void EachCountryHasItsOwnScheduleOfUniqueDosesAndNoneIsSignedOffYet()
    {
        Assert.Same(ImmunisationSchedule.India, ImmunisationSchedule.For("IN"));
        Assert.Same(ImmunisationSchedule.Uae, ImmunisationSchedule.For("AE"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImmunisationSchedule.For("US"));
        foreach (var s in new[] { ImmunisationSchedule.India, ImmunisationSchedule.Uae })
        {
            var doses = s.Bands().SelectMany(b => b.Doses).ToList();
            Assert.Equal(doses.Count, doses.Distinct().Count());
            Assert.All(s.Bands(), b => Assert.True(b.From.On(SixWeeksOld) < b.Until.On(SixWeeksOld), b.Key));
            Assert.Equal(s.Bands().Count, s.Bands().Select(b => b.Key).Distinct().Count());
        }
        Assert.Equal(("IAP 2023", "UAE NIP"), (ImmunisationSchedule.India.Name(), ImmunisationSchedule.Uae.Name()));
        Assert.Null(ImmunisationSchedule.SignOff);
        Assert.Null(DevelopmentalMilestones.SignOff);
    }

    [Fact]
    public async Task TheChartListsEachDoseFromTheDateOfBirthOnTheFacilitysDayAndVaccinesDueFeedTheCareGaps()
    {
        var view = await Service(h.Doctor).GetAsync(h.Patient, h.Facility);
        Assert.True(view.Child);
        Assert.Equal(("IAP 2023", false), (view.Schedule, view.SignedOff));
        Assert.Equal(DoseState.Overdue, Dose(view, "BCG", 1).State);
        Assert.Equal((DoseState.DueNow, SixWeeksOld.AddDays(42), SixWeeksOld.AddDays(70)), (Dose(view, "DTP", 1).State, Dose(view, "DTP", 1).DueFrom, Dose(view, "DTP", 1).OverdueFrom));
        Assert.Equal(DoseState.Upcoming, Dose(view, "DTP", 2).State);
        Assert.Null(view.Milestones); // no band for six weeks yet

        // 00:15 on Monday in Chennai (still Sunday in UTC): Monday's answer, the 6-week dose is due.
        h.Clock.Advance(new DateTimeOffset(2026, 10, 18, 18, 45, 0, TimeSpan.Zero) - h.Clock.GetUtcNow());
        Assert.Equal(DoseState.DueNow, Dose(await Service(h.Doctor).GetAsync(h.Patient, h.Facility), "DTP", 1).State);
        h.Clock.Advance(TimeSpan.FromMinutes(-30)); // 23:45 on Sunday in Chennai
        Assert.Equal(DoseState.Upcoming, Dose(await Service(h.Doctor).GetAsync(h.Patient, h.Facility), "DTP", 1).State);
        h.Clock.Advance(SchedulingHarness.MondayMorning - h.Clock.GetUtcNow());

        await Service(h.Nurse).RecordDoseAsync(h.Patient, h.Facility, Given("BCG", 1, SixWeeksOld));
        await Service(h.Nurse).RecordDoseAsync(h.Patient, h.Facility, new RecordDose("DTP", 1, DoseOutcome.Deferred, null, null, null, "Fever today (synthetic)"));
        var due = await Service(h.Doctor).VaccinesDueAsync(h.Patient, h.Facility);
        Assert.DoesNotContain(due, d => d.Vaccine == "BCG");
        Assert.Contains(new DueVaccine("OPV", 0, DoseState.Overdue, SixWeeksOld, false), due);
        Assert.Contains(new DueVaccine("DTP", 1, DoseState.DueNow, SixWeeksOld.AddDays(42), true), due); // deferred, still due
        Assert.DoesNotContain(due, d => d.State == DoseState.Upcoming);
        Assert.Equal(due.OrderBy(d => d.DueFrom).Select(d => d.DueFrom), due.Select(d => d.DueFrom));
    }

    /// <summary>SAFETY (a deferral needs a reason), mutation check: drop the "reason required" refusal in RecordDoseAsync and a bare deferral is stored.</summary>
    [Fact]
    public async Task ADeferralNeedsAReasonAndAGivenDoseNeedsAPlausibleDay()
    {
        var nurse = Service(h.Nurse);
        async Task<IReadOnlyList<(string, string)>> Refused(RecordDose c) => (await Assert.ThrowsAsync<ChildHealthValidationException>(() => nurse.RecordDoseAsync(h.Patient, h.Facility, c))).Errors;
        Assert.Contains(("reason", "required"), await Refused(new("DTP", 1, DoseOutcome.Deferred, null, null, null, "  ")));
        Assert.Contains(("reason", "too_long"), await Refused(new("DTP", 1, DoseOutcome.Deferred, null, null, null, new string('x', 501))));
        Assert.Contains(("givenOn", "not_for_deferred"), await Refused(new("DTP", 1, DoseOutcome.Deferred, SixWeeksOld, null, null, "Fever")));
        Assert.Contains(("givenOn", "required"), await Refused(Given("BCG", 1, default) with { GivenOn = null }));
        Assert.Contains(("givenOn", "future"), await Refused(Given("BCG", 1, SchedulingHarness.Monday.AddDays(1))));
        Assert.Contains(("givenOn", "before_birth"), await Refused(Given("BCG", 1, SixWeeksOld.AddDays(-1))));
        Assert.Contains(("dose", "unknown"), await Refused(Given("BCG", 2, SixWeeksOld)));
        Assert.Contains(("dose", "unknown"), await Refused(Given("Hexavalent", 1, SixWeeksOld))); // a UAE vaccine is not on India's schedule
        Assert.Contains(("outcome", "required"), await Refused(new("BCG", 1, null, SixWeeksOld, null, null, null)));
        Assert.Contains(("batch", "too_long"), await Refused(Given("BCG", 1, SixWeeksOld, new string('x', 51))));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Empty(await db.Immunisations.ToListAsync());
    }

    /// <summary>SAFETY (nothing is overwritten), mutation check: drop the "already_given" refusal and a second "given" replaces the first one's batch on the chart.</summary>
    [Fact]
    public async Task NothingIsOverwrittenOrDeletedAndADeferredDoseCanBeGivenLater()
    {
        var nurse = Service(h.Nurse);
        await nurse.RecordDoseAsync(h.Patient, h.Facility, new RecordDose("DTP", 1, DoseOutcome.Deferred, null, null, null, "Fever today (synthetic)"));
        var deferred = Dose(await Service(h.Doctor).GetAsync(h.Patient, h.Facility), "DTP", 1);
        Assert.Equal(("Fever today (synthetic)", (DateOnly?)SchedulingHarness.Monday, DoseState.DueNow), (deferred.DeferReason, deferred.DeferredOn, deferred.State));

        h.Clock.Advance(TimeSpan.FromDays(3));
        var view = await nurse.RecordDoseAsync(h.Patient, h.Facility, new RecordDose("DTP", 1, DoseOutcome.Given, SchedulingHarness.Monday.AddDays(3), " B-77 ", "Dr Kavya Rao (synthetic)", null));
        var given = Dose(view, "DTP", 1);
        Assert.Equal((DoseState.Given, "B-77", "Dr Kavya Rao (synthetic)", (string?)null), (given.State, given.Batch, given.By, given.DeferReason));
        Assert.Equal("already_given", (await Assert.ThrowsAsync<ChildHealthConflictException>(() =>
            nurse.RecordDoseAsync(h.Patient, h.Facility, Given("DTP", 1, SchedulingHarness.Monday)))).Code);
        Assert.Equal("already_given", (await Assert.ThrowsAsync<ChildHealthConflictException>(() =>
            nurse.RecordDoseAsync(h.Patient, h.Facility, new RecordDose("DTP", 1, DoseOutcome.Deferred, null, null, null, "Again")))).Code);

        await using var db = sql.Encounters(h.Tenant);
        var rows = await db.Immunisations.OrderBy(i => i.RecordedAt).ToListAsync();
        Assert.Equal([DoseOutcome.Deferred, DoseOutcome.Given], rows.Select(r => r.Outcome)); // the deferral is kept
        Assert.All(rows, r => Assert.Equal((h.Nurse, h.Facility), (r.RecordedBy, r.FacilityId)));
        // The database holds the shape too: a deferral without a reason cannot be written by any path.
        db.Immunisations.Add(new Immunisation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Vaccine = "IPV", Dose = 1, Outcome = DoseOutcome.Deferred, RecordedBy = h.Nurse, RecordedAt = h.Clock.GetUtcNow() });
        Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException);
    }

    [Fact]
    public async Task MilestonesOfTheChildsBandAreAnsweredWithWhoAnsweredAndTheLatestAnswerCounts()
    {
        var born = SchedulingHarness.Monday.AddYears(-4).AddMonths(-3);
        await BornAsync(h, h.Patient, born);
        var view = await Service(h.Doctor).GetAsync(h.Patient, h.Facility);
        Assert.Equal(("y4", 5, false), (view.Milestones!.Key, view.Milestones.Milestones.Count, view.Milestones.SignedOff));
        Assert.All(view.Milestones.Milestones, m => Assert.Null(m.Answer)); // unanswered is blank, never "no"

        await Service(h.Nurse).AnswerMilestoneAsync(h.Patient, h.Facility, new("y4", "dressesWithLittleHelp", MilestoneAnswer.Unsure, MilestoneAnswerer.Parent));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        view = await Service(h.Doctor).AnswerMilestoneAsync(h.Patient, h.Facility, new("y4", "dressesWithLittleHelp", MilestoneAnswer.Yes, MilestoneAnswerer.Clinician));
        var m = view.Milestones!.Milestones.Single(x => x.Key == "dressesWithLittleHelp");
        Assert.Equal((MilestoneAnswer.Yes, MilestoneAnswerer.Clinician, h.Clock.GetUtcNow()), (m.Answer, m.AnsweredBy, m.RecordedAt!.Value));

        var errors = (await Assert.ThrowsAsync<ChildHealthValidationException>(() =>
            Service(h.Doctor).AnswerMilestoneAsync(h.Patient, h.Facility, new("m18", "dressesWithLittleHelp", null, null)))).Errors;
        Assert.Equal([("milestone", "unknown"), ("answer", "required"), ("answeredBy", "required")], errors);
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(2, await db.MilestoneResponses.CountAsync()); // both answers kept
    }

    /// <summary>SAFETY (child only), mutation check: return true from Context.Child and an adult gets a schedule and accepts a dose.</summary>
    [Fact]
    public async Task AnAdultHasNoChildCardAndNothingCanBeRecordedForThem()
    {
        await BornAsync(h, h.Patient, SchedulingHarness.Monday.AddYears(-18));
        var view = await Service(h.Doctor).GetAsync(h.Patient, h.Facility);
        Assert.Equal((false, 0, (MilestoneBandView?)null), (view.Child, view.Bands.Count, view.Milestones));
        Assert.Empty(await Service(h.Doctor).VaccinesDueAsync(h.Patient, h.Facility));
        Assert.Equal("not_a_child", (await Assert.ThrowsAsync<ChildHealthConflictException>(() => Service(h.Nurse).RecordDoseAsync(h.Patient, h.Facility, Given("BCG", 1, SixWeeksOld)))).Code);
        await BornAsync(h, h.Patient, SchedulingHarness.Monday.AddYears(-18).AddDays(1)); // 18 tomorrow: still a child today
        Assert.True((await Service(h.Doctor).GetAsync(h.Patient, h.Facility)).Child);
    }

    [Fact]
    public async Task AUaeClinicReadsTheUaeSchedule()
    {
        var uae = new SchedulingHarness(sql) { Country = "AE" };
        await uae.SeedAsync();
        await uae.DoctorTemplateAsync();
        await uae.BookAsync(10, patient: uae.Patient);
        await BornAsync(uae, uae.Patient, SchedulingHarness.Monday.AddMonths(-4));
        try
        {
            var view = await uae.Get<IChildHealth>(uae.Doctor).GetAsync(uae.Patient, uae.Facility);
            Assert.Equal(("UAE NIP", false), (view.Schedule, view.SignedOff));
            Assert.Equal(["m2", "m4", "m6"], view.Bands.Select(b => b.Key));
            Assert.Equal((DoseState.Overdue, DoseState.DueNow), (Dose(view, "PCV", 1).State, Dose(view, "Hexavalent", 1).State));
            Assert.Contains(("dose", "unknown"), (await Assert.ThrowsAsync<ChildHealthValidationException>(() =>
                uae.Get<IChildHealth>(uae.Doctor).RecordDoseAsync(uae.Patient, uae.Facility, Given("BCG", 1, SchedulingHarness.Monday.AddMonths(-4))))).Errors);
        }
        finally
        {
            await uae.CloseAsync();
        }
    }

    /// <summary>SAFETY, mutation check: drop clinical.RequireAsync in RecordDoseAsync and a doctor without a care relationship (assigned only) records a dose.</summary>
    [Fact]
    public async Task OnlyAClinicianReadsOnlyADoctorOrNurseRecordsAndEveryCallIsAuditedWithoutTheVaccine()
    {
        foreach (var user in new[] { h.Reception, h.Admin })
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Service(user).GetAsync(h.Patient, h.Facility));
        foreach (var user in new[] { h.Reception, h.Admin, h.CareCoordinator })
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Service(user).RecordDoseAsync(h.Patient, h.Facility, Given("BCG", 1, SixWeeksOld)));
        foreach (var user in new[] { h.Reception, h.CareCoordinator })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).PostAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/immunisations", UriKind.Relative), Given("BCG", 1, SixWeeksOld))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).PostAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/milestones", UriKind.Relative), new { band = "y4" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.Reception).GetAsync(new Uri($"/v1/patients/{h.Patient}/child-health", UriKind.Relative))).StatusCode);
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Service(h.SecondDoctor).RecordDoseAsync(h.Patient, h.Facility, Given("BCG", 1, SixWeeksOld)));

        await Service(h.Doctor).RecordDoseAsync(h.Patient, h.Facility, Given("HepB", 1, SixWeeksOld, "SECRET-BATCH"));
        await using (var db = sql.Encounters(h.Tenant)) Assert.Single(await db.Immunisations.ToListAsync()); // the refused callers wrote nothing
        await using var audit = sql.Audit(h.Tenant);
        var events = await audit.Events.Where(e => e.Action == ChildHealthService.DoseAction || e.Action == ChildHealthService.ReadAction).ToListAsync();
        Assert.Contains(events, e => e.Action == ChildHealthService.DoseAction && e.Outcome == AuditOutcome.Allowed && e.PatientId == h.Patient && e.ActorUserId == h.Doctor);
        Assert.DoesNotContain(events, e => e.ToString()!.Contains("HepB", StringComparison.OrdinalIgnoreCase) || e.ToString()!.Contains("SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverHttpTheCardAnswersAndInputErrorsAreFieldCodes()
    {
        var doctor = Client(h.Doctor);
        var card = await doctor.GetFromJsonAsync<JsonElement>(new Uri($"/v1/patients/{h.Patient}/child-health", UriKind.Relative));
        Assert.True(card.GetProperty("child").GetBoolean());
        Assert.Equal("DueNow", card.GetProperty("bands")[1].GetProperty("doses")[0].GetProperty("state").GetString());

        var path = new Uri($"/v1/patients/{h.Patient}/immunisations", UriKind.Relative);
        using var bare = await Client(h.Nurse).PostAsJsonAsync(path, new { vaccine = "DTP", dose = 1, outcome = "Deferred" });
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Equal("required", (await bare.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("reason")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, (await Client(h.Nurse).PostAsJsonAsync(path, new { vaccine = "BCG", dose = 1, outcome = "Given", givenOn = "2026-09-07" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client(h.Nurse).PostAsJsonAsync(path, new { vaccine = "BCG", dose = 1, outcome = "Given", givenOn = "2026-09-08" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync(new Uri($"/v1/patients/{Guid.NewGuid()}/child-health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AnotherTenantSeesAndWritesNoneOfTheRowsEvenWithQueryFiltersOff()
    {
        await Service(h.Nurse).RecordDoseAsync(h.Patient, h.Facility, Given("BCG", 1, SixWeeksOld));
        await Service(h.Nurse).RecordDoseAsync(h.Patient, h.Facility, new RecordDose("DTP", 1, DoseOutcome.Deferred, null, null, null, "Fever"));
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.MilestoneResponses.Add(new MilestoneResponse { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Band = "y4", Milestone = "tellsAStory", Answer = MilestoneAnswer.Yes, AnsweredBy = MilestoneAnswerer.Parent, RecordedBy = h.Nurse, RecordedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var other = Guid.NewGuid();
        await using var foreign = sql.Encounters(other);
        Assert.Empty(await foreign.Immunisations.IgnoreQueryFilters().ToListAsync()); // row-level security alone
        Assert.Empty(await foreign.MilestoneResponses.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await foreign.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM encounters."Immunisations" """).ToListAsync());
        await using (var noTenant = sql.Encounters(null)) Assert.Empty(await noTenant.Immunisations.IgnoreQueryFilters().ToListAsync());
        foreign.Immunisations.Add(new Immunisation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Vaccine = "OPV", Dose = 0, Outcome = DoseOutcome.Given, GivenOn = SixWeeksOld, RecordedBy = h.Nurse, RecordedAt = h.Clock.GetUtcNow() });
        Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync())).InnerException);

        await using var mine = sql.Encounters(h.Tenant);
        Assert.Equal(2, await mine.Immunisations.CountAsync());
        Assert.Equal(1, await mine.MilestoneResponses.CountAsync());
    }
}
