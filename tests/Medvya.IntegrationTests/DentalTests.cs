using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// The dental pack (MDV-P8-010, MDV-P8-011, MDV-P8-012) on real PostgreSQL: findings are only ever added, in an open consultation, and the database refuses
/// to change one; the plan's items are priced from the catalogue, move only by the allowed steps and are never deleted; the patient's acceptance is
/// current until the plan changes; a done item is one suggested line on the visit's bill, priced on the day, never twice; another tenant sees nothing; roles
/// without the capability are refused; the routes exist only with the dental switch on; the PDF prints; the starter list loads once. Synthetic data only.
/// </summary>
public sealed class DentalTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly List<WebApplicationFactory<Program>> hosts = [];

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in hosts) await host.DisposeAsync();
        await h.CloseAsync();
    }

    IDental Dental(Guid? user = null) => h.Get<IDental>(user ?? h.Doctor);

    /// <summary>The patient checked in at <paramref name="hour"/> and the doctor's consultation started: the open encounter and its visit.</summary>
    async Task<(Guid Encounter, Guid Visit)> ConsultAsync(int hour, Guid? patient = null)
    {
        var visit = await h.BookAsync(hour, patient: patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        var encounter = await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)) ?? throw new InvalidOperationException("No encounter.");
        return (encounter.Id, visit.Id);
    }

    async Task<ServiceView> PricedAsync(string name, long amount)
    {
        var catalogue = h.Get<ICatalogue>(h.Admin);
        var service = await catalogue.CreateAsync(new SaveService { Name = name, Category = ServiceCategory.Procedure, Sac = "999312", DurationMinutes = 45 });
        await catalogue.AddPriceAsync(service.Id, new AddPrice { AmountMinor = amount });
        return service;
    }

    /// <summary>The history check confirmed on the visit (MDV-P8-014), with the questions answered yes; every other answer is no.</summary>
    Task<DentalHistoryCheckView?> CheckedAsync(Guid encounter, params DentalHistoryQuestion[] yes) =>
        Dental().ConfirmHistoryAsync(encounter, h.Facility, new(DentalHistoryRules.Questions.ToDictionary(q => q, yes.Contains), null));

    static async Task<string> Refused(Task task) => string.Join(",", (await Assert.ThrowsAsync<DentalValidationException>(() => task)).Errors.Select(e => $"{e.Field}:{e.Code}").Order());

    [Fact]
    public async Task FindingsAreOnlyAddedTheLatestIsTheToothsStateAndTheEarlierStaysInItsHistory()
    {
        var (encounter, _) = await ConsultAsync(10);
        await Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Caries, "od", "Deep, close to the pulp (synthetic)"));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await Dental().RecordFindingAsync(encounter, h.Facility, new("46", ToothCondition.Missing, null, null));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var correction = await Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Filling, "DO", null));

        var chart = await Dental().ChartAsync(h.Patient, h.Facility);
        Assert.Equal([("36", ToothCondition.Filling, "DO"), ("46", ToothCondition.Missing, (string?)null)], chart.Teeth.Select(t => (t.Tooth, t.Condition, t.Surfaces)));
        Assert.Equal([ToothCondition.Filling, ToothCondition.Missing, ToothCondition.Caries], chart.History.Select(f => f.Condition)); // newest first, nothing lost
        Assert.Equal(correction!.Id, chart.Teeth[0].Id);
        Assert.Equal(encounter, chart.OpenEncounterId);
        Assert.Equal(3, await RowsAsync());
    }

    /// <summary>SAFETY (mutation check: skip DentalRules.Validate or the ended-encounter check in RecordFindingAsync, and this fails).</summary>
    [Fact]
    public async Task AnInvalidFindingOrOneAfterTheVisitEndedIsRefusedAndNothingIsStored()
    {
        var (encounter, _) = await ConsultAsync(10);
        Assert.Equal("surfaces:required", await Refused(Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Caries, null, null))));
        Assert.Equal("condition:invalid,tooth:invalid", await Refused(Dental().RecordFindingAsync(encounter, h.Facility, new("19", null, null, null))));
        Assert.Equal("surfaces:not_allowed", await Refused(Dental().RecordFindingAsync(encounter, h.Facility, new("11", ToothCondition.Crown, "M", null))));
        Assert.Null(await Dental().RecordFindingAsync(encounter, h.OtherFacility, new("36", ToothCondition.Sound, null, null))); // not this facility's consultation

        await using (var db = sql.Encounters(h.Tenant))
            await db.Encounters.Where(e => e.Id == encounter).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EncounterStatus.Ended).SetProperty(e => e.EndedAt, h.Clock.GetUtcNow()));
        Assert.Equal("encounter:ended", await Refused(Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Sound, null, null))));
        Assert.Equal(0, await RowsAsync());
    }

    /// <summary>
    /// SAFETY (mutation check: drop the REVOKE UPDATE on encounters."DentalFindings" in infra/postgres/table-rights.sql, and this fails): the application's own
    /// database login cannot rewrite a recorded finding, even past the code.
    /// </summary>
    [Fact]
    public async Task TheDatabaseRefusesToChangeARecordedFinding()
    {
        var (encounter, _) = await ConsultAsync(10);
        await Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Caries, "O", null));
        await using var db = sql.Encounters(h.Tenant);
        var e = await Assert.ThrowsAnyAsync<PostgresException>(() => db.DentalFindings.ExecuteUpdateAsync(s => s.SetProperty(f => f.Condition, ToothCondition.Sound)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState);
        Assert.Equal(ToothCondition.Caries, (await db.DentalFindings.SingleAsync()).Condition);
    }

    async Task<int> RowsAsync()
    {
        await using var db = sql.Encounters(h.Tenant);
        return await db.DentalFindings.CountAsync();
    }

    /// <summary>
    /// MDV-P8-014: the history check is confirmed on an open visit with every question answered, audited, and the chart shows the latest with its date and the
    /// risks; a check is append-only (the database refuses an UPDATE), so a later visit confirms again with a new row.
    /// SAFETY (mutation check: accept a check with a question unanswered, or let the chart show an older check, and this fails).
    /// </summary>
    [Fact]
    public async Task TheHistoryCheckIsConfirmedOnTheVisitWithEveryAnswerAndTheLatestIsShown()
    {
        var (encounter, _) = await ConsultAsync(10);
        var partial = DentalHistoryRules.Questions.Where(q => q != DentalHistoryQuestion.Pregnancy).ToDictionary(q => q, _ => false);
        Assert.Equal("answers.Pregnancy:required", await Refused(Dental().ConfirmHistoryAsync(encounter, h.Facility, new(partial, null))));
        Assert.Null(await Dental().ConfirmHistoryAsync(encounter, h.OtherFacility, new(partial, null))); // not this facility's visit

        var first = (await CheckedAsync(encounter))!;
        Assert.Empty(first.Yes);
        var second = (await CheckedAsync(encounter, DentalHistoryQuestion.LocalAnaestheticAllergy, DentalHistoryQuestion.BloodThinners))!;
        Assert.Equal([DentalHistoryQuestion.BloodThinners, DentalHistoryQuestion.LocalAnaestheticAllergy], second.Yes); // in the order asked

        var chart = await Dental().ChartAsync(h.Patient, h.Facility);
        Assert.Equal((second.Id, encounter), (chart.HistoryCheck!.Id, chart.HistoryCheck.EncounterId));
        Assert.Equal(second.Yes, chart.HistoryCheck.Yes);
        await using (var audit = sql.Audit(h.Tenant))
            Assert.Equal(2, await audit.Events.CountAsync(e => e.Action == Medvya.Encounters.Infrastructure.DentalService.HistoryAction && e.PatientId == h.Patient));

        await using var db = sql.Encounters(h.Tenant);
        var e = await Assert.ThrowsAnyAsync<PostgresException>(() => db.DentalHistoryChecks.ExecuteUpdateAsync(x => x.SetProperty(c => c.Note, "changed")));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState); // append-only
    }

    /// <summary>SAFETY (mutation check: let MovePlanItemAsync skip DentalRules.CanMove, the decline reason or the encounter check, and this fails).</summary>
    [Fact]
    public async Task APlanItemIsPricedFromTheCatalogueAndMovesOnlyByTheAllowedSteps()
    {
        var (encounter, visit) = await ConsultAsync(10);
        var rct = await PricedAsync("Root canal, molar (synthetic)", 650_000);
        var unpriced = await h.Get<ICatalogue>(h.Admin).CreateAsync(new SaveService { Name = "Unpriced (synthetic)", Category = ServiceCategory.Procedure });
        Assert.Equal("serviceId:no_price", await Refused(Dental().AddPlanItemAsync(h.Patient, h.Facility, new(unpriced.Id, "36", null, 1))));
        Assert.Equal("phase:range", await Refused(Dental().AddPlanItemAsync(h.Patient, h.Facility, new(rct.Id, "36", null, 0))));

        var item = await Dental().AddPlanItemAsync(h.Patient, h.Facility, new(rct.Id, "36", null, 1));
        Assert.Equal((DentalPlanStatus.Planned, 650_000L, "INR", "Root canal, molar (synthetic)"), (item.Status, item.EstimateMinor, item.Currency, item.ServiceName));
        Assert.Equal("to:not_allowed", await Refused(Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Done, null, encounter, null))));
        Assert.Equal("reason:required", await Refused(Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Declined, " ", null, null))));
        Assert.Equal("encounterId:required", await Refused(Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.InProgress, null, null, null))));
        // MDV-P8-014: no treatment before the history is confirmed on this visit.
        Assert.Equal("historyCheck:required", await Refused(Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.InProgress, null, encounter, item.Version))));
        await CheckedAsync(encounter);

        var started = (await Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.InProgress, null, encounter, item.Version)))!;
        await Assert.ThrowsAsync<DentalConflictException>(() => Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Done, null, encounter, item.Version))); // stale
        var done = (await Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Done, null, encounter, started.Version)))!;
        Assert.Equal(DentalPlanStatus.Done, done.Status);
        Assert.Equal("to:not_allowed", await Refused(Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Declined, "Changed mind", encounter, null)))); // Done is final

        Assert.Equal([new DentalCharge(item.Id, rct.Id)], await h.Get<IDentalCharges>(h.Reception).DoneInVisitAsync(visit));
        Assert.Null(await Dental().MovePlanItemAsync(h.SecondPatient, h.Facility, item.Id, new(DentalPlanStatus.Planned, null, null, null))); // another patient's item: not found
    }

    [Fact]
    public async Task TheAcceptanceIsCurrentUntilThePlanChangesAndDeclinedItemsAreLeftOutOfTheTotal()
    {
        var crown = await PricedAsync("Crown, zirconia (synthetic)", 1_200_000);
        var scaling = await PricedAsync("Scaling (synthetic)", 150_000);
        Assert.Equal("plan:empty", await Refused(Dental().AcceptEstimateAsync(h.Patient, h.Facility, new("Farhan Qureshi (synthetic)", AcceptanceMethod.OnTablet))));
        await Dental().AddPlanItemAsync(h.Patient, h.Facility, new(crown.Id, "36", null, 2));
        var declined = await Dental().AddPlanItemAsync(h.Patient, h.Facility, new(scaling.Id, null, null, 2));
        await Dental().MovePlanItemAsync(h.Patient, h.Facility, declined.Id, new(DentalPlanStatus.Declined, "Patient: later, cost (synthetic)", null, null));
        Assert.Equal("acceptedBy:required,method:invalid", await Refused(Dental().AcceptEstimateAsync(h.Patient, h.Facility, new(" ", null))));

        var accepted = await Dental().AcceptEstimateAsync(h.Patient, h.Facility, new("Farhan Qureshi (synthetic)", AcceptanceMethod.OnTablet));
        Assert.Equal(1_200_000, accepted.TotalMinor);
        var plan = (await Dental().ChartAsync(h.Patient, h.Facility)).Plan;
        Assert.Equal((1_200_000L, true, "Patient: later, cost (synthetic)"), (plan.TotalMinor, plan.AcceptanceCurrent, plan.Items.Single(i => i.Id == declined.Id).Reason));

        await Dental().AddPlanItemAsync(h.Patient, h.Facility, new(scaling.Id, "11", null, 3));
        plan = (await Dental().ChartAsync(h.Patient, h.Facility)).Plan;
        Assert.Equal((1_350_000L, false), (plan.TotalMinor, plan.AcceptanceCurrent)); // changed since accepted
        Assert.Equal(accepted.Id, plan.Acceptance!.Id);
    }

    /// <summary>
    /// SAFETY (mutation check: drop the billed-items filter in Invoices.UnbilledDentalAsync, or the unique index on InvoiceLines (TenantId, InvoiceId, SourceId),
    /// and this fails): a done item is one suggested line on its visit's bill, however often the bill is opened.
    /// </summary>
    [Fact]
    public async Task ADoneItemIsOneSuggestedLineOnItsVisitsBillAndNeverTwice()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var (encounter, visit) = await ConsultAsync(10);
        await CheckedAsync(encounter);
        var rct = await PricedAsync("Root canal, stage 1 (synthetic)", 250_000);
        var xray = await PricedAsync("IOPA X-ray (synthetic)", 30_000);
        async Task DoneAsync(Guid service)
        {
            var item = await Dental().AddPlanItemAsync(h.Patient, h.Facility, new(service, "36", null, 1));
            var started = (await Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.InProgress, null, encounter, null)))!;
            await Dental().MovePlanItemAsync(h.Patient, h.Facility, item.Id, new(DentalPlanStatus.Done, null, encounter, started.Version));
        }
        await DoneAsync(xray.Id);
        var desk = h.Get<IInvoices>(h.Reception, s => s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(new BillingInvoiceTests.FakeLinks())));
        var draft = (await desk.ForVisitAsync(visit))!;
        var line = Assert.Single(draft.Lines, l => l.ServiceId == xray.Id);
        Assert.Equal((30_000L, true), (line.UnitMinor, line.Suggested));

        await DoneAsync(rct.Id); // done after the bill was opened: joins the draft
        var again = (await desk.ForVisitAsync(visit))!;
        Assert.Equal(draft.Id, again.Id);
        Assert.Equal(1, again.Lines.Count(l => l.ServiceId == xray.Id));
        Assert.Equal(1, again.Lines.Count(l => l.ServiceId == rct.Id));
        var third = (await desk.ForVisitAsync(visit))!;
        Assert.Equal(again.Lines.Count, third.Lines.Count); // opened once more: nothing added
        await using var db = sql.Billing(h.Tenant);
        Assert.Equal(2, await db.InvoiceLines.CountAsync(l => l.SourceId != null));
    }

    [Fact]
    public async Task AnotherTenantSeesNoDentalRowEvenWithTheFilterOff()
    {
        var (encounter, _) = await ConsultAsync(10);
        await Dental().RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Caries, "O", null));
        var item = await Dental().AddPlanItemAsync(h.Patient, h.Facility, new((await PricedAsync("Filling (synthetic)", 120_000)).Id, "36", "O", 1));
        await Dental().AcceptEstimateAsync(h.Patient, h.Facility, new("Synthetic", AcceptanceMethod.PrintedAndSigned));
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await other.DentalFindings.IgnoreQueryFilters().ToListAsync()); // row-level security on its own
        Assert.Empty(await other.DentalPlanItems.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await other.DentalEstimateAcceptances.IgnoreQueryFilters().ToListAsync());
        Assert.NotEqual(Guid.Empty, item.Id);
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check from any dental use case, or relax its capability to Full, and this fails).</summary>
    [Fact]
    public async Task OnlyADoctorWithACareRelationshipChangesTheChartAndANurseOnlyReadsIt()
    {
        var (encounter, _) = await ConsultAsync(10);
        await Dental(h.Nurse).ChartAsync(h.Patient, h.Facility); // Patient 360 at Limited: reads
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Dental(h.Nurse).RecordFindingAsync(encounter, h.Facility, new("36", ToothCondition.Caries, "O", null)));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Dental(h.Reception).ChartAsync(h.Patient, h.Facility));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Dental(h.Nurse).AcceptEstimateAsync(h.Patient, h.Facility, new("Synthetic", AcceptanceMethod.OnTablet)));
        Assert.Equal(0, await RowsAsync());
    }

    WebApplicationFactory<Program> Host()
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Features:InternalOrganisations:0", h.Tenant.ToString()) // dental is at the Internal release stage
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
        hosts.Add(host);
        return host;
    }

    HttpClient Client(WebApplicationFactory<Program> host, Guid user)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    /// <summary>SAFETY (mutation check: drop RequireFeature(Features.Dental) from a dental route, and this fails).</summary>
    [Fact]
    public async Task TheRoutesExistOnlyWithTheDentalSwitchOnAndThePdfPrints()
    {
        var (encounter, _) = await ConsultAsync(10);
        var host = Host();
        using var doctor = Client(host, h.Doctor);
        var chart = new Uri($"/v1/patients/{h.Patient}/dental", UriKind.Relative);
        Assert.Equal(HttpStatusCode.NotFound, (await doctor.GetAsync(chart)).StatusCode); // released to the organisation, not switched on yet

        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Dental, true, "practice", "Dental (synthetic test)");
        using (var r = await doctor.PostAsJsonAsync(new Uri($"/v1/encounters/{encounter}/dental/findings", UriKind.Relative), new { tooth = "36", condition = "Caries", surfaces = "OD" }))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await doctor.PostAsJsonAsync(new Uri($"/v1/encounters/{encounter}/dental/findings", UriKind.Relative), new { tooth = "36", condition = "Caries" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(["surfaces"], (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        }
        var body = await doctor.GetFromJsonAsync<JsonElement>(chart);
        Assert.Equal(("36", "Caries", "DO", "Dr. Arjun Rao (synthetic)"), (body.GetProperty("teeth")[0].GetProperty("tooth").GetString(), body.GetProperty("teeth")[0].GetProperty("condition").GetString(),
            body.GetProperty("teeth")[0].GetProperty("surfaces").GetString(), body.GetProperty("teeth")[0].GetProperty("author").GetString()));
        Assert.False(body.GetProperty("rulesSignedOff").GetBoolean());

        // Every condition on the drawn chart, a primary tooth among them: the PDF fonts have no "✓", so a healthy tooth must still print (its check is drawn).
        foreach (var (tooth, condition, surfaces) in new[] { ("11", "Sound", (string?)null), ("37", "Filling", "MO"), ("16", "Crown", null), ("26", "RootCanalTreated", null),
            ("46", "Missing", null), ("24", "Implant", null), ("14", "Bridge", null), ("31", "Mobile", null), ("21", "Fractured", null), ("55", "Sound", null) })
            using (var r = await doctor.PostAsJsonAsync(new Uri($"/v1/encounters/{encounter}/dental/findings", UriKind.Relative), new { tooth, condition, surfaces }))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await doctor.PostAsJsonAsync(new Uri($"/v1/encounters/{encounter}/dental/history-check", UriKind.Relative),
            new { answers = DentalHistoryRules.Questions.ToDictionary(q => q.ToString(), q => q == DentalHistoryQuestion.BloodThinners), note = "Aspirin 75 mg (synthetic)" }))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode); // printed as "Answered yes: Takes blood thinners…"
        using var pdf = await doctor.PostAsync(new Uri($"/v1/patients/{h.Patient}/dental/pdf", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..5]);

        using var nurse = Client(host, h.Nurse);
        using (var r = await nurse.PostAsJsonAsync(new Uri($"/v1/encounters/{encounter}/dental/findings", UriKind.Relative), new { tooth = "36", condition = "Sound" }))
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); // clinical notes at Read: no write route
    }

    [Fact]
    public async Task TheDentalStarterListLoadsOnceWithItsPricesAndTaxAndKeepsWhatTheClinicAlreadyHas()
    {
        await h.StepsAsync(nurse: false, billing: true);
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Dental, true, "practice", "Dental (synthetic test)");
        var catalogue = h.Get<ICatalogue>(h.Admin);
        await catalogue.CreateAsync(new SaveService { Name = "Our own scaling (synthetic)", Category = ServiceCategory.Procedure, Code = "DN-PRV001" });

        var first = await catalogue.LoadDentalStarterAsync();
        Assert.Equal((75, 1), (first.Added, first.Skipped)); // the clinic's own DN-PRV001 is kept as it is
        Assert.Equal((0, 76), ((await catalogue.LoadDentalStarterAsync()).Added, (await catalogue.LoadDentalStarterAsync()).Skipped));
        var services = await catalogue.ListAsync(includeArchived: false);
        var consult = services.Single(s => s.Code == "DN-DGN001");
        Assert.Equal(("Dental consultation", 30_000L, TaxTreatment.Exempt, "999312"), (consult.Name, consult.ClinicPriceMinor!.Value, consult.TaxTreatment, consult.Sac));
        Assert.All(services.Where(s => s.Code!.StartsWith("DN-COS", StringComparison.Ordinal)), s => Assert.Equal((TaxTreatment.Taxable, (int?)1800), (s.TaxTreatment, s.GstRateBasisPoints)));
        Assert.Equal("Our own scaling (synthetic)", services.Single(s => s.Code == "DN-PRV001").Name);

        var host = Host();
        using var admin = Client(host, h.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(new Uri("/v1/settings/services/dental-starter", UriKind.Relative), null)).StatusCode);
        using var desk = Client(host, h.Reception);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsync(new Uri("/v1/settings/services/dental-starter", UriKind.Relative), null)).StatusCode);
    }
}
