using System.Collections.Concurrent;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-007 on real PostgreSQL: impossible vitals refused, out-of-range ones flagged and decided before handoff, the handoff not
/// skippable through the board's generic move, escalation with a reason, readings stored as nurse-verified observations, clinical access,
/// audit, and row-level security. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class IntakeTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Pushes pushes = new();

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(IntakeFlag.Key, true, "clinical-team", "Nurse intake (synthetic test)");
    }

    public Task DisposeAsync() => h.CloseAsync();

    sealed class Pushes : IBoardNotifier
    {
        public ConcurrentQueue<BoardChange> Changes { get; } = new();
        public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default)
        {
            Changes.Enqueue(change);
            return Task.CompletedTask;
        }
    }

    static readonly string?[] Blanks = [null, "", "   "];

    IIntake Intake(Guid user) => h.Get<IIntake>(user, s => s.AddSingleton<IBoardNotifier>(pushes));

    /// <summary>A visit checked in and sent to the nurse by reception.</summary>
    async Task<Guid> WithNurseAsync(Guid? patient = null, int hour = 10, int minute = 0)
    {
        var visit = await h.BookAsync(hour, minute, patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        await h.FrontDesk(h.Reception).MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.WithNurse });
        return visit.Id;
    }

    async Task<AppointmentState> StateAsync(Guid id)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id)).State;
    }

    static RecordIntake Vitals(string? complaint = null, decimal? sys = null, decimal? dia = null, decimal? pulse = null, decimal? temp = null, decimal? spo2 = null, decimal? weight = null) =>
        new(complaint, [new(VitalKind.SystolicBp, sys), new(VitalKind.DiastolicBp, dia), new(VitalKind.Pulse, pulse), new(VitalKind.Temperature, temp),
            new(VitalKind.OxygenSaturation, spo2), new(VitalKind.Weight, weight)]);

    EncountersDbContext EncountersDb(Guid tenant)
    {
        var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(tenant));
        return db;
    }

    [Theory]
    [InlineData(VitalKind.OxygenSaturation, 130)]
    [InlineData(VitalKind.SystolicBp, 400)]
    [InlineData(VitalKind.Pulse, 0)]
    [InlineData(VitalKind.Temperature, 98.6)] // Fahrenheit typed into a Celsius box
    [InlineData(VitalKind.Weight, -5)]
    [InlineData(VitalKind.Height, 1750)] // millimetres typed into a centimetre box
    [InlineData(VitalKind.RespiratoryRate, 0)]
    public async Task AnImpossibleValueIsRefusedAndNothingIsSaved(VitalKind kind, double value)
    {
        var visit = await WithNurseAsync();
        var refused = await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).RecordAsync(visit, new RecordIntake(null, [new(kind, (decimal)value)])));
        Assert.Contains(refused.Errors, e => e.Code == "implausible");
        Assert.Empty((await Intake(h.Nurse).GetAsync(visit))!.Vitals);
    }

    [Fact]
    public async Task HeightAndRespiratoryRateAreSavedAsChartObservations()
    {
        var visit = await WithNurseAsync();
        var saved = await Intake(h.Nurse).RecordAsync(visit, new RecordIntake(null, [new(VitalKind.Height, 162.5m), new(VitalKind.RespiratoryRate, 24)]));
        Assert.Equal([(VitalKind.Height, 162.5m, (VitalAlert?)null), (VitalKind.RespiratoryRate, 24m, VitalAlert.High)], saved!.Vitals.Select(v => (v.Kind, v.Value, v.Flag)));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal([(ObservationCode.Height, 16250), (ObservationCode.RespiratoryRate, 2400)],
            (await db.Observations.AsNoTracking().Where(o => o.AppointmentId == visit).OrderBy(o => o.Code).ToListAsync()).Select(o => (o.Code, o.ValueHundredths)));
    }

    [Fact]
    public async Task BloodPressureWhereTheTopNumberIsNotAboveTheBottomIsRefused()
    {
        var visit = await WithNurseAsync();
        var refused = await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 80, dia: 120)));
        Assert.Contains(refused.Errors, e => e.Code == "systolic_not_above_diastolic");
        await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 120.55m)));
    }

    [Fact]
    public async Task OutOfRangeValuesAreFlaggedAndBlankOnesStayBlank()
    {
        var visit = await WithNurseAsync();
        var saved = await Intake(h.Nurse).RecordAsync(visit, Vitals("Chest tightness on stairs", sys: 168, dia: 96, pulse: 72, spo2: 92));

        Assert.Equal(["chest tightness on stairs"], [saved!.ChiefComplaint!.ToLowerInvariant()]);
        Assert.Equal(
            [(VitalKind.SystolicBp, VitalAlert.High), (VitalKind.DiastolicBp, VitalAlert.High), (VitalKind.Pulse, null), (VitalKind.OxygenSaturation, VitalAlert.Low)],
            saved.Vitals.Select(v => (v.Kind, v.Flag)));
        Assert.DoesNotContain(saved.Vitals, v => v.Kind is VitalKind.Temperature or VitalKind.Weight); // not taken: blank, never guessed
        Assert.All(saved.Vitals.Where(v => v.Flag is not null), v => Assert.False(v.FlagKept));
        Assert.Contains("vitals_undecided", saved.Blockers);
    }

    /// <summary>
    /// SAFETY, mutation check (#956): flag every age with the adult band (make Usual ignore ageInMonths) and the child's normal pulse and
    /// blood pressure come back flagged, while the same numbers stay flagged for the adult.
    /// </summary>
    [Fact]
    public async Task AChildsVitalsAreFlaggedAgainstTheirAgeBandNotTheAdultOne()
    {
        await using (var patients = sql.Patients(h.Tenant))
        {
            var row = await patients.Patients.SingleAsync(p => p.Id == h.SecondPatient);
            row.DateOfBirth = DateOnly.FromDateTime(h.Clock.GetUtcNow().UtcDateTime).AddYears(-4); // a preschool child, synthetic
            await patients.SaveChangesAsync();
        }
        var childVisit = await WithNurseAsync(h.SecondPatient, hour: 9);
        var adultVisit = await WithNurseAsync(hour: 10);

        var child = await Intake(h.Nurse).RecordAsync(childVisit, Vitals(sys: 85, dia: 50, pulse: 115));
        var adult = await Intake(h.Nurse).RecordAsync(adultVisit, Vitals(sys: 85, dia: 50, pulse: 115));
        var tooFast = await Intake(h.Nurse).RecordAsync(childVisit, Vitals(pulse: 150));

        Assert.All(child!.Vitals, v => Assert.Null(v.Flag));
        Assert.Equal([VitalAlert.Low, VitalAlert.Low, VitalAlert.High], adult!.Vitals.Select(v => v.Flag));
        Assert.Equal(VitalAlert.High, tooFast!.Vitals.Single(v => v.Kind == VitalKind.Pulse).Flag);
    }

    [Theory]
    [InlineData(0, VitalKind.Pulse, 180, null)] // newborn
    [InlineData(0, VitalKind.SystolicBp, 55, VitalAlert.Low)]
    [InlineData(6, VitalKind.Pulse, 190, VitalAlert.High)] // infant
    [InlineData(6, VitalKind.Pulse, 150, null)]
    [InlineData(24, VitalKind.SystolicBp, 71, VitalAlert.Low)] // toddler: PALS hypotension line
    [InlineData(96, VitalKind.Pulse, 119, VitalAlert.High)] // school age
    [InlineData(180, VitalKind.SystolicBp, 132, VitalAlert.High)] // adolescent
    [InlineData(180, VitalKind.Pulse, 55, VitalAlert.Low)]
    [InlineData(216, VitalKind.Pulse, 55, null)] // 18: the adult band
    [InlineData(216, VitalKind.SystolicBp, 140, VitalAlert.High)]
    [InlineData(6, VitalKind.Temperature, 38.0, VitalAlert.High)] // fever and SpO2 lines are the same at every age
    [InlineData(600, VitalKind.OxygenSaturation, 93, VitalAlert.Low)]
    [InlineData(6, VitalKind.RespiratoryRate, 40, null)] // infant: PALS 30–53
    [InlineData(6, VitalKind.RespiratoryRate, 54, VitalAlert.High)]
    [InlineData(12, VitalKind.RespiratoryRate, 40, VitalAlert.High)] // toddler: 22–37
    [InlineData(48, VitalKind.RespiratoryRate, 19, VitalAlert.Low)] // preschool: 20–28
    [InlineData(155, VitalKind.RespiratoryRate, 25, null)] // school age: 18–25
    [InlineData(156, VitalKind.RespiratoryRate, 25, VitalAlert.High)] // 13: adolescent 12–20
    [InlineData(216, VitalKind.RespiratoryRate, 20, null)]
    [InlineData(216, VitalKind.RespiratoryRate, 21, VitalAlert.High)]
    [InlineData(216, VitalKind.RespiratoryRate, 11, VitalAlert.Low)]
    [InlineData(216, VitalKind.Height, 250, null)] // height is measured, never flagged
    public void UsualRangesFollowTheAgeBand(int ageInMonths, VitalKind kind, double value, VitalAlert? expected) =>
        Assert.Equal(expected, VitalRules.Flag(kind, (decimal)value, null, ageInMonths));

    [Theory]
    [InlineData("2022-11-11", "2026-11-11", 48)]
    [InlineData("2022-11-12", "2026-11-11", 47)] // the birthday is tomorrow
    [InlineData("2026-10-31", "2026-11-30", 0)]
    [InlineData("2026-11-12", "2026-11-11", 0)] // a date of birth after the reading (bad data) is not a negative age
    public void AgeInMonthsCountsWholeMonths(string born, string on, int months) =>
        Assert.Equal(months, VitalRules.AgeInMonths(DateOnly.Parse(born, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(on, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task AWeightThatMovedALotSincePatientsLastVisitIsFlaggedWithTheDifference()
    {
        var earlier = await WithNurseAsync(hour: 9);
        await Intake(h.Nurse).RecordAsync(earlier, Vitals(weight: 70.0m));
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        var later = await WithNurseAsync(hour: 10);
        var saved = await Intake(h.Nurse).RecordAsync(later, Vitals(weight: 72.4m, pulse: 70));

        var weight = saved!.Vitals.Single(v => v.Kind == VitalKind.Weight);
        Assert.Equal((VitalAlert.Changed, 70.0m, 72.4m), (weight.Flag, weight.PreviousValue, weight.Value));
        Assert.Null(saved.Vitals.Single(v => v.Kind == VitalKind.Pulse).PreviousValue); // first pulse for this patient
    }

    /// <summary>SAFETY, mutation check: remove the "vitals_undecided" blocker and the handoff goes through with the flagged value undecided.</summary>
    [Fact]
    public async Task AFlaggedValueNeedsAReMeasureOrAnExplicitKeepBeforeHandoff()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 168, dia: 96));

        var blocked = await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).HandOffAsync(visit));
        Assert.Equal("handoff_blocked", blocked.Code);
        Assert.Equal(["vitals_undecided"], blocked.Blockers);
        Assert.Equal(AppointmentState.WithNurse, await StateAsync(visit));

        // Re-measure: a changed value is a new reading; a normal one clears its flag, a still-high one still needs a decision.
        var again = await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 170, dia: 118));
        Assert.Equal(["vitals_undecided"], again!.Blockers);
        await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).HandOffAsync(visit));
        var normal = await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 128, dia: 82));
        Assert.Equal(((VitalAlert?)null, 128m), (normal!.Vitals[0].Flag, normal.Vitals[0].Value));
        Assert.Empty(normal.Blockers);

        // Keep: explicit, and only for a flagged reading; saving the same value again does not undo the decision.
        var visit2 = await WithNurseAsync(h.SecondPatient, 10, 20);
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit2); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).RecordAsync(visit2, Vitals(spo2: 91));
        await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).KeepAsync(visit2, VitalKind.Pulse)); // nothing flagged to keep
        var kept = await Intake(h.Nurse).KeepAsync(visit2, VitalKind.OxygenSaturation);
        Assert.Empty(kept!.Blockers);
        Assert.Empty((await Intake(h.Nurse).RecordAsync(visit2, Vitals(spo2: 91)))!.Blockers);

        var handed = await Intake(h.Nurse).HandOffAsync(visit2);
        Assert.Equal((true, "ReadyForDoctor"), (handed!.HandedOff, handed.State));
        Assert.Equal(AppointmentState.ReadyForDoctor, await StateAsync(visit2));
        Assert.Equal(AppointmentState.ReadyForDoctor, pushes.Changes.Last().State);
        // Repeating it changes nothing.
        var count = pushes.Changes.Count;
        Assert.True((await Intake(h.Nurse).HandOffAsync(visit2))!.HandedOff);
        Assert.Equal(count, pushes.Changes.Count);
    }

    /// <summary>SAFETY, mutation check: without IHandoffGate in the board's move, reception moves the card to Ready for doctor around the intake.</summary>
    [Fact]
    public async Task TheBoardsGenericMoveCannotSkipTheNursesHandoff()
    {
        var visit = await WithNurseAsync();
        Assert.Equal("intake_required", (await Assert.ThrowsAsync<VisitTransitionException>(() =>
            h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.ReadyForDoctor }))).Code);
        Assert.Equal(AppointmentState.WithNurse, await StateAsync(visit));

        await Intake(h.Nurse).RecordAsync(visit, Vitals(pulse: 70));
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).HandOffAsync(visit);
        Assert.Equal(AppointmentState.ReadyForDoctor, (await h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.ReadyForDoctor })).State); // a repeat

        // A clinic without the nurse step skips it: checked in goes straight to ready for the doctor (MDV audit UX-05).
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(IntakeFlag.Key, false, "clinical-team", "Nurse intake (synthetic test)");
        var other = await h.BookAsync(10, 20, h.SecondPatient);
        await h.FrontDesk(h.Reception).CheckInAsync(other.Id, new CheckIn());
        Assert.Equal(AppointmentState.ReadyForDoctor, (await h.FrontDesk(h.Reception).MoveAsync(other.Id, new MoveVisit { State = AppointmentState.ReadyForDoctor })).State);
    }

    [Fact]
    public async Task EscalatingNeedsAReasonAndReachesTheTreatingDoctor()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 210, dia: 130));
        foreach (var blank in Blanks)
            Assert.Contains(("reason", "required"), (await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).EscalateAsync(visit, new EscalateIntake(blank)))).Errors);
        Assert.Empty(await Intake(h.Doctor).EscalationsAsync(h.Facility));

        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        pushes.Changes.Clear();
        var escalated = await Intake(h.Nurse).EscalateAsync(visit, new EscalateIntake("Severe headache with BP 210/130 (synthetic)"));
        Assert.True(escalated!.Escalated);
        // MDV-P2-002 mutation check: drop the push in IntakeService.EscalateAsync and this fails. The push is ids and state, never the reason.
        var push = Assert.Single(pushes.Changes);
        Assert.Equal((visit, h.Facility, AppointmentState.WithNurse), (push.AppointmentId, push.FacilityId, push.State));
        Assert.Empty(await Intake(h.SecondDoctor).EscalationsAsync(h.Facility)); // not the treating doctor: nothing, even with the push
        var urgent = Assert.Single(await Intake(h.Doctor).EscalationsAsync(h.Facility));
        Assert.Equal((visit, h.Patient, "Severe headache with BP 210/130 (synthetic)"), (urgent.AppointmentId, urgent.PatientId, urgent.Reason));
        // Open until the doctor takes the patient in.
        await Intake(h.Nurse).KeepAsync(visit, VitalKind.SystolicBp);
        await Intake(h.Nurse).KeepAsync(visit, VitalKind.DiastolicBp);
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).HandOffAsync(visit);
        await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit));
        Assert.Empty(await Intake(h.Doctor).EscalationsAsync(h.Facility));
    }

    [Fact]
    public async Task SavedVitalsAreObservationsFromNurseIntakeVerifiedByTheNurse()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 128, dia: 82, temp: 36.8m));
        await using var db = EncountersDb(h.Tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
        var rows = await db.Observations.AsNoTracking().OrderBy(o => o.Code).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, o => Assert.Equal((ChartSource.ClinicianVerified, (Guid?)h.Nurse, (Guid?)h.Nurse, h.Patient, (Guid?)h.Facility, (Guid?)visit), (o.Source, o.RecordedBy, o.VerifiedBy, o.PatientId, o.FacilityId, o.AppointmentId)));
    }

    [Fact]
    public async Task OnlyTheVisitsNurseOrDoctorWorkTheIntakeAndOnlyWhileThePatientIsWithTheNurse()
    {
        var visit = await WithNurseAsync();
        // Reception and the practice admin have no vitals entry; the nurse of another facility cannot reach the visit.
        foreach (var user in new[] { h.Reception, h.Admin, h.Elsewhere })
        {
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).GetAsync(visit));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).RecordAsync(visit, Vitals(pulse: 70)));
        }
        Assert.Empty((await Intake(h.Nurse).GetAsync(visit))!.Vitals);
        Assert.Null(await Intake(h.Nurse).GetAsync(Guid.NewGuid()));

        await Intake(h.Nurse).RecordAsync(visit, Vitals(pulse: 70));
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).HandOffAsync(visit);
        Assert.Equal("not_with_nurse", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).RecordAsync(visit, Vitals(pulse: 80)))).Code); // readings do not change after handoff
        var booked = await h.BookAsync(11);
        Assert.Equal("not_with_nurse", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).RecordAsync(booked.Id, Vitals(pulse: 70)))).Code);
    }

    [Fact]
    public async Task EveryCallIsAuditedWithThePatientAndNoValuesInTheRow()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).GetAsync(visit);
        await Intake(h.Nurse).RecordAsync(visit, Vitals("Synthetic complaint", pulse: 70));
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit); // the allergy gate is its own tests; here the patient has none
        await Intake(h.Nurse).HandOffAsync(visit);
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "intake").OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(["intake.read", "intake.record", "intake.allergy.none", "intake.handoff"], rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, AuditPurpose.Treatment, (Guid?)visit, (Guid?)h.Nurse),
            (r.Outcome, r.PatientId, r.Purpose, r.ResourceId, r.ActorUserId)));
    }

    [Fact]
    public async Task AnotherTenantSeesNoIntakeRows()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).RecordAsync(visit, Vitals("Synthetic complaint", pulse: 70));
        await Intake(h.Nurse).EscalateAsync(visit, new EscalateIntake("Synthetic reason"));
        await using var db = EncountersDb(h.Tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        // Layer 2 (row-level security): even with the filter bypassed, a session of another tenant reads nothing, and writes nothing.
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {Guid.NewGuid().ToString()}, true)");
        Assert.Equal((0, 0, 0), (await db.Intakes.IgnoreQueryFilters().CountAsync(), await db.Observations.IgnoreQueryFilters().CountAsync(), await db.IntakeEscalations.IgnoreQueryFilters().CountAsync()));
    }
}
