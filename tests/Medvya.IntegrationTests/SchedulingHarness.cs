using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>A clock tests move by hand, so slot holds can expire without waiting.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset now = start;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}

/// <summary>
/// One synthetic clinic for scheduling tests: an Adyar facility in IST with a doctor, a second doctor without a template, reception,
/// a practice admin, a nurse, a portal user and two patients, plus the scheduling services built as the host builds them.
/// </summary>
internal sealed class SchedulingHarness(SqlServerFixture sql)
{
    /// <summary>Monday 19 Oct 2026, 08:00 IST: before the first slot of the day.</summary>
    public static readonly DateTimeOffset MondayMorning = new(2026, 10, 19, 2, 30, 0, TimeSpan.Zero);
    public static readonly DateOnly Monday = new(2026, 10, 19);

    public readonly Guid Tenant = Guid.NewGuid(), Facility = Guid.NewGuid(), OtherFacility = Guid.NewGuid();
    public readonly Guid Doctor = Guid.NewGuid(), SecondDoctor = Guid.NewGuid(), Reception = Guid.NewGuid(), Admin = Guid.NewGuid(),
        Nurse = Guid.NewGuid(), PortalUser = Guid.NewGuid(), Elsewhere = Guid.NewGuid();
    public readonly Guid Patient = Guid.NewGuid(), SecondPatient = Guid.NewGuid();
    public ManualClock Clock { get; } = new(MondayMorning);
    public SchedulingOptions Options { get; } = new();
    /// <summary>A pool big enough for a hundred desks booking at once (each request also opens Tenancy and Patients connections).</summary>
    public string ConnectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sql.ConnectionString) { MaxPoolSize = 400 }.ConnectionString;
    readonly List<ServiceProvider> providers = [];

    public async Task SeedAsync()
    {
        await using (var db = sql.Context(Tenant))
        {
            db.Tenants.Add(new Tenant { Id = Tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = Facility, TenantId = Tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = OtherFacility, TenantId = Tenant, Name = "Velachery (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (user, role, at) in new[]
            {
                (Doctor, Role.SoloDoctor, Facility), (SecondDoctor, Role.HospitalDoctor, Facility), (Reception, Role.Receptionist, Facility),
                (Admin, Role.PracticeAdmin, Facility), (Nurse, Role.Nurse, Facility), (PortalUser, Role.Patient, Facility), (Elsewhere, Role.PracticeAdmin, OtherFacility),
            })
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = Tenant, UserId = user, FacilityId = at, Role = role, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = Doctor, Issuer = "test-idp", Subject = Doctor.ToString(), DisplayName = "Dr. Arjun Rao (synthetic)" });
            identity.Users.Add(new StaffUser { Id = SecondDoctor, Issuer = "test-idp", Subject = SecondDoctor.ToString(), DisplayName = "Dr. S. Menon (synthetic)" });
            await identity.SaveChangesAsync();
        }
        await using var patients = sql.Patients(Tenant);
        foreach (var (id, name, mrn) in new[] { (Patient, "Meera Iyer (synthetic)", "000001"), (SecondPatient, "Deepak Nair (synthetic)", "000002") })
            patients.Patients.Add(new Patient
            {
                Id = id, TenantId = Tenant, FacilityId = Facility, Mrn = mrn, CountryCode = "IN", FullName = name, DateOfBirth = new(1972, 3, 12),
                SexAtBirth = SexAtBirth.Female, Mobile = "+919000000101", Status = RegistrationStatus.Incomplete, RegisteredAt = MondayMorning, ChangedAt = MondayMorning,
            });
        await patients.SaveChangesAsync();
    }

    /// <summary>Adds <paramref name="count"/> receptionists at the facility: one desk per parallel caller.</summary>
    public async Task<List<Guid>> AddReceptionistsAsync(int count)
    {
        var users = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await using var db = sql.Context(Tenant);
        db.Memberships.AddRange(users.Select(u => new Membership
        {
            Id = Guid.NewGuid(), TenantId = Tenant, UserId = u, FacilityId = Facility, Role = Role.Receptionist, Status = MembershipStatus.Active,
        }));
        await db.SaveChangesAsync();
        return users;
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    IServiceProvider Scope(Guid user, Guid? inTenant)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICurrentTenant>(new SqlServerFixture.FixedTenant(inTenant ?? Tenant))
            .AddSingleton<ICurrentUser>(new FixedUser(user))
            .AddSingleton<TimeProvider>(Clock)
            .AddTenancyModule(ConnectionString).AddIdentityModule(ConnectionString)
            .AddPatientsModule(ConnectionString).AddSchedulingModule(ConnectionString);
        services.Configure<SchedulingOptions>(o => (o.SlotHold, o.LockTimeout) = (Options.SlotHold, Options.LockTimeout));
        var provider = services.BuildServiceProvider();
        providers.Add(provider);
        return provider.CreateScope().ServiceProvider;
    }

    public IScheduleService Schedule(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<IScheduleService>();

    public IBookingService Booking(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<IBookingService>();

    /// <summary>Dr. Rao: Mon–Fri 09:00–13:00 and 17:00–20:00 IST, 20-minute slots, lunch 12:00–13:00 on Monday, new consultation 40 min.</summary>
    public Task<TemplateView> DoctorTemplateAsync() => Schedule(Admin).SaveTemplateAsync(DoctorTemplate());

    public SaveTemplate DoctorTemplate(int slotMinutes = 20) => new()
    {
        FacilityId = Facility, CalendarId = Doctor, CalendarKind = Scheduling.Domain.CalendarKind.Doctor, SlotMinutes = slotMinutes,
        Hours = ScheduleRulesTests.MorningAndEvening().Hours,
        Breaks = [new Scheduling.Domain.ScheduleBreak { Weekday = DayOfWeek.Monday, Start = new(12, 0), End = new(13, 0), Label = "Lunch break" }],
        VisitLengths = [new Scheduling.Domain.VisitLength { VisitType = Scheduling.Domain.VisitType.NewConsultation, Minutes = 2 * slotMinutes }],
    };

    /// <summary>The instant of a wall-clock time on Monday in IST (UTC+05:30).</summary>
    public static DateTimeOffset At(int hour, int minute = 0) => new DateTimeOffset(2026, 10, 19, hour, minute, 0, TimeSpan.FromHours(5.5)).ToUniversalTime();

    /// <summary>Disposes the service providers built for the test.</summary>
    public async Task CloseAsync()
    {
        foreach (var p in providers) await p.DisposeAsync();
    }
}
