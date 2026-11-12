using System.Collections.Concurrent;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-001 on real PostgreSQL: one encounter per visit (repeat and simultaneous starts reuse it), the board's In consult and Billing
/// committed with it and pushed after, switching patient needs a decision, and every call is behind clinical access (MDV-P2-024) and audited.
/// </summary>
public sealed class EncounterTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Pushes pushes = new();

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    /// <summary>What the open boards were told, in order.</summary>
    sealed class Pushes : IBoardNotifier
    {
        public ConcurrentQueue<BoardChange> Changes { get; } = new();
        public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default)
        {
            Changes.Enqueue(change);
            return Task.CompletedTask;
        }
    }

    IEncounters Encounters(Guid user) => h.Get<IEncounters>(user, s => s.AddSingleton<IBoardNotifier>(pushes));

    async Task<Guid> ArrivedAsync(Guid? patient = null, int hour = 10, int minute = 0)
    {
        var visit = await h.BookAsync(hour, minute, patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        return visit.Id;
    }

    async Task<Appointment> VisitAsync(Guid id)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    async Task<int> EncounterRowsAsync()
    {
        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
        return await db.Encounters.CountAsync();
    }

    [Fact]
    public async Task StartingTwiceGivesOneEncounterAndCallsThePatientInToTheDoctorsRoom()
    {
        var visit = await ArrivedAsync();
        var first = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        var again = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));

        Assert.Equal(first!.Id, again!.Id);
        Assert.Equal((EncounterStatus.Active, AuthorityMode.Medvya, h.Patient, h.Doctor, h.Facility, "Room 3"),
            (first.Status, first.AuthorityMode, first.PatientId, first.LeadClinicianId, first.FacilityId, first.Room));
        Assert.Equal((AppointmentState.InConsult, "Room 3"), ((await VisitAsync(visit)).State, (await VisitAsync(visit)).Room));
        Assert.Equal(1, await EncounterRowsAsync());
        // The board was told once, after the commit, by ids and state only.
        var change = Assert.Single(pushes.Changes);
        Assert.Equal((h.Facility, visit, AppointmentState.InConsult), (change.FacilityId, change.AppointmentId, change.State));

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "encounter.start").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal((first.Id, h.Patient, AuditPurpose.Treatment), (r.ResourceId!.Value, r.PatientId!.Value, r.Purpose)));
    }

    [Fact]
    public async Task SimultaneousStartsStillGiveOneEncounter()
    {
        var visit = await ArrivedAsync();
        var all = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Encounters(h.Doctor).StartAsync(new StartEncounter(visit)))));
        Assert.Single(all.Select(e => e!.Id).Distinct());
        Assert.Equal(1, await EncounterRowsAsync());
        Assert.Equal(AppointmentState.InConsult, (await VisitAsync(visit)).State);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(6, await audit.Events.CountAsync(e => e.Action == "encounter.start"));
    }

    /// <summary>
    /// Forces each lost race of a double start (LoseTheRace): the other start commits right after this one has read the visit's encounter
    /// (<c>"AppointmentId" =</c>), or after it has also looked for the doctor's open consultation (<c>"Status" =</c>, so its insert hits the
    /// unique index). Either way it answers with the winner's encounter. SAFETY: mutation check: without the <c>AppointmentId != visit.Id</c>
    /// in the open-consultation check, the first loser is refused with OpenEncounterException.
    /// </summary>
    [Theory]
    [InlineData("\"AppointmentId\" = ")]
    [InlineData("\"Status\" = ")]
    public async Task AStartThatLosesTheRaceAnswersWithTheWinner(string after)
    {
        var visit = await ArrivedAsync();
        EncounterView? winner = null;
        var race = new LoseTheRace(async () => winner = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit)), after);
        var loser = await h.Get<IEncounters>(h.Doctor, s => s.AddSingleton<IBoardNotifier>(pushes)
            .ConfigureDbContext<EncountersDbContext>(o => o.AddInterceptors(race))).StartAsync(new StartEncounter(visit));

        Assert.True(race.Fired);
        Assert.Equal((winner!.Id, EncounterStatus.Active, "Room 3"), (loser!.Id, loser.Status, loser.Room));
        Assert.Equal(1, await EncounterRowsAsync());
        Assert.Equal(AppointmentState.InConsult, (await VisitAsync(visit)).State);
        Assert.Single(pushes.Changes); // only the winner moved the visit
    }

    [Fact]
    public async Task APatientWhoHasNotArrivedCannotBeSeenAndNothingIsKept()
    {
        var booked = await h.BookAsync(10);
        Assert.Equal("not_arrived", (await Assert.ThrowsAsync<VisitTransitionException>(() => Encounters(h.Doctor).StartAsync(new StartEncounter(booked.Id)))).Code);
        Assert.Equal(0, await EncounterRowsAsync()); // the encounter rolled back with the refused move
        Assert.Equal(AppointmentState.Booked, (await VisitAsync(booked.Id)).State);
        Assert.Empty(pushes.Changes);
        Assert.Null(await Encounters(h.Doctor).StartAsync(new StartEncounter(Guid.NewGuid())));
    }

    [Fact]
    public async Task YesterdaysUnfinishedVisitIsNotCalledInToday()
    {
        var visit = await ArrivedAsync();
        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal("not_today", (await Assert.ThrowsAsync<VisitTransitionException>(() => Encounters(h.Doctor).StartAsync(new StartEncounter(visit)))).Code);
        Assert.Equal(0, await EncounterRowsAsync());
        Assert.Equal(AppointmentState.CheckedIn, (await VisitAsync(visit)).State);
    }

    [Fact]
    public async Task AVisitTheDeskAlreadyFinishedHasNoConsultation()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var visit = await ArrivedAsync();
        foreach (var to in new[] { AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing })
            await h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = to });
        Assert.Equal("finished", (await Assert.ThrowsAsync<VisitTransitionException>(() => Encounters(h.Doctor).StartAsync(new StartEncounter(visit)))).Code);
        Assert.Equal(0, await EncounterRowsAsync());
        Assert.Equal(AppointmentState.Billing, (await VisitAsync(visit)).State);
    }

    [Fact]
    public async Task SwitchingPatientNeedsTheOpenConsultationEndedOrLeftOpen()
    {
        var meera = await ArrivedAsync(h.Patient, 10);
        var deepak = await ArrivedAsync(h.SecondPatient, 11);
        var open = await Encounters(h.Doctor).StartAsync(new StartEncounter(meera));

        var refused = await Assert.ThrowsAsync<OpenEncounterException>(() => Encounters(h.Doctor).StartAsync(new StartEncounter(deepak)));
        Assert.Equal(open!.Id, refused.EncounterId);
        Assert.Equal(AppointmentState.CheckedIn, (await VisitAsync(deepak)).State);

        var second = await Encounters(h.Doctor).StartAsync(new StartEncounter(deepak, LeaveOpen: true));
        Assert.Equal(h.SecondPatient, second!.PatientId);
        Assert.Equal(EncounterStatus.Active, (await Encounters(h.Doctor).GetAsync(open.Id))!.Status); // left open, as chosen

        // Another doctor's open consultation is not this doctor's to decide about.
        var third = await ArrivedAsync(h.Patient, 11, 20); // 12:00 is lunch
        Assert.NotNull(await Encounters(h.SecondDoctor).StartAsync(new StartEncounter(third)));
    }

    [Fact]
    public async Task EndingMovesTheVisitToBillingOnceAndTheVisitIsNotReopened()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var visit = await ArrivedAsync();
        var started = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        var ended = await Encounters(h.Doctor).EndAsync(started!.Id);
        Assert.Equal(EncounterStatus.Ended, ended!.Status);
        Assert.Equal(h.Clock.GetUtcNow(), ended.EndedAt);
        Assert.Equal(AppointmentState.Billing, (await VisitAsync(visit)).State);
        Assert.Equal([AppointmentState.InConsult, AppointmentState.Billing], pushes.Changes.Select(c => c.State));

        Assert.Equal(EncounterStatus.Ended, (await Encounters(h.Doctor).EndAsync(started.Id))!.Status); // ending again changes nothing
        Assert.Equal(2, pushes.Changes.Count);
        await Assert.ThrowsAsync<EncounterEndedException>(() => Encounters(h.Doctor).StartAsync(new StartEncounter(visit)));
        Assert.Null(await Encounters(h.Doctor).EndAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task WithBillingOffEndingFinishesTheVisit()
    {
        var visit = await ArrivedAsync();
        var started = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        await Encounters(h.Doctor).EndAsync(started!.Id);
        Assert.Equal(AppointmentState.Done, (await VisitAsync(visit)).State);
        Assert.Equal([AppointmentState.InConsult, AppointmentState.Done], pushes.Changes.Select(c => c.State));
    }

    [Fact]
    public async Task ADeskMoveBeforeTheEndLeavesTheVisitWhereTheDeskPutIt()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var visit = await ArrivedAsync();
        var started = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        await h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.Billing });
        await h.FrontDesk(h.Reception).MoveAsync(visit, new MoveVisit { State = AppointmentState.Done });
        Assert.Equal(EncounterStatus.Ended, (await Encounters(h.Doctor).EndAsync(started!.Id))!.Status);
        Assert.Equal(AppointmentState.Done, (await VisitAsync(visit)).State);
    }

    [Fact]
    public async Task EveryCallIsBehindClinicalAccess()
    {
        var visit = await ArrivedAsync();
        // Reception, the nurse (Limited), the practice admin: none starts a consultation, and asking for access cannot change a role.
        foreach (var user in new[] { h.Reception, h.Nurse, h.Admin })
            Assert.False((await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Encounters(user).StartAsync(new StartEncounter(visit)))).CanRequest);
        Assert.Equal(0, await EncounterRowsAsync());

        var started = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        Assert.NotNull(await Encounters(h.Nurse).GetAsync(started!.Id)); // the nurse sees the banner of today's patient
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Encounters(h.Reception).GetAsync(started.Id));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Encounters(h.Nurse).EndAsync(started.Id));

        // Assigned only: Dr. Menon is not Meera's doctor, so he may ask for access but not open or end her consultation.
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        Assert.True((await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Encounters(h.SecondDoctor).GetAsync(started.Id))).CanRequest);
        Assert.True((await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Encounters(h.SecondDoctor).EndAsync(started.Id))).CanRequest);
        Assert.Equal(EncounterStatus.Active, (await Encounters(h.Doctor).GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task LeadingAnEncounterIsACareRelationshipUnderAssignedOnly()
    {
        // Dr. Menon covers Dr. Rao's patient while the clinic treats all its patients; later the clinic restricts access.
        var visit = await ArrivedAsync();
        var covered = await Encounters(h.SecondDoctor).StartAsync(new StartEncounter(visit));
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        Assert.NotNull(await Encounters(h.SecondDoctor).GetAsync(covered!.Id)); // he led it, so she is his patient too
        Assert.Equal(Access.Full, await h.Get<IClinicalAccess>(h.SecondDoctor).RequireAsync(h.Patient, h.Facility, Capability.Patient360Chart, [Access.Full]));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() =>
            h.Get<IClinicalAccess>(h.SecondDoctor).RequireAsync(h.SecondPatient, h.Facility, Capability.Patient360Chart, [Access.Full]));
    }

    [Fact]
    public async Task EncountersAreClinicalDataAndMoveWithAMergeAndBack()
    {
        var visit = await ArrivedAsync(h.SecondPatient);
        var started = await Encounters(h.Doctor).StartAsync(new StartEncounter(visit));
        var merge = h.Get<Medvya.Patients.Application.IPatientMergeParticipant>(h.Doctor, s => s.AddScoped<Medvya.Patients.Application.IPatientMergeParticipant, EncounterMergeParticipant>());
        Assert.True(await merge.HasClinicalDataAsync(h.SecondPatient, default));
        Assert.False(await merge.HasClinicalDataAsync(h.Patient, default));

        await using var db = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(sql.ConnectionString).Options, new PostgresFixture.FixedTenant(h.Tenant));
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('medvya.tenant_id', {h.Tenant.ToString()}, true)");
            var connectionTx = Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(tx);
            await merge.MoveAsync(h.SecondPatient, h.Patient, connectionTx, default);
            Assert.Equal(h.Patient, (await db.Encounters.AsNoTracking().SingleAsync(e => e.Id == started!.Id)).PatientId);
            await merge.RestoreAsync(h.SecondPatient, h.Patient, connectionTx, default);
            Assert.Equal(h.SecondPatient, (await db.Encounters.AsNoTracking().SingleAsync(e => e.Id == started!.Id)).PatientId);
        }
    }
}
