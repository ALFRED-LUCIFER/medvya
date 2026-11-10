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
/// MDV-P2-015 on real PostgreSQL: the dose builder's structured sig and its readable instruction per print language, discontinue lines from the
/// reconciliation's stop decisions, flagged free-text medicines, autosave with an audit row per change, nothing authorised, and every call behind
/// clinical access and the tenant. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class PrescriptionTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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

    IPrescriptions Rx(Guid user) => h.Get<IPrescriptions>(user);

    async Task<EncounterView> ConsultationAsync()
    {
        var visit = await h.BookAsync(10, 0);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        return (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!;
    }

    async Task<Guid> FormularyIdAsync(string generic) => (await h.Get<IFormulary>(h.Doctor).SearchAsync(generic, 1)).Single().Id;

    static UpdateRxLine Metformin => new("1", "tablet", "oral", "BD", "after_food", 5, 10, null);

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.ResourceType == "prescription" && e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task TheDoseBuilderGivesAStructuredSigAndAReadableInstructionInEachPrintLanguage()
    {
        var enc = await ConsultationAsync();
        var line = Assert.Single((await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(await FormularyIdAsync("Metformin"), null, null, null)))!);
        Assert.Equal(("Metformin", "500 mg", "Tablet", false, RxSource.AddedByYou), (line.Medicine, line.Strength, line.Form, line.FreeText, line.Source));
        Assert.All(line.Sig.Values, v => Assert.Equal("", v)); // nothing is filled in for the doctor, not even the formulary's default sig

        var built = Assert.Single((await Rx(h.Doctor).UpdateAsync(line.Id!.Value, Metformin with { Instructions = " Check sugar weekly " }))!);
        Assert.Equal(("1", "tablet", "oral", "BD", "after_food", 5, 10), (built.Dose, built.DoseUnit, built.Route, built.Frequency, built.Timing, built.DurationDays, built.Quantity));
        Assert.Equal("Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10. Check sugar weekly", built.Sig["en"]);
        Assert.Equal("1 गोली मुँह से दिन में दो बार खाने के बाद 5 दिन तक लें। मात्रा: 10। Check sugar weekly", built.Sig["hi"]);
        Assert.Equal("تناول 1 قرص عن طريق الفم مرتين يوميًا بعد الأكل لمدة 5 أيام. الكمية: 10. Check sugar weekly", built.Sig["ar"]);
        Assert.Equal("1 மாத்திரை வாய் வழியாக தினமும் இரண்டு முறை உணவுக்குப் பின் 5 நாட்களுக்கு எடுத்துக்கொள்ளவும். அளவு: 10. Check sugar weekly", built.Sig["ta"]);

        // Autosaved: a fresh read has it, and each change left one audit row with codes only.
        Assert.Equal(built, Assert.Single((await Rx(h.Doctor).ListAsync(enc.Id))!), new LineComparer());
        var row = Assert.Single(await AuditAsync("rx.update"));
        Assert.Equal((AuditOutcome.Allowed, AuditPurpose.Treatment, (Guid?)h.Patient, (Guid?)h.Doctor, (Guid?)line.Id), (row.Outcome, row.Purpose, row.PatientId, row.ActorUserId, row.ResourceId));
        Assert.Equal("formulary", Assert.Single(await AuditAsync("rx.add")).Detail);
        Assert.DoesNotContain("Metformin", row.Detail ?? "");
    }

    [Fact]
    public async Task ABlankFieldAddsNothingAndClearingAFieldIsSaved()
    {
        var enc = await ConsultationAsync();
        var id = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(await FormularyIdAsync("Metformin"), null, null, null)))!.Single().Id!.Value;
        await Rx(h.Doctor).UpdateAsync(id, Metformin);
        var cleared = (await Rx(h.Doctor).UpdateAsync(id, new UpdateRxLine("2", "tablet", " ", null, "with_dinner", null, null, null)))!.Single();
        Assert.Equal((null, null, null), (cleared.Route, cleared.DurationDays, cleared.Quantity));
        Assert.Equal("Take 2 tablets with dinner.", cleared.Sig["en"]);
        Assert.Equal("استخدم 1 مرة على الجلد.", Sig.Render(new SigParts("1", "application", "topical", null, null, null, null, null), "ar"));
    }

    [Fact]
    public async Task InvalidDoseBuilderFieldsAreRefusedWithCodesAndNothingIsSaved()
    {
        var enc = await ConsultationAsync();
        var id = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(await FormularyIdAsync("Metformin"), null, null, null)))!.Single().Id!.Value;
        var e = await Assert.ThrowsAsync<PrescriptionValidationException>(() =>
            Rx(h.Doctor).UpdateAsync(id, new UpdateRxLine("0", "spoonful", "by_ear", "hourly", "teatime", 0, 10_001, new string('x', 201))));
        Assert.Equal([("dose", "invalid"), ("doseUnit", "unknown"), ("durationDays", "out_of_range"), ("frequency", "unknown"), ("instructions", "too_long"), ("quantity", "out_of_range"), ("route", "unknown"), ("timing", "unknown")],
            e.Errors.Order());
        foreach (var dose in new[] { "-1", "1,5", "1.2345", "123456", "١", "1e3", "0.000" })
            Assert.Contains(("dose", "invalid"), (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).UpdateAsync(id, new UpdateRxLine(dose, null, null, null, null, null, null, null)))).Errors);
        Assert.Equal("0.5", (await Rx(h.Doctor).UpdateAsync(id, new UpdateRxLine("0.5", null, null, null, null, null, null, null)))!.Single().Dose);
        Assert.Single(await AuditAsync("rx.update")); // only the valid one
    }

    /// <summary>SAFETY (mutation check: drop the free-text flag in PrescriptionService.ViewsAsync and this fails).</summary>
    [Fact]
    public async Task AFreeTextMedicineIsFlaggedAndAFormularyIdMustBeThisCountrysMedicine()
    {
        var enc = await ConsultationAsync();
        var lines = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, " Herbal tonic X ", "10 ml", "Syrup")))!;
        var free = Assert.Single(lines);
        Assert.Equal(("Herbal tonic X", "10 ml", "Syrup", true, (Guid?)null), (free.Medicine, free.Strength, free.Form, free.FreeText, free.FormularyItemId));
        Assert.Equal("free_text", Assert.Single(await AuditAsync("rx.add")).Detail);

        Assert.Equal([("medicine", "required")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, " ", null, null)))).Errors);
        Assert.Equal([("form", "unknown"), ("medicine", "too_long")],
            (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, new string('m', 201), null, "Potion")))).Errors.Order());
        Assert.Equal([("formularyItemId", "unknown")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(Guid.NewGuid(), null, null, null)))).Errors);
        Guid uaeItem;
        await using (var db = sql.Prescribing(h.Tenant))
            uaeItem = await db.FormularyItems.Where(i => i.Country == "AE").Select(i => i.Id).FirstAsync();
        Assert.Equal([("formularyItemId", "unknown")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(uaeItem, null, null, null)))).Errors);
        Assert.Single((await Rx(h.Doctor).ListAsync(enc.Id))!);
    }

    /// <summary>SAFETY (mutation check: return no stops from ReconciliationStops, or ignore the latest decision, and this fails).</summary>
    [Fact]
    public async Task AStopDecisionInReconciliationIsADiscontinueLineAndFollowsTheDecision()
    {
        var enc = await ConsultationAsync();
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.MedicationStatements.AddRange(
                new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Name = "Atorvastatin", Dose = "20 mg", State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), RecordedBy = h.Doctor },
                new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Name = "Aspirin", Dose = "75 mg", State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), RecordedBy = h.Doctor });
            await db.SaveChangesAsync();
        }
        var rec = h.Get<IMedicationReconciliation>(h.Doctor);
        await rec.DecideAsync(enc.Id, new DecideMedicine("atorvastatin", MedicationDecisionKind.Stop, null, "Muscle pain"));
        await rec.DecideAsync(enc.Id, new DecideMedicine("aspirin", MedicationDecisionKind.Keep, null, null));
        await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(await FormularyIdAsync("Metformin"), null, null, null));

        var lines = (await Rx(h.Doctor).ListAsync(enc.Id))!;
        Assert.Equal([RxLineKind.Prescribe, RxLineKind.Discontinue], lines.Select(l => l.Kind));
        var stop = lines[1];
        Assert.Equal(("Atorvastatin", "Muscle pain", RxSource.ChartReconciliation, (Guid?)null), (stop.Medicine, stop.Reason, stop.Source, stop.Id));
        Assert.Equal(("Stop this medicine.", "यह दवा बंद करें।", "أوقف هذا الدواء."), (stop.Sig["en"], stop.Sig["hi"], stop.Sig["ar"]));

        // The latest decision counts: kept after all, the discontinue line goes; stopped again, it is back.
        await rec.DecideAsync(enc.Id, new DecideMedicine("atorvastatin", MedicationDecisionKind.Keep, null, null));
        Assert.DoesNotContain((await Rx(h.Doctor).ListAsync(enc.Id))!, l => l.Kind == RxLineKind.Discontinue);
        await rec.DecideAsync(enc.Id, new DecideMedicine("aspirin", MedicationDecisionKind.Stop, null, "Bleeding risk"));
        Assert.Equal(["Aspirin"], (await Rx(h.Doctor).ListAsync(enc.Id))!.Where(l => l.Kind == RxLineKind.Discontinue).Select(l => l.Medicine));
        Assert.Equal(3, (await AuditAsync("rx.read")).Count); // one row per read; the reconciliation lookup adds none
    }

    [Fact]
    public async Task ALineCanBeSwitchedOffAndAnEndedConsultationCanBeReadButNotChangedOrAuthorised()
    {
        var enc = await ConsultationAsync();
        var formulary = await FormularyIdAsync("Metformin");
        var first = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(formulary, null, null, null)))!.Single().Id!.Value;
        var cream = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, "Synthetic cream", null, "Cream")))!.Single(l => l.Medicine == "Synthetic cream").Id!.Value;
        var off = await Rx(h.Doctor).IncludeAsync(first, new IncludeRxLine(false));
        Assert.Equal([false, true], off!.Select(l => l.Included)); // kept on the record, not prescribed
        Assert.True((await Rx(h.Doctor).IncludeAsync(first, new IncludeRxLine(true)))![0].Included);
        Assert.Equal(["included:False", "included:True"], (await AuditAsync("rx.include")).Select(e => e.Detail));
        Assert.Equal([("included", "required")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).IncludeAsync(first, new IncludeRxLine(null)))).Errors);

        await h.Get<IEncounters>(h.Doctor).EndAsync(enc.Id); // ending the consultation authorises nothing: the lines stay drafts
        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<PrescriptionConflictException>(() => Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(formulary, null, null, null)))).Code);
        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<PrescriptionConflictException>(() => Rx(h.Doctor).UpdateAsync(cream, Metformin))).Code);
        Assert.Equal("encounter_ended", (await Assert.ThrowsAsync<PrescriptionConflictException>(() => Rx(h.Doctor).IncludeAsync(cream, new IncludeRxLine(false)))).Code);
        Assert.Equal(2, (await Rx(h.Doctor).ListAsync(enc.Id))!.Count);
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check in PrescriptionService.RequireAsync, or the tenant filter, and this fails).</summary>
    [Fact]
    public async Task OnlyTheClinicsOwnDoctorsReachThePrescriptionAndAnotherClinicSeesNone()
    {
        var enc = await ConsultationAsync();
        var id = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, "Synthetic syrup", null, null)))!.Single().Id!.Value;
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Nurse).ListAsync(enc.Id));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Reception).AddAsync(enc.Id, new AddRxLine(null, "X", null, null)));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Admin).UpdateAsync(id, Metformin));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Nurse).IncludeAsync(id, new IncludeRxLine(false)));

        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            Assert.Null(await other.Get<IPrescriptions>(other.Doctor).ListAsync(enc.Id)); // not found, not forbidden: it does not exist for them
            Assert.Null(await other.Get<IPrescriptions>(other.Doctor).UpdateAsync(id, Metformin));
            await using var db = sql.Prescribing(other.Tenant);
            Assert.Equal(0, await db.PrescriptionItems.IgnoreQueryFilters().CountAsync()); // row-level security, with the EF filter off
        }
        finally
        {
            await other.CloseAsync();
        }
    }

    [Fact]
    public async Task AMergeMovesPrescriptionLinesToTheSurvivorAndAnUndoMovesThemBack()
    {
        var enc = await ConsultationAsync();
        await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, "Synthetic syrup", null, null));
        var participant = new OrderMergeParticipant(new PostgresFixture.FixedTenant(h.Tenant));
        async Task<(Guid, Guid?)> OwnerAsync()
        {
            await using var db = sql.Prescribing(h.Tenant);
            return await db.PrescriptionItems.Select(i => new ValueTuple<Guid, Guid?>(i.PatientId, i.MergedFromPatientId)).SingleAsync();
        }

        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.MoveAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal((h.SecondPatient, (Guid?)h.Patient), await OwnerAsync());
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.RestoreAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal((h.Patient, (Guid?)null), await OwnerAsync());
    }

    /// <summary>Lines compared by value, the sig dictionary included.</summary>
    sealed class LineComparer : IEqualityComparer<RxLineView>
    {
        public bool Equals(RxLineView? x, RxLineView? y) => x is not null && y is not null && (x with { Sig = y.Sig }) == y && x.Sig.SequenceEqual(y.Sig);

        public int GetHashCode(RxLineView obj) => obj.Id.GetHashCode();
    }
}
