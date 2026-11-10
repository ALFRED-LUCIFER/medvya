using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-072 follow-up on real PostgreSQL: a patient registered at location A (Adyar) is found, booked, checked in and treated at location B
/// (Velachery) of the same organisation. The visit is at B and is what relates B's clinicians to the patient; registration at A relates nobody at B.
/// Another organisation's patient stays invisible. Synthetic data only.
/// </summary>
public sealed class CrossLocationBookingTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Guid receptionB = Guid.NewGuid(), doctorB = Guid.NewGuid(), colleagueB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using (var db = sql.Context(h.Tenant))
        {
            foreach (var (user, role) in new[] { (receptionB, Role.Receptionist), (doctorB, Role.SoloDoctor), (colleagueB, Role.HospitalDoctor) })
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = user, FacilityId = h.OtherFacility, Role = role, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        await h.Schedule(h.Elsewhere).SaveTemplateAsync(h.DoctorTemplate() with { FacilityId = h.OtherFacility, CalendarId = doctorB });
        h.Clock.Advance(SchedulingHarness.At(9) - h.Clock.GetUtcNow() - TimeSpan.FromMinutes(30)); // Monday 08:30 IST at both locations
    }

    public Task DisposeAsync() => h.CloseAsync();

    async Task<AppointmentView> BookAtBAsync(Guid patient, int hour = 10)
    {
        var booking = h.Booking(receptionB);
        var hold = await booking.HoldAsync(new HoldSlot { FacilityId = h.OtherFacility, CalendarId = doctorB, Start = At(hour), VisitType = VisitType.FollowUp });
        return await booking.BookAsync(new BookAppointment
        {
            HoldId = hold.Id, PatientId = patient, ReasonCategory = ReasonCategory.General, Channel = BookingChannel.Phone,
        });
    }

    Task<Access> OpenAtBAsync(Guid user, Guid patient) =>
        h.Get<IClinicalAccess>(user).RequireAsync(patient, h.OtherFacility, Capability.Patient360Chart, [Access.Read, Access.Limited, Access.Full, Access.Sign]);

    async Task<ClinicalAccessDeniedException> RefusedAtBAsync(Guid user, Guid patient) =>
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => OpenAtBAsync(user, patient));

    [Fact]
    public async Task ADeskAtLocationBFindsBooksChecksInAndSeesAPatientRegisteredAtA()
    {
        Assert.Equal("Meera Iyer (synthetic)", (await h.Booking(receptionB).FindPatientAsync(h.OtherFacility, h.Patient))!.FullName);

        var visit = await BookAtBAsync(h.Patient);
        Assert.Equal((h.OtherFacility, doctorB, h.Patient), (visit.FacilityId, visit.CalendarId, visit.PatientId));
        await using (var db = sql.Patients(h.Tenant)) Assert.Equal(h.Facility, (await db.Patients.SingleAsync(p => p.Id == h.Patient)).FacilityId); // the chart stays where it was registered

        h.Clock.Advance(TimeSpan.FromHours(1.4)); // 09:54 IST: check-in opens before the visit
        await h.FrontDesk(receptionB).CheckInAsync(visit.Id, new CheckIn());
        var card = Assert.Single((await h.FrontDesk(receptionB).GetBoardAsync(h.OtherFacility)).Cards);
        Assert.Equal((h.Patient, "Meera Iyer (synthetic)", AppointmentState.CheckedIn), (card.PatientId, card.PatientName, card.State));
        Assert.Empty((await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards); // location A's board has none of it
    }

    [Fact]
    public async Task OnlyTheVisitAtBRelatesBsCliniciansToThePatientNotRegistrationAtAOrAVisitAtA()
    {
        // Registered at A, never seen at B: a doctor at B has no relationship, and may ask.
        var before = await RefusedAtBAsync(doctorB, h.Patient);
        Assert.True(before.CanRequest);
        Assert.True((await RefusedAtBAsync(colleagueB, h.Patient)).CanRequest);

        // A visit at A (booked by Adyar's desk) relates nobody at B.
        await h.BookAsync(9, patient: h.Patient);
        await RefusedAtBAsync(doctorB, h.Patient);

        // A staff member of A has no role at B, so the chart at B is not theirs to ask for.
        Assert.False((await RefusedAtBAsync(h.Doctor, h.Patient)).CanRequest);

        // The visit at B relates B's clinicians, and a visit for one patient relates nobody to another.
        var visit = await BookAtBAsync(h.Patient);
        Assert.Equal(Access.Full, await OpenAtBAsync(doctorB, h.Patient));
        Assert.Equal(Access.Full, await OpenAtBAsync(colleagueB, h.Patient)); // the clinic's default: every clinician of the facility treats its visits
        await RefusedAtBAsync(doctorB, h.SecondPatient);

        // A cancelled visit never happened.
        await h.Booking(receptionB).CancelAsync(visit.Id, new CancelAppointment { Reason = CancelReason.PatientRequest });
        await RefusedAtBAsync(doctorB, h.Patient);
    }

    [Fact]
    public async Task UnderAssignedOnlyTheVisitRelatesOnlyTheDoctorWhoseCalendarItIs()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await BookAtBAsync(h.Patient);
        Assert.Equal(Access.Full, await OpenAtBAsync(doctorB, h.Patient));
        await RefusedAtBAsync(colleagueB, h.Patient);
    }

    [Fact]
    public async Task AnotherOrganisationsPatientIsInvisibleEvenWithTheEfFilterOffBecauseOfRowLevelSecurity()
    {
        await h.SeedOtherOrganisationPatientAsync();
        Assert.Null(await h.Booking(receptionB).FindPatientAsync(h.OtherFacility, h.ForeignPatient));
        await Assert.ThrowsAsync<SchedulingValidationException>(() => BookAtBAsync(h.ForeignPatient));

        await using var mine = sql.Patients(h.Tenant);
        Assert.Empty(await mine.Patients.IgnoreQueryFilters().Where(p => p.Id == h.ForeignPatient).ToListAsync());
        await using var theirs = sql.Patients(Guid.NewGuid());
        Assert.Empty(await theirs.Patients.IgnoreQueryFilters().Where(p => p.Id == h.Patient || p.Id == h.OtherFacilityPatient).ToListAsync());
    }
}
