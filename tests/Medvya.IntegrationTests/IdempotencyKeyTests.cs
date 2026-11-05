using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-047 on real SQL Server: a booking repeated with the same Idempotency-Key creates one appointment and returns the first
/// outcome, also when the repeats race; another request under the same key is refused; keys are per user and expire after 24 hours;
/// only hashes are stored. SAFETY: mutation check: removing the ReplayAsync calls in BookingService.BookAsync makes the repeat
/// fail with "hold_expired" and the race book once but answer the rest with 409, so these tests fail.
/// </summary>
public sealed class BookingIdempotencyTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    const string Key = "k-9f1c2a7e-booking-0001";
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    Task<HoldView> Hold(int hour, int minute = 0) =>
        h.Booking(h.Reception).HoldAsync(new() { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(hour, minute), VisitType = VisitType.FollowUp });

    BookAppointment Book(Guid holdId, Guid? patient = null) => new() { HoldId = holdId, PatientId = patient ?? h.Patient };

    [Fact]
    public async Task ARepeatWithTheSameKeyReturnsTheFirstAppointmentAndBooksNothingMore()
    {
        var hold = await Hold(9);
        var first = await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key);
        var again = await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key); // the hold is gone by now: the key answers

        Assert.Equal(first, again);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Single(await db.Appointments.ToListAsync());
        Assert.Single(await db.StatusEvents.ToListAsync());
        Assert.Single(await db.Outbox.ToListAsync());
    }

    [Fact]
    public async Task IdenticalRequestsAtTheSameTimeBookOnce()
    {
        var hold = await Hold(9);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => h.Booking(h.Reception).BookAsync(Book(hold.Id), Key))));

        Assert.Single(results.Select(r => r.Id).Distinct());
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Single(await db.Appointments.ToListAsync());
    }

    [Fact]
    public async Task TheSameKeyWithAnotherRequestIsRefusedAndNothingIsBooked()
    {
        var hold = await Hold(9);
        await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key);
        var other = await Hold(10);

        var refused = await Assert.ThrowsAsync<IdempotencyKeyException>(() => h.Booking(h.Reception).BookAsync(Book(other.Id, h.SecondPatient), Key));
        Assert.Equal("reused", refused.Code);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Single(await db.Appointments.ToListAsync());
    }

    [Fact]
    public async Task AKeyBelongsToItsUserAndAMalformedKeyIsRefused()
    {
        var hold = await Hold(9);
        var first = await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key);

        // Another user with the same key text gets no replay: their request is judged on its own (the hold is gone).
        await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Doctor).BookAsync(Book(hold.Id), Key));
        Assert.Equal("invalid", (await Assert.ThrowsAsync<IdempotencyKeyException>(() => h.Booking(h.Reception).BookAsync(Book(hold.Id), "short"))).Code);
        Assert.Equal(first.Id, (await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key)).Id);
    }

    [Fact]
    public async Task OnlyHashesAreStoredAndAKeyExpiresAfterADay()
    {
        var hold = await Hold(9);
        var first = await h.Booking(h.Reception).BookAsync(Book(hold.Id), Key);
        await using (var db = sql.Scheduling(h.Tenant))
        {
            var row = await db.Appointments.SingleAsync();
            Assert.Equal(64, row.IdempotencyKeyHash!.Length);
            Assert.DoesNotContain(Key, row.IdempotencyKeyHash + row.IdempotencyRequestHash, StringComparison.Ordinal);
        }

        await using (var old = sql.Scheduling(h.Tenant)) // the booking is now 25 hours old
            await old.Appointments.ExecuteUpdateAsync(s => s.SetProperty(x => x.BookedAt, h.Clock.GetUtcNow() - TimeSpan.FromHours(25)));
        var next = await Hold(11);
        var second = await h.Booking(h.Reception).BookAsync(Book(next.Id), Key); // the old key was cleared: a new booking, not a replay

        Assert.NotEqual(first.Id, second.Id);
        await using var after = sql.Scheduling(h.Tenant);
        Assert.Equal(2, await after.Appointments.CountAsync());
        Assert.Equal(1, await after.Appointments.CountAsync(a => a.IdempotencyKeyHash != null));
    }

    [Fact]
    public async Task ACheckInRepeatedChangesNothingTheSecondTime()
    {
        var visit = await h.BookAsync(10);
        var board = h.FrontDesk(h.Reception);
        var first = await board.CheckInAsync(visit.Id, new CheckIn());
        var again = await board.CheckInAsync(visit.Id, new CheckIn()); // check-in is idempotent by state, so a retried keyed request stores nothing

        Assert.Equal(first.State, again.State);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(2, await db.StatusEvents.CountAsync(e => e.AppointmentId == visit.Id)); // Booked, CheckedIn: no third event
    }
}

/// <summary>
/// MDV-P1-047 for registration. SAFETY: mutation check: removing the ReplayAsync calls in PatientRegistry.RegisterAsync makes the
/// repeat fail as an unresolved duplicate of its own first record and the race fail on the unique index.
/// </summary>
public sealed class RegistrationIdempotencyTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    const string Key = "k-9f1c2a7e-register-01";
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), actor = Guid.NewGuid();

    PatientRegistry Registry() =>
        new(sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(facility),
            new User(actor), new AuditLog(sql.Audit(tenant)), TimeProvider.System, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));

    sealed class User(Guid id) : ICurrentUser { public Guid? UserId => id; }

    RegisterPatient Meera => IndiaBase with { FacilityId = facility, FullName = "Meera Iyer (synthetic)", Mobile = "+91 90000 00401" };

    [Fact]
    public async Task ARepeatWithTheSameKeyReturnsTheFirstRegistration()
    {
        var first = await Registry().RegisterAsync(Meera, Key);
        var again = await Registry().RegisterAsync(Meera, Key);

        Assert.Equal(first.PatientId, again.PatientId);
        Assert.Equal(first.Mrn, again.Mrn);
        await using var db = sql.Patients(tenant);
        Assert.Single(await db.Patients.ToListAsync());
    }

    [Fact]
    public async Task IdenticalRegistrationsAtTheSameTimeCreateOnePatient()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => Registry().RegisterAsync(Meera, Key))));

        Assert.Single(results.Select(r => r.PatientId).Distinct());
        await using var db = sql.Patients(tenant);
        Assert.Single(await db.Patients.ToListAsync());
    }

    [Fact]
    public async Task TheSameKeyWithAnotherRequestIsRefused()
    {
        await Registry().RegisterAsync(Meera, Key);

        var refused = await Assert.ThrowsAsync<IdempotencyKeyException>(() => Registry().RegisterAsync(Meera with { Mobile = "+91 90000 00402" }, Key));
        Assert.Equal("reused", refused.Code);
        await using var db = sql.Patients(tenant);
        Assert.Single(await db.Patients.ToListAsync());
    }
}
