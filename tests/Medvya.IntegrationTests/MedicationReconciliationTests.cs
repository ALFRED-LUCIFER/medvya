using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-011 on real PostgreSQL: the table lines up chart, patient-reported and other sources and flags disagreement; confirm is refused (service and
/// HTTP) until every row is decided; decisions keep actor, time, reason and history; confirm writes the chart (stopped with its date in Patient 360
/// and the timeline); only a doctor with a care relationship may reconcile. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class MedicationReconciliationTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateTimeOffset T0 = new(2026, 10, 1, 4, 30, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
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

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    IMedicationReconciliation Rec(Guid user) => h.Get<IMedicationReconciliation>(user);

    async Task<(Guid Visit, Guid Encounter)> InConsultAsync(int hour = 10)
    {
        var visit = await h.BookAsync(hour);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        var encounter = await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id));
        return (visit.Id, encounter!.Id);
    }

    MedicationStatement Statement(string name, string? dose, ChartSource source, MedicationState state = MedicationState.Active, Guid? visit = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Name = name, Dose = dose, State = state, Source = source, AppointmentId = visit,
        RecordedAt = T0, RecordedBy = h.Doctor,
    };

    async Task SeedAsync(params MedicationStatement[] rows)
    {
        await using var db = sql.Encounters(h.Tenant);
        db.MedicationStatements.AddRange(rows);
        await db.SaveChangesAsync();
    }

    /// <summary>Chart: atorvastatin 20 mg and metformin 500 mg active. Patient at this visit: atorvastatin stopped, metformin 1000 mg, aspirin only they report. Import: ibuprofen only.</summary>
    async Task SeedTableAsync(Guid visit) => await SeedAsync(
        Statement("Atorvastatin", "20 mg", ChartSource.ClinicianVerified), Statement("Metformin", "500 mg", ChartSource.ClinicianVerified),
        Statement("atorvastatin", null, ChartSource.PatientReported, MedicationState.Stopped, visit), Statement("Metformin", "1000 mg", ChartSource.PatientReported, visit: visit),
        Statement("Aspirin", "75 mg", ChartSource.PatientReported, visit: visit), Statement("Ibuprofen", "200 mg", ChartSource.Imported));

    static DecideMedicine D(string key, MedicationDecisionKind kind, string? dose = null, string? reason = null) => new(key, kind, dose, reason);

    static MedicineRow Row(ReconciliationView v, string key) => v.Rows.Single(r => r.Key == key);

    [Fact]
    public async Task TheTableLinesUpTheSourcesAndFlagsEveryDisagreementInWords()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        var view = (await Rec(h.Doctor).GetAsync(encounter))!;

        Assert.Equal((0, 4, 4), (view.Decided, view.Total, view.Disagree)); // atorvastatin, metformin, aspirin, ibuprofen
        var statin = Row(view, "atorvastatin");
        Assert.Equal(MedicineConflict.State, statin.Conflict); // "active in chart, patient says stopped"
        Assert.Equal((MedicationState.Active, MedicationState.Stopped), (statin.Chart!.State, statin.Patient!.State));
        Assert.Null(statin.Patient.Dose); // blank stays blank
        Assert.Equal(MedicineConflict.Dose, Row(view, "metformin").Conflict);
        Assert.Equal(MedicineConflict.NotOnChart, Row(view, "aspirin").Conflict);
        Assert.Equal(MedicineConflict.NotOnChart, Row(view, "ibuprofen").Conflict);
        Assert.Equal([MedicationDecisionKind.Keep, MedicationDecisionKind.Change, MedicationDecisionKind.Taper, MedicationDecisionKind.Stop], statin.Allowed);
        Assert.Equal([MedicationDecisionKind.Add, MedicationDecisionKind.Ignore], Row(view, "aspirin").Allowed); // patient-only items: Add or Ignore
        Assert.NotNull(Row(view, "ibuprofen").Other);
    }

    [Fact]
    public async Task AMedicineTheSourcesAgreeOnIsNotAConflict()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedAsync(Statement("Amlodipine", "5 mg", ChartSource.ClinicianVerified), Statement("amlodipine", "5 MG", ChartSource.PatientReported, visit: visit),
            Statement("Old drug", "1 mg", ChartSource.ClinicianVerified, MedicationState.Stopped));
        var view = (await Rec(h.Doctor).GetAsync(encounter))!;
        Assert.Null(Row(view, "amlodipine").Conflict);
        Assert.Single(view.Rows); // settled history (stopped on the chart, nobody says otherwise) is not today's question
    }

    [Fact]
    public async Task AnOldVisitsPatientReportedListDoesNotCountAtThisVisit()
    {
        var (_, encounter) = await InConsultAsync();
        await SeedAsync(Statement("Warfarin", "5 mg", ChartSource.PatientReported, visit: Guid.NewGuid()));
        Assert.Empty((await Rec(h.Doctor).GetAsync(encounter))!.Rows);
    }

    /// <summary>SAFETY, mutation check: drop the "undecided" refusal in ConfirmAsync and a half-decided table is applied to the chart.</summary>
    [Fact]
    public async Task ConfirmIsRefusedUntilEveryRowIsDecidedAndChangesNothing()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        var rec = Rec(h.Doctor);
        foreach (var key in new[] { "atorvastatin", "metformin", "aspirin" }) // ibuprofen is left undecided
            await rec.DecideAsync(encounter, D(key, key == "aspirin" ? MedicationDecisionKind.Ignore : MedicationDecisionKind.Stop, reason: "Synthetic reason"));

        Assert.Equal("undecided", (await Assert.ThrowsAsync<ReconciliationConflictException>(() => Rec(h.Doctor).ConfirmAsync(encounter))).Code);
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.MedicationReconciliations.CountAsync());
        Assert.All(await db.MedicationStatements.Where(m => m.Source == ChartSource.ClinicianVerified).ToListAsync(), m => Assert.Equal(MedicationState.Active, m.State));

        using var http = await Client(h.Doctor).PostAsync(new Uri($"/v1/encounters/{encounter}/medicines/reconciliation/confirm", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Conflict, http.StatusCode);
        Assert.Equal("undecided", (await http.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DecisionsKeepActorTimeReasonAndHistoryAndTheLatestWins()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        await Rec(h.Doctor).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Keep));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var view = (await Rec(h.Doctor).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Stop, reason: "Muscle pain (synthetic)")))!;

        Assert.Equal(MedicationDecisionKind.Stop, Row(view, "atorvastatin").Decision!.Kind);
        Assert.Equal(1, view.Decided);
        await using var db = sql.Encounters(h.Tenant);
        var rows = await db.MedicationDecisions.Where(d => d.MedicineKey == "atorvastatin").OrderBy(d => d.DecidedAt).ToListAsync();
        Assert.Equal([MedicationDecisionKind.Keep, MedicationDecisionKind.Stop], rows.Select(r => r.Kind));
        Assert.All(rows, r => Assert.Equal((h.Doctor, h.Patient, encounter), (r.DecidedBy, r.PatientId, r.EncounterId)));
        Assert.Equal(("Muscle pain (synthetic)", h.Clock.GetUtcNow()), (rows[1].Reason, rows[1].DecidedAt));
    }

    [Fact]
    public async Task ADecisionTheTableCannotTakeIsRefusedWithCodes()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        async Task<string> Refused(DecideMedicine d)
        {
            var e = await Assert.ThrowsAsync<ReconciliationValidationException>(() => Rec(h.Doctor).DecideAsync(encounter, d));
            return string.Join(",", e.Errors.Select(x => $"{x.Field}:{x.Code}"));
        }
        Assert.Equal("reason:required", await Refused(D("atorvastatin", MedicationDecisionKind.Stop)));
        Assert.Equal("reason:required,newDose:required", await Refused(D("metformin", MedicationDecisionKind.Change)));
        Assert.Equal("kind:not_allowed", await Refused(D("aspirin", MedicationDecisionKind.Keep))); // not on the chart: nothing to keep
        Assert.Equal("kind:not_allowed", await Refused(D("atorvastatin", MedicationDecisionKind.Add)));
        Assert.Equal("key:unknown", await Refused(D("not-a-medicine", MedicationDecisionKind.Keep)));
        Assert.Equal("kind:not_allowed", await Refused(new DecideMedicine("atorvastatin", null, null, null)));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.MedicationDecisions.CountAsync());
    }

    /// <summary>MDV-P2-043 AC1: change and taper keep the chart's old dose and the new one (a taper its steps and the day of the last dose), only in this tenant.</summary>
    [Fact]
    public async Task ChangeAndTaperAreRecordedWithTheOldAndNewDoseAndStayInTheTenant()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        var rec = Rec(h.Doctor);
        await rec.DecideAsync(encounter, D("metformin", MedicationDecisionKind.Change, " 1000 mg ", "HbA1c above target (synthetic)"));
        var view = (await rec.DecideAsync(encounter, new DecideMedicine("atorvastatin", MedicationDecisionKind.Taper, null, "Leg aches (synthetic)", [new(" 10 mg ", 7), new("5 mg", 7)])))!;

        var change = Row(view, "metformin").Decision!;
        Assert.Equal((MedicationDecisionKind.Change, "500 mg", "1000 mg"), (change.Kind, change.FromDose, change.NewDose));
        var taper = Row(view, "atorvastatin").Decision!;
        Assert.Equal((MedicationDecisionKind.Taper, "20 mg", (string?)null), (taper.Kind, taper.FromDose, taper.NewDose));
        Assert.Equal([new TaperStep("10 mg", 7), new TaperStep("5 mg", 7)], taper.Steps);
        // The decision day (Monday 19 Oct in India) is day one of 14: the last dose is on 01 Nov.
        Assert.Equal(new DateOnly(2026, 11, 1), taper.EndsOn);

        var changes = await h.Get<IReconciliationStops>(h.Doctor).ChangesAsync(encounter);
        Assert.Equal(["Atorvastatin", "Metformin"], changes.Select(c => c.Name));
        Assert.Empty(await h.Get<IReconciliationStops>(h.Doctor).StopsAsync(encounter)); // a taper is not a stop

        await rec.DecideAsync(encounter, D("aspirin", MedicationDecisionKind.Ignore));
        await rec.DecideAsync(encounter, D("ibuprofen", MedicationDecisionKind.Ignore));
        await rec.ConfirmAsync(encounter);
        await using var db = sql.Encounters(h.Tenant);
        var statin = await db.MedicationStatements.SingleAsync(m => m.Name == "Atorvastatin" && m.Source == ChartSource.ClinicianVerified);
        Assert.Equal(("10 mg", MedicationState.Active), (statin.Dose, statin.State)); // tapering from the first step; still taken until the end date

        // Tenant isolation: another clinic sees none of it, through the EF filter and row-level security alike.
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Equal(0, await other.MedicationDecisions.CountAsync());
        Assert.Equal(0, await other.MedicationDecisions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ATaperNeedsStepsWithADoseAndDaysAndAReason()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        async Task<string> Refused(IReadOnlyList<TaperStep>? steps, string? reason = "Synthetic")
        {
            var e = await Assert.ThrowsAsync<ReconciliationValidationException>(() => Rec(h.Doctor).DecideAsync(encounter, new("atorvastatin", MedicationDecisionKind.Taper, null, reason, steps)));
            return string.Join(",", e.Errors.Select(x => $"{x.Field}:{x.Code}"));
        }
        Assert.Equal("steps:required", await Refused(null));
        Assert.Equal("steps:required", await Refused([]));
        Assert.Equal("reason:required", await Refused([new("10 mg", 5)], null));
        Assert.Equal("steps:dose_required", await Refused([new("10 mg", 5), new(" ", 5)]));
        Assert.Equal("steps:days_out_of_range", await Refused([new("10 mg", 0)]));
        Assert.Equal("steps:days_out_of_range", await Refused([new("10 mg", 200), new("5 mg", 166)]));
        Assert.Equal("steps:too_many", await Refused([.. Enumerable.Range(1, 9).Select(i => new TaperStep($"{i} mg", 1))]));
        Assert.Equal(("kind", "not_allowed"), (await Assert.ThrowsAsync<ReconciliationValidationException>(() => // not on the chart: nothing to taper
            Rec(h.Doctor).DecideAsync(encounter, new("aspirin", MedicationDecisionKind.Taper, null, "Synthetic", [new("1 mg", 1)])))).Errors.Single());
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.MedicationDecisions.CountAsync());
    }

    [Fact]
    public async Task ConfirmAppliesEveryDecisionToTheChartAndStoppedShowsWithItsDate()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        var rec = Rec(h.Doctor);
        await rec.DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Stop, reason: "Muscle pain (synthetic)"));
        await rec.DecideAsync(encounter, D("metformin", MedicationDecisionKind.Change, "1000 mg", "Patient takes twice daily (synthetic)"));
        await rec.DecideAsync(encounter, D("aspirin", MedicationDecisionKind.Add));
        await rec.DecideAsync(encounter, D("ibuprofen", MedicationDecisionKind.Ignore));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var done = (await Rec(h.Doctor).ConfirmAsync(encounter))!;

        Assert.Equal((4, 4, h.Clock.GetUtcNow()), (done.Decided, done.Total, done.ConfirmedAt));
        await using var db = sql.Encounters(h.Tenant);
        var chart = await db.MedicationStatements.Where(m => m.Source == ChartSource.ClinicianVerified).ToListAsync();
        var statin = chart.Single(m => m.Name == "Atorvastatin");
        Assert.Equal((MedicationState.Stopped, h.Clock.GetUtcNow(), h.Doctor), (statin.State, statin.StoppedAt, statin.ReviewedBy));
        Assert.Equal("1000 mg", chart.Single(m => m.Name == "Metformin").Dose);
        var aspirin = chart.Single(m => m.Name == "Aspirin"); // added from what the patient said, as a clinician-verified chart row
        Assert.Equal(("75 mg", MedicationState.Active), (aspirin.Dose, aspirin.State));
        Assert.DoesNotContain(chart, m => m.Name == "Ibuprofen");
        Assert.NotNull(await db.MedicationReconciliations.SingleAsync(r => r.EncounterId == encounter && r.ConfirmedBy == h.Doctor));

        // Patient 360 and the timeline show it stopped, dated.
        var seen = (await h.Get<IPatientChart>(h.Doctor).GetAsync(h.Patient, h.Facility)).Medicines.Single(m => m.Name == "Atorvastatin");
        Assert.Equal((MedicationState.Stopped, h.Clock.GetUtcNow()), (seen.State, seen.StoppedAt));
        var line = (await h.Get<IPatientTimeline>(h.Doctor).PageAsync(h.Patient, h.Facility, new(new HashSet<TimelineKind> { TimelineKind.Medicine }, null, null, 50))).Entries
            .Single(e => e.Title == "Atorvastatin");
        Assert.Equal(("MedicineStopped", h.Clock.GetUtcNow()), (line.Code, line.At));

        // It is done: nothing more is decided or confirmed for this consultation, and a second confirm cannot double-apply.
        Assert.Equal("confirmed", (await Assert.ThrowsAsync<ReconciliationConflictException>(() => Rec(h.Doctor).ConfirmAsync(encounter))).Code);
        Assert.Equal("confirmed", (await Assert.ThrowsAsync<ReconciliationConflictException>(() => Rec(h.Doctor).DecideAsync(encounter, D("aspirin", MedicationDecisionKind.Ignore)))).Code);
        Assert.NotNull((await Rec(h.Doctor).GetAsync(encounter))!.ConfirmedAt);
    }

    [Fact]
    public async Task AddRestartsAStoppedChartMedicineWithTheDoseTheSourceSaysAndLeavesAnUnstatedDoseBlank()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedAsync(Statement("Ramipril", "5 mg", ChartSource.ClinicianVerified, MedicationState.Stopped), Statement("Ramipril", null, ChartSource.PatientReported, visit: visit));
        var row = Row((await Rec(h.Doctor).GetAsync(encounter))!, "ramipril");
        Assert.Equal(MedicineConflict.State, row.Conflict);
        Assert.Equal([MedicationDecisionKind.Add, MedicationDecisionKind.Ignore], row.Allowed);
        await Rec(h.Doctor).DecideAsync(encounter, D("ramipril", MedicationDecisionKind.Add));
        await Rec(h.Doctor).ConfirmAsync(encounter);
        await using var db = sql.Encounters(h.Tenant);
        var chart = await db.MedicationStatements.SingleAsync(m => m.Source == ChartSource.ClinicianVerified);
        Assert.Equal((MedicationState.Active, null, null), (chart.State, chart.Dose, chart.StoppedAt)); // never the old 5 mg guessed back
    }

    [Fact]
    public async Task AnEndedConsultationCannotBeReconciled()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
        Assert.Equal("ended", (await Assert.ThrowsAsync<ReconciliationConflictException>(() => Rec(h.Doctor).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Keep)))).Code);
        Assert.NotNull(await Rec(h.Doctor).GetAsync(encounter)); // still readable
    }

    [Fact]
    public async Task OnlyADoctorWithACareRelationshipMayReconcileAndEveryCallIsAudited()
    {
        var (visit, encounter) = await InConsultAsync();
        await SeedTableAsync(visit);
        foreach (var user in new[] { h.Reception, h.Nurse, h.Admin })
        {
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rec(user).GetAsync(encounter));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rec(user).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Keep)));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rec(user).ConfirmAsync(encounter));
        }
        foreach (var user in new[] { h.Reception, h.Nurse })
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).GetAsync(new Uri($"/v1/encounters/{encounter}/medicines/reconciliation", UriKind.Relative))).StatusCode);

        // Assigned only: Dr. Menon is not Meera's doctor.
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        Assert.True((await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rec(h.SecondDoctor).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Keep)))).CanRequest);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(h.Doctor).GetAsync(new Uri($"/v1/encounters/{Guid.NewGuid()}/medicines/reconciliation", UriKind.Relative))).StatusCode);

        await Rec(h.Doctor).GetAsync(encounter);
        await Rec(h.Doctor).DecideAsync(encounter, D("atorvastatin", MedicationDecisionKind.Stop, reason: "Reason text (synthetic)"));
        await using var audit = sql.Audit(h.Tenant);
        var events = await audit.Events.Where(e => e.Action.StartsWith("medication.reconcile")).ToListAsync();
        Assert.Contains(events, e => e.Action == "medication.reconcile.decide" && e.Outcome == AuditOutcome.Allowed && e.PatientId == h.Patient && e.ResourceId == encounter);
        Assert.Contains(events, e => e.Action == "medication.reconcile.read" && e.Outcome == AuditOutcome.Allowed);
        Assert.DoesNotContain(events, e => (e.ToString() + e.Action).Contains("atorvastatin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RulesTellDoseStateAndChartDisagreement()
    {
        static MedicineSide S(string? dose, MedicationState state = MedicationState.Active) => new(dose, null, state, false);
        Assert.Equal(MedicineConflict.NotOnChart, MedicationReconciliationRules.Conflict(null, S("1 mg"), null));
        Assert.Null(MedicationReconciliationRules.Conflict(S("5 mg"), S("5 MG"), null));
        Assert.Null(MedicationReconciliationRules.Conflict(S("5 mg"), S(null), null)); // an unknown dose is not a different dose
        Assert.Equal(MedicineConflict.Dose, MedicationReconciliationRules.Conflict(S("5 mg"), S("10 mg"), null));
        Assert.Equal(MedicineConflict.Dose, MedicationReconciliationRules.Conflict(S("5 mg"), S("5 mg"), S("10 mg")));
        Assert.Equal(MedicineConflict.State, MedicationReconciliationRules.Conflict(S("5 mg"), S("5 mg", MedicationState.Stopped), null));
    }
}
