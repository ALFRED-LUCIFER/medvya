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
/// MDV-P2-016 on real PostgreSQL: the safety checks read the chart's allergies and active medicines through Encounters' contract, alert in words
/// with the evidence, say what they could not check, record the rule set version with every check (audit) and override (row), and need a reason
/// for each severe alert before MDV-P2-017 may authorise. Every call is behind clinical access and the tenant. SAFETY mutation checks are named.
/// </summary>
public sealed class PrescriptionCheckTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
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

    async Task<Guid> AddAsync(Guid encounterId, string generic)
    {
        var id = (await h.Get<IFormulary>(h.Doctor).SearchAsync(generic, 1)).Single().Id;
        return (await Rx(h.Doctor).AddAsync(encounterId, new AddRxLine(id, null, null, null)))!.Single(l => l.FormularyItemId == id).Id!.Value;
    }

    static (AllergyKind, string?, AllergyStatus) Allergic(string substance, AllergyStatus status = AllergyStatus.Confirmed) => (AllergyKind.Substance, substance, status);

    static readonly (AllergyKind, string?, AllergyStatus) NoneKnown = (AllergyKind.NoKnownAllergies, null, AllergyStatus.Confirmed);

    async Task ChartAsync((AllergyKind Kind, string? Substance, AllergyStatus Status)[] allergies, params string[] medicines)
    {
        await using var db = sql.Encounters(h.Tenant);
        db.Allergies.AddRange(allergies.Select(a => new Allergy { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Kind = a.Kind, Substance = a.Substance, Status = a.Status, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() }));
        db.MedicationStatements.AddRange(medicines.Select(m => new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Name = m, State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() }));
        await db.SaveChangesAsync();
    }

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.ResourceType == "prescription" && e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    /// <summary>SAFETY (mutation check: pass no allergies from PrescribingChart, or drop the class match, and this fails).</summary>
    [Fact]
    public async Task APenicillinAllergyAlertsOnAmoxicillinInWordsWithEvidenceAndTheRuleSetIsRecorded()
    {
        await ChartAsync([Allergic("Penicillin"), Allergic("Cetirizine", AllergyStatus.NotAnAllergy)]);
        var enc = await ConsultationAsync();
        var amoxicillin = await AddAsync(enc.Id, "Amoxicillin");
        await AddAsync(enc.Id, "Cetirizine"); // ruled "not an allergy": no alert

        var check = (await Rx(h.Doctor).CheckAsync(enc.Id))!;
        var alert = Assert.Single(check.Alerts);
        Assert.Equal((RxAlertKind.Allergy, RxSeverity.Severe, "penicillins", "Penicillin", "Confirmed", "Amoxicillin", (Guid?)amoxicillin),
            (alert.Kind, alert.Severity, alert.Code, alert.Substance, alert.AllergyStatus, alert.Medicine, alert.ItemId));
        Assert.Equal((SafetyRules.Source, SafetyRules.Version, ChartAllergies.Listed, 1), (check.RuleSet, check.RuleSetVersion, check.Allergies, check.Unresolved));

        var row = Assert.Single(await AuditAsync("rx.check"));
        Assert.Equal((AuditOutcome.Allowed, AuditPurpose.Treatment, (Guid?)h.Patient), (row.Outcome, row.Purpose, row.PatientId));
        Assert.Equal($"rules:{SafetyRules.Version};severe:1;moderate:0;unresolved:1", row.Detail);
    }

    /// <summary>SAFETY (mutation check: count overrides in Unresolved without the key, accept a moderate or stale key, or skip the reason, and this fails).</summary>
    [Fact]
    public async Task ASevereAlertNeedsAReasonThatIsRecordedWithTheRuleSetAndAuditedAndANewDoseAsksAgain()
    {
        await ChartAsync([NoneKnown]);
        var enc = await ConsultationAsync();
        var paracetamol = await AddAsync(enc.Id, "Paracetamol");
        var omeprazole = await AddAsync(enc.Id, "Omeprazole");
        await AddAsync(enc.Id, "Pantoprazole"); // a duplicate: moderate, so no reason is asked
        await Rx(h.Doctor).UpdateAsync(paracetamol, new UpdateRxLine("3", "tablet", "oral", "QID", null, 3, 36, null)); // 6 g a day
        await Rx(h.Doctor).UpdateAsync(omeprazole, new UpdateRxLine("5", "capsule", "oral", "OD", null, 3, 15, null)); // 100 mg a day: a second severe alert
        var check = (await Rx(h.Doctor).CheckAsync(enc.Id))!;
        Assert.Equal(ChartAllergies.NoKnownAllergies, check.Allergies);
        var dose = Assert.Single(check.Alerts, a => a.Kind == RxAlertKind.DoseAboveMaximum && a.ItemId == paracetamol);
        Assert.Equal((6_000_000L, 4_000_000L, 2), (dose.DailyMicrograms!.Value, dose.MaxDailyMicrograms!.Value, check.Unresolved));

        Assert.Equal([("reason", "required")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert(dose.Key, " ")))).Errors);
        Assert.Equal([("key", "unknown")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert("dose:made-up", "Synthetic")))).Errors);
        var duplicate = Assert.Single(check.Alerts, a => a.Kind == RxAlertKind.Duplicate);
        Assert.Equal([("key", "unknown")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert(duplicate.Key, "Synthetic")))).Errors);

        var after = (await Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert(dose.Key, " Loading dose, reviewed (synthetic) ")))!;
        Assert.Equal(1, after.Unresolved); // the other severe alert still has none
        var given = Assert.Single(after.Overrides);
        Assert.Equal((dose.Key, "Loading dose, reviewed (synthetic)", SafetyRules.Version, h.Doctor), (given.Key, given.Reason, given.RuleSetVersion, given.OverriddenBy));
        await using (var db = sql.Prescribing(h.Tenant))
        {
            var stored = await db.RxAlertOverrides.SingleAsync();
            Assert.Equal((RxAlertKind.DoseAboveMaximum, SafetyRules.Version, h.Patient, enc.Id), (stored.Kind, stored.RuleSetVersion, stored.PatientId, stored.EncounterId));
        }
        var audited = Assert.Single(await AuditAsync("rx.override"));
        Assert.Equal($"kind:DoseAboveMaximum;rules:{SafetyRules.Version}", audited.Detail); // never the reason or the medicine
        Assert.Equal(h.Patient, audited.PatientId);

        // A higher dose is a new finding: the old reason does not cover it.
        await Rx(h.Doctor).UpdateAsync(paracetamol, new UpdateRxLine("4", "tablet", "oral", "QID", null, 3, 48, null));
        var again = (await Rx(h.Doctor).CheckAsync(enc.Id))!;
        Assert.Equal((2, 0), (again.Unresolved, again.Overrides.Count));
        Assert.Equal("encounter_ended", await EndedAsync(enc.Id, again.Alerts.First(a => a.Severity == RxSeverity.Severe).Key));
    }

    async Task<string> EndedAsync(Guid encounterId, string key)
    {
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounterId);
        return (await Assert.ThrowsAsync<PrescriptionConflictException>(() => Rx(h.Doctor).OverrideAsync(encounterId, new OverrideRxAlert(key, "Synthetic")))).Code;
    }

    /// <summary>SAFETY (mutation check: read no allergy rows as "no known allergies", and this fails).</summary>
    [Fact]
    public async Task NoAllergyRowsReadAsNotRecordedNeverAsNone()
    {
        var enc = await ConsultationAsync();
        await AddAsync(enc.Id, "Metformin");
        var check = (await Rx(h.Doctor).CheckAsync(enc.Id))!;
        Assert.Equal(ChartAllergies.NotRecorded, check.Allergies);
        Assert.Equal(RxAlertKind.AllergiesNotRecorded, Assert.Single(check.Alerts).Kind);
    }

    /// <summary>SAFETY (mutation check: leave out the chart medicines, or keep those stopped in this visit, and this fails).</summary>
    [Fact]
    public async Task AChartMedicineInteractsUnlessStoppedInThisVisitAndAnUnknownOneIsReportedNotGuessed()
    {
        await ChartAsync([NoneKnown], "Clopidogrel 75 mg", "Synthetic herbal tonic");
        var enc = await ConsultationAsync();
        var omeprazole = await AddAsync(enc.Id, "Omeprazole");
        var free = (await Rx(h.Doctor).AddAsync(enc.Id, new AddRxLine(null, "Synthetic drops", null, null)))!.Single(l => l.FreeText).Id!.Value;

        var check = (await Rx(h.Doctor).CheckAsync(enc.Id))!;
        var alert = Assert.Single(check.Alerts);
        Assert.Equal((RxAlertKind.Interaction, RxSeverity.Moderate, "reduced_antiplatelet", "Omeprazole", "Clopidogrel 75 mg", true, 0),
            (alert.Kind, alert.Severity, alert.Code, alert.Medicine, alert.Other, alert.OtherOnChart, check.Unresolved));
        Assert.Contains(check.NotChecked, n => n.Id == free && n.Reason == NotCheckedReason.FreeText);
        Assert.Contains(check.NotChecked, n => n is { OnChart: true, Medicine: "Synthetic herbal tonic", Reason: NotCheckedReason.NotOnFormulary });
        Assert.Contains(check.NotChecked, n => n.Id == omeprazole && n.Reason == NotCheckedReason.DoseNotChecked);

        await h.Get<IMedicationReconciliation>(h.Doctor).DecideAsync(enc.Id, new DecideMedicine("clopidogrel 75 mg", MedicationDecisionKind.Stop, null, "Synthetic"));
        Assert.Empty((await Rx(h.Doctor).CheckAsync(enc.Id))!.Alerts);
        await Rx(h.Doctor).IncludeAsync(omeprazole, new IncludeRxLine(false)); // a switched-off line is not prescribed, so not checked
        Assert.DoesNotContain((await Rx(h.Doctor).CheckAsync(enc.Id))!.NotChecked, n => n.Id == omeprazole);
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check from CheckAsync or OverrideAsync, or the tenant filter, and this fails).</summary>
    [Fact]
    public async Task OnlyTheClinicsOwnDoctorsRunOrOverrideTheChecksAndAnotherClinicSeesNone()
    {
        await ChartAsync([Allergic("Penicillin", AllergyStatus.PatientReported)]);
        var enc = await ConsultationAsync();
        await AddAsync(enc.Id, "Amoxicillin");
        var key = (await Rx(h.Doctor).CheckAsync(enc.Id))!.Alerts.Single().Key;
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Nurse).CheckAsync(enc.Id));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Reception).OverrideAsync(enc.Id, new OverrideRxAlert(key, "Synthetic")));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Rx(h.Admin).OverrideAsync(enc.Id, new OverrideRxAlert(key, "Synthetic")));
        await Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert(key, "Synthetic tolerance test"));

        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            Assert.Null(await other.Get<IPrescriptions>(other.Doctor).CheckAsync(enc.Id));
            Assert.Null(await other.Get<IPrescriptions>(other.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert(key, "Synthetic")));
            await using var db = sql.Prescribing(other.Tenant);
            Assert.Equal(0, await db.RxAlertOverrides.IgnoreQueryFilters().CountAsync()); // row-level security, with the EF filter off
        }
        finally
        {
            await other.CloseAsync();
        }
    }

    [Fact]
    public async Task AMergeMovesOverridesToTheSurvivorAndAnUndoMovesThemBack()
    {
        await ChartAsync([Allergic("Penicillin")]);
        var enc = await ConsultationAsync();
        await AddAsync(enc.Id, "Amoxicillin");
        await Rx(h.Doctor).OverrideAsync(enc.Id, new OverrideRxAlert((await Rx(h.Doctor).CheckAsync(enc.Id))!.Alerts.Single().Key, "Synthetic"));
        var participant = new OrderMergeParticipant(new PostgresFixture.FixedTenant(h.Tenant));
        async Task<Guid> OwnerAsync()
        {
            await using var db = sql.Prescribing(h.Tenant);
            return await db.RxAlertOverrides.Select(o => o.PatientId).SingleAsync();
        }

        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.MoveAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal(h.SecondPatient, await OwnerAsync());
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.RestoreAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        Assert.Equal(h.Patient, await OwnerAsync());
    }
}
