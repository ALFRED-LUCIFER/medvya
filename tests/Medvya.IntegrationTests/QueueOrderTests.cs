using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-103 on real PostgreSQL: the board, the clinic overview and the queue display read one ordered-queue query, so under each clinic
/// rule they list the waiting patients in the same order; a nurse escalation heads all three; a walk-in counts from her arrival; the rule
/// is the clinic owner's, audited and scoped to its tenant; the display names the doctor of each token after a reassignment.
/// </summary>
public sealed class QueueOrderTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await other.SeedAsync();
        await h.StepsAsync(nurse: true, billing: false); // so a checked-in patient waits for the nurse, who can escalate
    }

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        await other.CloseAsync();
    }

    /// <summary>
    /// The audit's morning (08:00 now): Early booked 10:20 and comes at 09:50; a walk-in comes at 09:58 and is given a later slot;
    /// OnTime booked 10:00 and comes at 10:05. Tokens A-01, A-02, A-03 in that order.
    /// </summary>
    Task<(Guid Early, Guid WalkIn, Guid OnTime)> MorningAsync() => MorningAsync(h);

    static async Task<(Guid Early, Guid WalkIn, Guid OnTime)> MorningAsync(SchedulingHarness h)
    {
        var onTime = await h.BookAsync(10, 0, h.Patient);
        var early = await h.BookAsync(10, 20, h.SecondPatient);
        h.Clock.Advance(TimeSpan.FromMinutes(110));
        await h.FrontDesk(h.Reception).CheckInAsync(early.Id, new CheckIn());
        h.Clock.Advance(TimeSpan.FromMinutes(8));
        var walkIn = await h.Booking(h.Reception).WalkInAsync(new WalkIn
        {
            FacilityId = h.Facility, CalendarId = h.Doctor, PatientId = h.Patient, ReasonCategory = ReasonCategory.General, AllowRepeat = true,
        });
        Assert.True(walkIn.Start > early.Start); // her slot is after both booked patients' slots
        await h.FrontDesk(h.Reception).CheckInAsync(walkIn.Id, new CheckIn());
        h.Clock.Advance(TimeSpan.FromMinutes(7));
        await h.FrontDesk(h.Reception).CheckInAsync(onTime.Id, new CheckIn());
        return (early.Id, walkIn.Id, onTime.Id);
    }

    /// <summary>The waiting patients as the board (by place), the clinic overview (Next) and the queue display (Next tokens) list them.</summary>
    async Task<(Guid[] Board, Guid[] Overview, Guid[] Display)> ScreensAsync()
    {
        var board = await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility);
        var byToken = board.Cards.Where(c => c.Token is not null).ToDictionary(c => c.Token!, c => c.Id);
        var overview = await h.FrontDesk(h.Reception).GetOverviewAsync(h.Facility);
        var tv = await h.Get<IQueueService>(h.Reception).TvAsync();
        return (
            [.. board.Cards.Where(c => c.QueuePlace is not null).OrderBy(c => c.QueuePlace).Select(c => c.Id)],
            [.. overview.Doctors.Single(d => d.CalendarId == h.Doctor).Next.Select(n => n.Id)],
            [.. tv.Next.Select(n => byToken[n.Token])]);
    }

    async Task AssertAllThree(Guid[] expected)
    {
        var (board, overview, display) = await ScreensAsync();
        Assert.Equal(expected, board);
        Assert.Equal(expected, overview);
        Assert.Equal(expected, display);
    }

    [Fact]
    public async Task EveryScreenFollowsTheClinicsRuleAndANurseEscalationHeadsThemAll()
    {
        var (early, walkIn, onTime) = await MorningAsync();
        Assert.Equal(QueueRule.Default, await h.Settings(h.Admin).GetQueueRuleAsync()); // existing clinics keep arrival order
        await AssertAllThree([early, walkIn, onTime]);

        await h.Settings(h.Admin).SaveQueueRuleAsync(new(QueueOrder.Appointment, false));
        await AssertAllThree([walkIn, onTime, early]); // the walk-in counts from 09:58, not from her slot

        await h.Settings(h.Admin).SaveQueueRuleAsync(new(QueueOrder.Arrival, true));
        await AssertAllThree([early, onTime, walkIn]);

        await h.Settings(h.Admin).SaveQueueRuleAsync(new(QueueOrder.Token, false));
        await AssertAllThree([early, walkIn, onTime]);

        // SAFETY mutation check (MDV-P1-103 AC5): drop the escalation-first step of QueueRules.Order and this fails.
        await h.FrontDesk(h.Reception).MoveAsync(onTime, new MoveVisit { State = AppointmentState.WithNurse });
        await h.Get<IIntake>(h.Nurse).EscalateAsync(onTime, new EscalateIntake("Fever with BP 168/96 (synthetic)"));
        await AssertAllThree([onTime, early, walkIn]);
        await h.Settings(h.Admin).SaveQueueRuleAsync(new(QueueOrder.Appointment, true));
        await AssertAllThree([onTime, early, walkIn]);
    }

    [Fact]
    public async Task TheRuleIsAuditedAndScopedToItsClinic()
    {
        var (early, walkIn, onTime) = await MorningAsync();
        await other.Settings(other.Admin).SaveQueueRuleAsync(new(QueueOrder.Appointment, true));

        Assert.Equal(new QueueRule(QueueOrder.Appointment, true), await other.Settings(other.Admin).GetQueueRuleAsync());
        Assert.Equal(QueueRule.Default, await h.Settings(h.Admin).GetQueueRuleAsync());
        await AssertAllThree([early, walkIn, onTime]); // the other clinic's rule changes nothing here
        await using (var audit = sql.Audit(other.Tenant))
        {
            var row = Assert.Single(await audit.Events.Where(e => e.Action == "settings.queue_rule.update").ToListAsync());
            Assert.Equal((other.Admin, AuditOutcome.Allowed, (Guid?)null), (row.ActorUserId, row.Outcome, row.PatientId));
        }
        await using (var audit = sql.Audit(h.Tenant))
            Assert.Empty(await audit.Events.Where(e => e.Action == "settings.queue_rule.update").ToListAsync());
        await Assert.ThrowsAsync<SettingsValidationException>(() => h.Settings(h.Admin).SaveQueueRuleAsync(new((QueueOrder)7, false)));
    }

    [Fact]
    public async Task TheDisplayNamesTheDoctorOfEachTokenAfterAReassignment()
    {
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        var (_, walkIn, _) = await MorningAsync();
        await h.FrontDesk(h.Reception).ReassignAsync(walkIn, new ReassignVisit { CalendarId = h.SecondDoctor, Reason = ReassignReason.BalanceWorkload });

        var board = await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility);
        var tv = await h.Get<IQueueService>(h.Reception).TvAsync();
        // A-02 keeps its letter but waits for the second doctor, and the display says so; tokens and doctors only, never a name.
        Assert.Equal(board.Cards.Single(c => c.Id == walkIn).CalendarName, tv.Next.Single(n => n.Token == "A-02").Doctor);
        Assert.All(tv.Next.Where(n => n.Token != "A-02"), n => Assert.Equal(board.Cards.Single(c => c.Id != walkIn && c.Token == n.Token).CalendarName, n.Doctor));
        Assert.NotEqual(tv.Next.Single(n => n.Token == "A-01").Doctor, tv.Next.Single(n => n.Token == "A-02").Doctor);
    }

    /// <summary>
    /// Decision 10 of the validation plan: each doctor has their own sequence and the patient is told how many are ahead. Dr. Menon's two
    /// walk-ins come after Dr. Rao's three patients, yet are 1 and 2 in Dr. Menon's queue; the board's place, the display's count ahead and
    /// the token slip agree. Reassigned, Dr. Rao's 09:58 walk-in takes her place in Dr. Menon's queue by her arrival.
    /// SAFETY, tenant isolation: another organisation's morning is put on this clinic's facility and Dr. Rao's calendar ids (a doctor who works
    /// for both), so only the tenant filter and row-level security keep its patients out of the count. Mutation checks: grouping
    /// QueueRules.Places by one key for the whole clinic fails; dropping the Scheduling tenant query filter and disabling row-level security on
    /// Appointments and QueueTokens together fails (either alone does not: the two layers, ADR-012).
    /// </summary>
    [Fact]
    public async Task EachDoctorHasTheirOwnSequenceAndAnotherOrganisationsPatientsNeverCount()
    {
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        await other.DoctorTemplateAsync();
        var (early, walkIn, onTime) = await MorningAsync();
        await MorningAsync(other); // put on our ids after our bookings, so it meets only the queue
        await using (var admin = new NpgsqlConnection(sql.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var move = new NpgsqlCommand("""
                UPDATE scheduling."Appointments" SET "FacilityId" = @facility, "CalendarId" = @doctor WHERE "TenantId" = @tenant;
                UPDATE scheduling."QueueTokens" SET "FacilityId" = @facility, "CalendarId" = @doctor WHERE "TenantId" = @tenant;
                """, admin);
            move.Parameters.AddWithValue("facility", h.Facility);
            move.Parameters.AddWithValue("doctor", h.Doctor);
            move.Parameters.AddWithValue("tenant", other.Tenant);
            Assert.Equal(6, await move.ExecuteNonQueryAsync());
        }
        async Task<Guid> MenonWalkInAsync(Guid patient)
        {
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            var visit = await h.Booking(h.Reception).WalkInAsync(new WalkIn
            {
                FacilityId = h.Facility, CalendarId = h.SecondDoctor, PatientId = patient, ReasonCategory = ReasonCategory.General, AllowRepeat = true,
            });
            await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
            return visit.Id;
        }
        var (menon1, menon2) = (await MenonWalkInAsync(h.SecondPatient), await MenonWalkInAsync(h.Patient));

        async Task AssertPlaces(params (Guid Visit, int Place)[] expected)
        {
            var board = (await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards;
            var tv = (await h.Get<IQueueService>(h.Reception).TvAsync()).Next.ToDictionary(n => n.Token, n => n.Ahead);
            foreach (var (visit, place) in expected)
            {
                var card = board.Single(c => c.Id == visit);
                Assert.Equal((visit, place), (visit, card.QueuePlace));
                Assert.Equal((visit, place - 1), (visit, tv[card.Token!])); // the display: how many ahead
                Assert.Equal((visit, (int?)(place - 1)), (visit, (await h.FrontDesk(h.Reception).TokenSlipAsync(visit)).Ahead)); // the slip
            }
            Assert.Equal(expected.Length, board.Count(c => c.QueuePlace is not null));
        }

        await AssertPlaces((early, 1), (walkIn, 2), (onTime, 3), (menon1, 1), (menon2, 2));

        await h.FrontDesk(h.Reception).ReassignAsync(walkIn, new ReassignVisit { CalendarId = h.SecondDoctor, Reason = ReassignReason.BalanceWorkload });
        await AssertPlaces((early, 1), (onTime, 2), (walkIn, 1), (menon1, 2), (menon2, 3)); // 09:58 is before both of Dr. Menon's walk-ins
    }
}
