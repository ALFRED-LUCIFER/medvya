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
    public async Task AnImpossibleValueIsRefusedAndNothingIsSaved(VitalKind kind, double value)
    {
        var visit = await WithNurseAsync();
        var refused = await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).RecordAsync(visit, new RecordIntake(null, [new(kind, (decimal)value)])));
        Assert.Contains(refused.Errors, e => e.Code == "implausible");
        Assert.Empty((await Intake(h.Nurse).GetAsync(visit))!.Vitals);
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
        Assert.Equal(["vitals_undecided"], saved.Blockers);
    }

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
        await Intake(h.Nurse).HandOffAsync(visit);
        Assert.Equal(AppointmentState.ReadyForDoctor, (await h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.ReadyForDoctor })).State); // a repeat

        // A clinic that has not turned the nurse step on keeps the board as it was.
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(IntakeFlag.Key, false, "clinical-team", "Nurse intake (synthetic test)");
        var other = await WithNurseAsync(h.SecondPatient, 10, 20);
        Assert.Equal(AppointmentState.ReadyForDoctor, (await h.FrontDesk(h.Reception).MoveAsync(other, new MoveVisit { State = AppointmentState.ReadyForDoctor })).State);
    }

    [Fact]
    public async Task EscalatingNeedsAReasonAndReachesTheTreatingDoctor()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).RecordAsync(visit, Vitals(sys: 210, dia: 130));
        foreach (var blank in Blanks)
            Assert.Contains(("reason", "required"), (await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).EscalateAsync(visit, new EscalateIntake(blank)))).Errors);
        Assert.Empty(await Intake(h.Doctor).EscalationsAsync(h.Facility));

        var escalated = await Intake(h.Nurse).EscalateAsync(visit, new EscalateIntake("Severe headache with BP 210/130 (synthetic)"));
        Assert.True(escalated!.Escalated);
        var urgent = Assert.Single(await Intake(h.Doctor).EscalationsAsync(h.Facility));
        Assert.Equal((visit, h.Patient, "Severe headache with BP 210/130 (synthetic)"), (urgent.AppointmentId, urgent.PatientId, urgent.Reason));
        // Open until the doctor takes the patient in.
        await Intake(h.Nurse).KeepAsync(visit, VitalKind.SystolicBp);
        await Intake(h.Nurse).KeepAsync(visit, VitalKind.DiastolicBp);
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
        await Intake(h.Nurse).HandOffAsync(visit);
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "intake").OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(["intake.read", "intake.record", "intake.handoff"], rows.Select(r => r.Action));
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
