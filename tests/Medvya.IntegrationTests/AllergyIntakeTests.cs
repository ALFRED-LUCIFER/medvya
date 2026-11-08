using System.Collections.Concurrent;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
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
/// MDV-P2-008 on real PostgreSQL: the handoff is impossible (service and board) until every allergy has a status, "no known allergies" is never the
/// default, source and actor are stored, and the board moves on handoff. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class AllergyIntakeTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly ConcurrentQueue<BoardChange> pushes = new();

    sealed class Pushes(ConcurrentQueue<BoardChange> into) : IBoardNotifier
    {
        public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default)
        {
            into.Enqueue(change);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(IntakeFlag.Key, true, "clinical-team", "Nurse intake (synthetic test)");
    }

    public Task DisposeAsync() => h.CloseAsync();

    IIntake Intake(Guid user) => h.Get<IIntake>(user, s => s.AddSingleton<IBoardNotifier>(new Pushes(pushes)));

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

    /// <summary>An allergy recorded by someone else (the patient, through the portal later): no status yet.</summary>
    async Task<Guid> UnreviewedAllergyAsync(Guid patient, string substance = "Penicillin (synthetic)")
    {
        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
        var allergy = new Allergy
        {
            Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = patient, Kind = AllergyKind.Substance, Substance = substance,
            Status = AllergyStatus.PatientReported, Source = ChartSource.PatientReported, RecordedBy = h.PortalUser, RecordedAt = h.Clock.GetUtcNow(), // StatusSetAt null: nobody at the clinic has looked
        };
        db.Allergies.Add(allergy);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return allergy.Id;
    }

    /// <summary>SAFETY, mutation check: remove the "allergy_status_required" blocker and the handoff goes through with an allergy nobody looked at.</summary>
    [Fact]
    public async Task HandoffIsImpossibleWhileAnAllergyHasNoStatus()
    {
        var visit = await WithNurseAsync();
        var allergy = await UnreviewedAllergyAsync(h.Patient);

        var shown = await Intake(h.Nurse).GetAsync(visit);
        Assert.Equal((null, ChartSource.PatientReported), (shown!.Allergies.Single().Status, shown.Allergies.Single().Source));
        Assert.Contains("allergy_status_required", shown.Blockers);
        var blocked = await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).HandOffAsync(visit));
        Assert.Equal(("handoff_blocked", true), (blocked.Code, blocked.Blockers.Contains("allergy_status_required")));
        Assert.Equal(AppointmentState.WithNurse, await StateAsync(visit));
        // The board's own move cannot get round it either (the flag is on).
        Assert.Equal("intake_required", (await Assert.ThrowsAsync<VisitTransitionException>(() =>
            h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.ReadyForDoctor }))).Code);

        var set = await Intake(h.Nurse).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(AllergyStatus.Confirmed));
        Assert.DoesNotContain("allergy_status_required", set!.Blockers);
        Assert.Empty(set.Blockers); // an allergy on record needs no "no known allergies"
        Assert.Equal(AllergyStatus.Confirmed, set.Allergies.Single().Status);
        Assert.Equal("ReadyForDoctor", (await Intake(h.Nurse).HandOffAsync(visit))!.State);
        Assert.Equal(AppointmentState.ReadyForDoctor, await StateAsync(visit));
    }

    [Theory]
    [InlineData(AllergyStatus.PatientReported)]
    [InlineData(AllergyStatus.Unknown)]
    [InlineData(AllergyStatus.NotAnAllergy)]
    public async Task AnyStatusSetsItAndUnlocksTheHandoff(AllergyStatus status)
    {
        var visit = await WithNurseAsync();
        var allergy = await UnreviewedAllergyAsync(h.Patient);
        Assert.Empty((await Intake(h.Nurse).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(status)))!.Blockers);
    }

    /// <summary>SAFETY, mutation check: default "no known allergies" to confirmed (or drop the "allergies_not_confirmed" blocker) and a patient nobody asked is handed off.</summary>
    [Fact]
    public async Task NoKnownAllergiesIsNeverTheDefaultAndMustBeConfirmedOnPurpose()
    {
        var visit = await WithNurseAsync();
        var start = await Intake(h.Nurse).GetAsync(visit);
        Assert.False(start!.NoKnownAllergiesConfirmed);
        Assert.Equal(["allergies_not_confirmed"], start.Blockers);
        Assert.Equal("allergies_not_confirmed", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).HandOffAsync(visit))).Blockers.Single());
        Assert.Equal(AppointmentState.WithNurse, await StateAsync(visit));

        var confirmed = await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit);
        Assert.Equal((true, 0), (confirmed!.NoKnownAllergiesConfirmed, confirmed.Blockers.Count));
        Assert.True((await Intake(h.Nurse).HandOffAsync(visit))!.HandedOff);

        // One confirmation is for one visit: the patient's next visit starts unconfirmed again.
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        var next = await WithNurseAsync(hour: 11);
        Assert.Contains("allergies_not_confirmed", (await Intake(h.Nurse).GetAsync(next))!.Blockers);

        // The chart keeps the statement once (Patient 360 reads it), even when the next visit confirms it again.
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(next);
        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
        var rows = await db.Allergies.AsNoTracking().Where(a => a.PatientId == h.Patient).ToListAsync();
        Assert.Equal((AllergyKind.NoKnownAllergies, ChartSource.ClinicianVerified), (rows.Single().Kind, rows.Single().Source));
    }

    [Fact]
    public async Task AnAllergyAddedAfterNoKnownAllergiesWasConfirmedNeedsItsOwnStatusAndTheConfirmationNoLongerCounts()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit);
        var added = await Intake(h.Nurse).AddAllergyAsync(visit, new AddAllergy("Peanuts (synthetic)", "Hives", AllergyStatus.PatientReported));
        Assert.Equal((false, 1), (added!.NoKnownAllergiesConfirmed, added.Allergies.Count));
        // With an allergy on record "none" cannot be confirmed any more.
        Assert.Equal("has_allergies", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).ConfirmNoKnownAllergiesAsync(visit))).Code);
        Assert.Empty(added.Blockers);
    }

    [Fact]
    public async Task SourceAndActorAreStoredWithAllergyStatusesAndPatientReportedMedicines()
    {
        var visit = await WithNurseAsync();
        var theirs = await UnreviewedAllergyAsync(h.Patient, "Latex (synthetic)");
        await Intake(h.Nurse).AddAllergyAsync(visit, new AddAllergy("  Sulfa drugs (synthetic) ", "Rash", AllergyStatus.Confirmed));
        await Intake(h.Nurse).SetAllergyStatusAsync(visit, theirs, new SetAllergyStatus(AllergyStatus.PatientReported));
        var view = await Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine("a white tablet for sugar", " ", null, true));
        await Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine("Metformin (synthetic)", "500 mg", "twice a day", false));

        Assert.Equal(["Latex (synthetic)", "Sulfa drugs (synthetic)"], view!.Allergies.Select(a => a.Substance).Order());
        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
        var allergies = await db.Allergies.AsNoTracking().OrderBy(a => a.Substance).ToListAsync();
        // Theirs keeps its source (the patient) and who added it; the nurse is the actor of the status. The nurse's own is Nurse intake throughout.
        Assert.Equal((ChartSource.PatientReported, (Guid?)h.PortalUser, AllergyStatus.PatientReported, (Guid?)h.Nurse), (allergies[0].Source, allergies[0].RecordedBy, allergies[0].Status, allergies[0].StatusSetBy));
        Assert.Equal((ChartSource.PatientReported, (Guid?)h.Nurse, AllergyStatus.Confirmed, (Guid?)h.Nurse, "Rash"), (allergies[1].Source, allergies[1].RecordedBy, allergies[1].Status, allergies[1].StatusSetBy, allergies[1].Reaction));
        var medicines = await db.MedicationStatements.AsNoTracking().OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync();
        Assert.Equal(2, medicines.Count);
        Assert.All(medicines, m => Assert.Equal((ChartSource.PatientReported, (Guid?)h.Nurse, h.Patient, (Guid?)visit), (m.Source, m.RecordedBy, m.PatientId, m.AppointmentId)));
        Assert.Equal(("a white tablet for sugar", null, null, true), (medicines[0].Name, medicines[0].Dose, medicines[0].Frequency, medicines[0].NeedsClarification)); // blank stays blank
    }

    [Fact]
    public async Task RefusesWhatIsInvalidAndAnAllergyThatIsNotThisPatients()
    {
        var visit = await WithNurseAsync();
        var other = await WithNurseAsync(h.SecondPatient, 10, 20);
        var mine = await UnreviewedAllergyAsync(h.Patient);
        var notMine = await UnreviewedAllergyAsync(h.SecondPatient, "Shellfish (synthetic)");

        foreach (var (command, field) in new (AddAllergy, string)[]
        {
            (new(null, null, AllergyStatus.Confirmed), "substance"), (new("  ", null, AllergyStatus.Confirmed), "substance"), (new("X", null, null), "status"),
            (new("X", null, AllergyStatus.NotAnAllergy), "status"), (new(new string('x', 101), null, AllergyStatus.Confirmed), "substance"), (new("Penicillin (SYNTHETIC)", null, AllergyStatus.Confirmed), "substance"),
        })
            Assert.Contains(field, (await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).AddAllergyAsync(visit, command))).Errors.Select(e => e.Field));
        Assert.Contains(("name", "required"), (await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine(" ", null, null, false)))).Errors);
        await Assert.ThrowsAsync<IntakeValidationException>(() => Intake(h.Nurse).SetAllergyStatusAsync(visit, mine, new SetAllergyStatus(null)));
        Assert.Single((await Intake(h.Nurse).GetAsync(visit))!.Allergies); // nothing invalid was kept

        // The other patient's allergy, and an unknown one, are simply not found: nothing changes.
        Assert.Null(await Intake(h.Nurse).SetAllergyStatusAsync(visit, notMine, new SetAllergyStatus(AllergyStatus.Confirmed)));
        Assert.Null(await Intake(h.Nurse).SetAllergyStatusAsync(visit, Guid.NewGuid(), new SetAllergyStatus(AllergyStatus.Confirmed)));
        Assert.Contains("allergy_status_required", (await Intake(h.Nurse).GetAsync(other))!.Blockers);
    }

    [Fact]
    public async Task OnlyTheNurseWorksAllergiesAndOnlyWhilePatientIsWithTheNurse()
    {
        var visit = await WithNurseAsync();
        var allergy = await UnreviewedAllergyAsync(h.Patient);
        foreach (var user in new[] { h.Reception, h.Admin, h.Elsewhere })
        {
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(AllergyStatus.Confirmed)));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).AddAllergyAsync(visit, new AddAllergy("X", null, AllergyStatus.Confirmed)));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).ConfirmNoKnownAllergiesAsync(visit));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Intake(user).AddMedicineAsync(visit, new AddMedicine("X", null, null, false)));
        }
        Assert.Null((await Intake(h.Nurse).GetAsync(visit))!.Allergies.Single().Status);

        await Intake(h.Nurse).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(AllergyStatus.Confirmed));
        await Intake(h.Nurse).HandOffAsync(visit);
        Assert.Equal("not_with_nurse", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(AllergyStatus.Unknown)))).Code);
        Assert.Equal("not_with_nurse", (await Assert.ThrowsAsync<IntakeConflictException>(() => Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine("X", null, null, false)))).Code);
    }

    [Fact]
    public async Task HandoffMovesTheBoardAndTellsTheOpenBoards()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).AddAllergyAsync(visit, new AddAllergy("Peanuts (synthetic)", null, AllergyStatus.Confirmed));
        Assert.Empty(pushes); // nothing told yet: the card has not moved
        await Intake(h.Nurse).HandOffAsync(visit);
        var change = Assert.Single(pushes);
        Assert.Equal((h.Facility, visit, AppointmentState.ReadyForDoctor), (change.FacilityId, change.AppointmentId, change.State));
    }

    [Fact]
    public async Task AnotherTenantSeesNoAllergiesOrMedicines()
    {
        var visit = await WithNurseAsync();
        await Intake(h.Nurse).AddAllergyAsync(visit, new AddAllergy("Peanuts (synthetic)", null, AllergyStatus.Confirmed));
        await Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine("Metformin (synthetic)", null, null, false));
        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {Guid.NewGuid().ToString()}, true)");
        Assert.Equal((0, 0), (await db.Allergies.IgnoreQueryFilters().CountAsync(), await db.MedicationStatements.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task EveryAllergyCallIsAuditedWithThePatient()
    {
        var visit = await WithNurseAsync();
        var allergy = await UnreviewedAllergyAsync(h.Patient);
        await Intake(h.Nurse).SetAllergyStatusAsync(visit, allergy, new SetAllergyStatus(AllergyStatus.Confirmed));
        await Intake(h.Nurse).AddAllergyAsync(visit, new AddAllergy("Peanuts (synthetic)", null, AllergyStatus.Confirmed));
        await Intake(h.Nurse).AddMedicineAsync(visit, new AddMedicine("Metformin (synthetic)", null, null, false));
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "intake").OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(["intake.allergy.status", "intake.allergy.add", "intake.medicine.add"], rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, (Guid?)h.Nurse), (r.Outcome, r.PatientId, r.ActorUserId)));
    }
}
