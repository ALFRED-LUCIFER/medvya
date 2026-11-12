using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-018 on real PostgreSQL: orders stay Draft until the doctor places them, placing is one audited call that nothing else makes, the
/// slip carries identifiers and instructions, orders show on the timeline in every state, and every call is behind clinical access.
/// </summary>
public sealed class OrderTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        // "Prescriptions and orders" is a signing capability: it stays off until the doctor's licence is verified.
        await using var db = sql.Context(h.Tenant);
        var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
        db.DoctorLicences.Add(new Medvya.Tenancy.Domain.DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = h.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IOrders Orders(Guid user) => h.Get<IOrders>(user);

    async Task<EncounterView> ConsultationAsync(Guid? patient = null, int hour = 10)
    {
        var visit = await h.BookAsync(hour, 0, patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        return (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!;
    }

    static AddOrder Lab(string name, string? note = null) => new(OrderKind.Lab, name, note);

    async Task<List<AuditEvent>> AuditAsync(string actionPrefix)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.ResourceType == "order" && e.Action.StartsWith(actionPrefix)).OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task OrdersStayDraftUntilPlacedThenAreOrderedWithTheLabAndAnAuditRow()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c", "fasting 10 h"));
        var drafts = (await Orders(h.Doctor).AddAsync(enc.Id, new AddOrder(OrderKind.Imaging, "Chest X-ray", null)))!;
        Assert.Equal([OrderState.Draft, OrderState.Draft], drafts.Select(o => o.State));
        Assert.All(drafts, o => Assert.Null(o.OrderedAt));

        var placed = (await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(" Metro Diagnostics ")))!;
        Assert.Equal([OrderState.Ordered, OrderState.Ordered], placed.Select(o => o.State));
        Assert.All(placed, o => Assert.Equal(("Metro Diagnostics", true), (o.Destination, o.OrderedAt is not null)));

        var row = Assert.Single(await AuditAsync("order.place"));
        Assert.Equal((AuditOutcome.Allowed, AuditPurpose.Treatment, (Guid?)h.Patient, (Guid?)h.Doctor, (Guid?)enc.Id), (row.Outcome, row.Purpose, row.PatientId, row.ActorUserId, row.ResourceId));
        Assert.DoesNotContain("HbA1c", row.Detail ?? ""); // codes and ids only, no test names or notes
    }

    [Fact]
    public async Task PlacingTwiceOrWithNothingToPlaceIsRefusedAndNothingIsRecordedAsDone()
    {
        var enc = await ConsultationAsync();
        var none = await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null)));
        Assert.Equal("nothing_to_place", none.Code);
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null));
        await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null)));
        Assert.Single(await AuditAsync("order.place"));
    }

    [Fact]
    public async Task ASwitchedOffTestIsNotPlacedAndAnOrderedOneCannotBeSwitched()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        var both = (await Orders(h.Doctor).AddAsync(enc.Id, Lab("Lipid profile")))!;
        var lipid = both.Single(o => o.Name == "Lipid profile");

        Assert.Equal(OrderState.NotIncluded, (await Orders(h.Doctor).IncludeAsync(lipid.Id, new IncludeOrder(false)))!.Single(o => o.Id == lipid.Id).State);
        Assert.Equal(OrderState.NotIncluded, (await Orders(h.Doctor).IncludeAsync(lipid.Id, new IncludeOrder(false)))!.Single(o => o.Id == lipid.Id).State); // again: where the doctor wants it
        var placed = (await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null)))!;
        Assert.Equal((OrderState.Ordered, OrderState.NotIncluded), (placed.Single(o => o.Name == "HbA1c").State, placed.Single(o => o.Name == "Lipid profile").State));

        var hba1c = placed.Single(o => o.Name == "HbA1c");
        Assert.Equal("not_draft", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).IncludeAsync(hba1c.Id, new IncludeOrder(false)))).Code);
        Assert.Equal("not_draft", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).IncludeAsync(hba1c.Id, new IncludeOrder(true)))).Code);
    }

    /// <summary>
    /// MDV-P2-043 AC2, the Plan's test toggles: on adds one draft (a second add is refused, on again re-includes the same row), off takes the draft out,
    /// and off never takes out an ordered test. SAFETY mutation check: drop "and o.State == from" in OrderService.IncludeAsync and the last part fails.
    /// </summary>
    [Fact]
    public async Task ATestToggledOnIsOneDraftAndToggledOffNeverTakesOutAnOrderedTest()
    {
        var enc = await ConsultationAsync();
        var hba1c = Assert.Single((await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c")))!);
        Assert.Equal("already_listed", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).AddAsync(enc.Id, Lab("hba1c")))).Code);
        Assert.Equal(OrderState.NotIncluded, Assert.Single((await Orders(h.Doctor).IncludeAsync(hba1c.Id, new IncludeOrder(false)))!).State);
        var on = Assert.Single((await Orders(h.Doctor).IncludeAsync(hba1c.Id, new IncludeOrder(true)))!);
        Assert.Equal((hba1c.Id, OrderState.Draft), (on.Id, on.State));

        await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null));
        Assert.Equal("not_draft", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).IncludeAsync(hba1c.Id, new IncludeOrder(false)))).Code);
        await using var db = sql.Prescribing(h.Tenant);
        Assert.Equal(OrderState.Ordered, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == hba1c.Id)).State);
    }

    [Fact]
    public async Task AnOrderSetAddsItsTestsAsDraftsOnceAndOrdersCanBeAddedByHand()
    {
        var set = await h.Get<IFormulary>(h.Admin).CreateOrderSetAsync(new CreateOrderSet("Diabetes review labs", ["HbA1c", "Fasting glucose", "Serum creatinine"]));
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("hba1c"));
        var drafts = (await Orders(h.Doctor).AddSetAsync(enc.Id, new AddOrderSetOrders(set.Id)))!;
        Assert.Equal(["Fasting glucose", "hba1c", "Serum creatinine"], drafts.Select(o => o.Name).Order()); // the hand-typed "hba1c" stays, the set's HbA1c is not added twice
        Assert.All(drafts, o => Assert.Equal(OrderState.Draft, o.State));
        Assert.Equal("already_listed", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).AddSetAsync(enc.Id, new AddOrderSetOrders(set.Id)))).Code);
        Assert.Null(await Orders(h.Doctor).AddSetAsync(enc.Id, new AddOrderSetOrders(Guid.NewGuid()))); // not one of this clinic's sets
        Assert.Equal("already_listed", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).AddAsync(enc.Id, Lab("FASTING GLUCOSE")))).Code);
    }

    [Fact]
    public async Task TheSlipHasThePatientsIdentifiersTheDoctorTheLabAndTheInstructions()
    {
        var enc = await ConsultationAsync();
        Assert.Null(await Orders(h.Doctor).SlipAsync(enc.Id)); // nothing placed yet
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c", "fasting 10 h"));
        var off = (await Orders(h.Doctor).AddAsync(enc.Id, Lab("Lipid profile")))!.Single(o => o.Name == "Lipid profile");
        await Orders(h.Doctor).IncludeAsync(off.Id, new IncludeOrder(false));
        await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders("Metro Diagnostics"));

        var slip = (await Orders(h.Doctor).SlipAsync(enc.Id))!;
        Assert.Equal(("Meera Iyer (synthetic)", "000001", new DateOnly(1972, 3, 12), "Female"), (slip.Patient.FullName, slip.Patient.Mrn, slip.Patient.DateOfBirth, slip.Patient.SexAtBirth));
        Assert.Equal(("Adyar (synthetic)", "Metro Diagnostics"), (slip.FacilityName, slip.Destination));
        var line = Assert.Single(slip.Orders); // the switched-off test is not on the slip
        Assert.Equal(("HbA1c", "fasting 10 h"), (line.Name, line.Note));
        Assert.Single(await AuditAsync("order.slip"));
    }

    [Fact]
    public async Task OrdersAreOnTheTimelineInEveryStateAndEndingTheConsultationPlacesNothing()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null));
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("Lipid profile"));
        var off = (await Orders(h.Doctor).AddAsync(enc.Id, Lab("Urine ACR")))!.Single(o => o.Name == "Urine ACR");
        await Orders(h.Doctor).IncludeAsync(off.Id, new IncludeOrder(false));
        await h.Get<IEncounters>(h.Doctor).EndAsync(enc.Id); // closing the consultation never places a draft

        var page = await h.Get<IPatientTimeline>(h.Doctor).PageAsync(h.Patient, h.Facility, new TimelineQuery(new HashSet<TimelineKind> { TimelineKind.Order }, null, null));
        Assert.Equal(["OrderDraft", "OrderNotIncluded", "OrderOrdered"], page.Entries.Select(e => e.Code).Order());
        Assert.Equal(["HbA1c", "Lipid profile", "Urine ACR"], page.Entries.Select(e => e.Title).Order());
        Assert.All(page.Entries, e => Assert.Equal(ChartSource.ClinicianVerified, e.Source));
        var all = await h.Get<IPatientTimeline>(h.Doctor).PageAsync(h.Patient, h.Facility, new TimelineQuery(new HashSet<TimelineKind>(), null, null));
        Assert.Equal(3, all.Entries.Count(e => e.Kind == TimelineKind.Order));
        var unverified = await h.Get<IPatientTimeline>(h.Doctor).PageAsync(h.Patient, h.Facility, new TimelineQuery(new HashSet<TimelineKind>(), ChartSource.Imported, null));
        Assert.DoesNotContain(unverified.Entries, e => e.Kind == TimelineKind.Order); // a source filter for imports never shows clinician orders
    }

    [Fact]
    public async Task AnEndedConsultationCannotBeChangedButItsOrdersAndSlipCanStillBeRead()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        var draft = (await Orders(h.Doctor).AddAsync(enc.Id, Lab("Lipid profile")))!.Single(o => o.Name == "Lipid profile");
        await Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null));
        await h.Get<IEncounters>(h.Doctor).EndAsync(enc.Id);

        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).AddAsync(enc.Id, Lab("Urine ACR")))).Code);
        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(null)))).Code);
        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<OrdersConflictException>(() => Orders(h.Doctor).IncludeAsync(draft.Id, new IncludeOrder(false)))).Code);
        Assert.Equal(2, (await Orders(h.Doctor).ListAsync(enc.Id))!.Count);
        Assert.NotNull(await Orders(h.Doctor).SlipAsync(enc.Id));
    }

    [Fact]
    public async Task InvalidInputIsRefusedWithCodes()
    {
        var enc = await ConsultationAsync();
        Assert.Equal([("kind", "required"), ("name", "required")], (await Assert.ThrowsAsync<OrdersValidationException>(() => Orders(h.Doctor).AddAsync(enc.Id, new AddOrder(null, " ", null)))).Errors.Order());
        Assert.Equal([("name", "too_long"), ("note", "too_long")], (await Assert.ThrowsAsync<OrdersValidationException>(() => Orders(h.Doctor).AddAsync(enc.Id, new AddOrder(OrderKind.Lab, new string('x', 201), new string('n', 201))))).Errors.Order());
        Assert.Equal([("destination", "too_long")], (await Assert.ThrowsAsync<OrdersValidationException>(() => Orders(h.Doctor).PlaceAsync(enc.Id, new PlaceOrders(new string('d', 101))))).Errors);
        Assert.Equal([("kind", "required")], (await Assert.ThrowsAsync<OrdersValidationException>(() => Orders(h.Doctor).AddAsync(enc.Id, new AddOrder((OrderKind)9, "X", null)))).Errors);
        Assert.Empty((await Orders(h.Doctor).ListAsync(enc.Id))!);
    }

    [Fact]
    public async Task OnlyTheClinicsOwnDoctorsReachOrdersAndAnotherClinicSeesNone()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Orders(h.Nurse).ListAsync(enc.Id)); // vitals entry is not ordering
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Orders(h.Reception).AddAsync(enc.Id, Lab("X")));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Orders(h.Admin).SlipAsync(enc.Id));

        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            Assert.Null(await other.Get<IOrders>(other.Doctor).ListAsync(enc.Id)); // not found, not forbidden: it does not exist for them
            await using var db = sql.Prescribing(other.Tenant);
            Assert.Equal(0, await db.Orders.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            await other.CloseAsync();
        }
    }

    [Fact]
    public async Task AMergeMovesOrdersToTheSurvivorAndAnUndoMovesThemBack()
    {
        var enc = await ConsultationAsync();
        await Orders(h.Doctor).AddAsync(enc.Id, Lab("HbA1c"));
        var participant = new OrderMergeParticipant(new PostgresFixture.FixedTenant(h.Tenant));

        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.MoveAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        await using (var db = sql.Prescribing(h.Tenant))
            Assert.Equal((h.SecondPatient, (Guid?)h.Patient), await db.Orders.Select(o => new ValueTuple<Guid, Guid?>(o.PatientId, o.MergedFromPatientId)).SingleAsync());
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.RestoreAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        await using (var db = sql.Prescribing(h.Tenant))
            Assert.Equal((h.Patient, (Guid?)null), await db.Orders.Select(o => new ValueTuple<Guid, Guid?>(o.PatientId, o.MergedFromPatientId)).SingleAsync());
    }
}
