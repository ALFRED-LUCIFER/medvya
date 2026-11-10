using Medvya.Audit.Infrastructure;
using Medvya.Encounters.Infrastructure;
using Medvya.Prescribing.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
internal sealed class SchedulingHarness(PostgresFixture sql)
{
    /// <summary>Monday 19 Oct 2026, 08:00 IST: before the first slot of the day.</summary>
    public static readonly DateTimeOffset MondayMorning = new(2026, 10, 19, 2, 30, 0, TimeSpan.Zero);
    public static readonly DateOnly Monday = new(2026, 10, 19);

    public readonly Guid Tenant = Guid.NewGuid(), Facility = Guid.NewGuid(), OtherFacility = Guid.NewGuid();
    public readonly Guid Doctor = Guid.NewGuid(), SecondDoctor = Guid.NewGuid(), Reception = Guid.NewGuid(), Admin = Guid.NewGuid(),
        Nurse = Guid.NewGuid(), PortalUser = Guid.NewGuid(), Elsewhere = Guid.NewGuid();
    /// <summary>Patient and SecondPatient are registered at Facility (Adyar); OtherFacilityPatient at OtherFacility (Velachery).</summary>
    public readonly Guid CareCoordinator = Guid.NewGuid(), HospitalIt = Guid.NewGuid();
    /// <summary>The seeded member of each role at Facility (access is read from the membership, never from a claim).</summary>
    public Dictionary<Role, Guid> ByRole => new()
    {
        [Role.SoloDoctor] = Doctor, [Role.HospitalDoctor] = SecondDoctor, [Role.Receptionist] = Reception, [Role.PracticeAdmin] = Admin,
        [Role.Nurse] = Nurse, [Role.Patient] = PortalUser, [Role.CareCoordinator] = CareCoordinator, [Role.HospitalIt] = HospitalIt,
    };
    public readonly Guid Patient = Guid.NewGuid(), SecondPatient = Guid.NewGuid(), OtherFacilityPatient = Guid.NewGuid(), ForeignPatient = Guid.NewGuid();
    /// <summary>The tenant's country; "AE" gives a UAE clinic (its patients and clock stay Indian: only the tenant's rules differ).</summary>
    public string Country { get; init; } = "IN";
    public ManualClock Clock { get; } = new(MondayMorning);
    public InMemoryLogos Logos { get; } = new();
    public SchedulingOptions Options { get; } = new();
    /// <summary>A pool big enough for a hundred desks booking at once (each request also opens Tenancy and Patients connections).</summary>
    public Medvya.Patients.Infrastructure.Persistence.NationalIdColumns NationalIds => sql.NationalIds;

    public string ConnectionString => new Npgsql.NpgsqlConnectionStringBuilder(sql.ConnectionString) { MaxPoolSize = 400 }.ConnectionString;
    readonly System.Collections.Concurrent.ConcurrentBag<ServiceProvider> providers = []; // race tests build services from many threads at once

    public async Task SeedAsync()
    {
        Medvya.Tenancy.Infrastructure.StarterPlan.RaiseCapForTests(Tenant); // the cast has one member of every role, more than the starter plan (PlanCapTests covers the cap)
        await using (var db = sql.Context(Tenant))
        {
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = Tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = Country, ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = Facility, TenantId = Tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = OtherFacility, TenantId = Tenant, Name = "Velachery (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (user, role, at) in new[]
            {
                (Doctor, Role.SoloDoctor, Facility), (SecondDoctor, Role.HospitalDoctor, Facility), (Reception, Role.Receptionist, Facility),
                (Admin, Role.PracticeAdmin, Facility), (Nurse, Role.Nurse, Facility), (PortalUser, Role.Patient, Facility), (Elsewhere, Role.PracticeAdmin, OtherFacility),
                (CareCoordinator, Role.CareCoordinator, Facility), (HospitalIt, Role.HospitalIt, Facility),
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
        foreach (var (id, name, mrn, at) in new[]
        {
            (Patient, "Meera Iyer (synthetic)", "000001", Facility), (SecondPatient, "Deepak Nair (synthetic)", "000002", Facility),
            (OtherFacilityPatient, "Lakshmi Pillai (synthetic)", "000003", OtherFacility),
        })
            patients.Patients.Add(new Patient
            {
                Id = id, TenantId = Tenant, FacilityId = at, Mrn = mrn, CountryCode = "IN", FullName = name, DateOfBirth = new(1972, 3, 12),
                SexAtBirth = SexAtBirth.Female, Mobile = "+919000000101", Status = RegistrationStatus.Incomplete, RegisteredAt = MondayMorning, ChangedAt = MondayMorning,
            });
        await patients.SaveChangesAsync();
    }

    /// <summary>One patient of a different organisation (tenant): nothing of this one may reach it.</summary>
    public async Task SeedOtherOrganisationPatientAsync()
    {
        var foreignTenant = Guid.NewGuid();
        await using var patients = sql.Patients(foreignTenant);
        patients.Patients.Add(new Patient
        {
            Id = ForeignPatient, TenantId = foreignTenant, FacilityId = Guid.NewGuid(), Mrn = "000001", CountryCode = "IN", FullName = "Foreign Patient (synthetic)", DateOfBirth = new(1972, 3, 12),
            SexAtBirth = SexAtBirth.Female, Mobile = "+919000000102", Status = RegistrationStatus.Incomplete, RegisteredAt = MondayMorning, ChangedAt = MondayMorning,
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

    IServiceProvider Scope(Guid user, Guid? inTenant, IInterceptor? interceptor = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICurrentTenant>(new PostgresFixture.FixedTenant(inTenant ?? Tenant))
            .AddSingleton<ICurrentUser>(new FixedUser(user))
            .AddSingleton<TimeProvider>(Clock)
            .AddSingleton<ICurrentFacility>(new PatientRegistryTests.FixedFacility(Facility))
            .AddSingleton<Medvya.Tenancy.Application.ILogoStore>(Logos) // before the module, which only adds a store when none is registered
            .AddSingleton(sql.NationalIds) // the fixture's national-ID setup (this harness has no host configuration)
            .AddAuditModule(ConnectionString).AddTenancyModule(ConnectionString).AddIdentityModule(ConnectionString)
            .AddPatientsModule(ConnectionString).AddSchedulingModule(ConnectionString).AddEncountersModule(ConnectionString).AddPrescribingModule(ConnectionString);
        if (interceptor is not null) services.ConfigureDbContext<Medvya.Scheduling.Infrastructure.Persistence.SchedulingDbContext>(o => o.AddInterceptors(interceptor));
        services.Configure<SchedulingOptions>(o => (o.SlotHold, o.LockTimeout) = (Options.SlotHold, Options.LockTimeout));
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        providers.Add(provider);
        return provider.CreateScope().ServiceProvider;
    }

    /// <summary>Any service of the modules above as <paramref name="user"/> at Facility, with extra registrations if a test needs them.</summary>
    public T Get<T>(Guid user, Action<IServiceCollection>? configure = null) where T : notnull => Scope(user, null, configure: configure).GetRequiredService<T>();

    public IScheduleService Schedule(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<IScheduleService>();

    public Medvya.Tenancy.Application.IPracticeSettings Settings(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<Medvya.Tenancy.Application.IPracticeSettings>();

    public IBookingService Booking(Guid user, Guid? inTenant = null, IInterceptor? interceptor = null) => Scope(user, inTenant, interceptor).GetRequiredService<IBookingService>();

    public IScheduleExceptionService Adjustments(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<IScheduleExceptionService>();

    public IFrontDeskService FrontDesk(Guid user, Guid? inTenant = null) => Scope(user, inTenant).GetRequiredService<IFrontDeskService>();

    /// <summary>Reception books Dr. Rao for <paramref name="patient"/> at a Monday wall-clock time, or at <paramref name="start"/>.</summary>
    public async Task<AppointmentView> BookAsync(int hour, int minute = 0, Guid? patient = null, DateTimeOffset? start = null)
    {
        var booking = Booking(Reception);
        var hold = await booking.HoldAsync(new HoldSlot { FacilityId = Facility, CalendarId = Doctor, Start = start ?? At(hour, minute), VisitType = Scheduling.Domain.VisitType.FollowUp });
        return await booking.BookAsync(new BookAppointment
        {
            HoldId = hold.Id, PatientId = patient ?? Patient, ReasonCategory = Scheduling.Domain.ReasonCategory.ChronicReview,
            ReasonText = "Synthetic: chest tightness on stairs", Channel = Scheduling.Domain.BookingChannel.Phone,
        });
    }

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

/// <summary>Logo storage for tests: what was stored, by key.</summary>
internal sealed class InMemoryLogos : Medvya.Tenancy.Application.ILogoStore
{
    public Dictionary<string, (byte[] Content, string ContentType)> Stored { get; } = [];
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        Stored[key] = (content, contentType);
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Stored.Remove(key);
        return Task.CompletedTask;
    }
    public Task<Uri> LinkForAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default) => Task.FromResult(new Uri($"https://blob.test/{key}?se={lifetime.TotalSeconds}"));
}
