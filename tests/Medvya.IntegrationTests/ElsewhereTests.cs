using System.Net;
using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// #1365 F-061 over HTTP on real PostgreSQL: a doctor working at Adyar is told how many of their own patients wait at Velachery, where they also
/// hold a membership. Counts only, of their own calendar only, and only for locations their own membership opens. Synthetic data only.
/// </summary>
public sealed class ElsewhereTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Guid receptionB = Guid.NewGuid(), doctorB = Guid.NewGuid(), raoAtB = Guid.NewGuid();
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using (var db = sql.Context(h.Tenant))
        {
            // Dr. Rao works at both locations; Velachery has its own desk and its own doctor.
            db.Memberships.Add(new Membership { Id = raoAtB, TenantId = h.Tenant, UserId = h.Doctor, FacilityId = h.OtherFacility, Role = Role.HospitalDoctor, Status = MembershipStatus.Active });
            foreach (var (user, role) in new[] { (receptionB, Role.Receptionist), (doctorB, Role.SoloDoctor) })
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = user, FacilityId = h.OtherFacility, Role = role, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        foreach (var doctor in new[] { h.Doctor, doctorB })
            await h.Schedule(h.Elsewhere).SaveTemplateAsync(h.DoctorTemplate() with { FacilityId = h.OtherFacility, CalendarId = doctor });
        h.Clock.Advance(At(9) - h.Clock.GetUtcNow() - TimeSpan.FromMinutes(30)); // Monday 08:30 IST at both locations
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    async Task<AppointmentView> BookAtBAsync(Guid doctor, Guid patient, int hour)
    {
        var booking = h.Booking(receptionB);
        var hold = await booking.HoldAsync(new HoldSlot { FacilityId = h.OtherFacility, CalendarId = doctor, Start = At(hour), VisitType = VisitType.FollowUp });
        return await booking.BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = patient, ReasonCategory = ReasonCategory.General, Channel = BookingChannel.Phone });
    }

    async Task<(HttpStatusCode Status, JsonElement Json, string Body)> ElsewhereAsync(Guid user, Guid? at = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", (at ?? h.Facility).ToString());
        using var r = await client.GetAsync(new Uri($"/v1/front-desk/elsewhere?facilityId={at ?? h.Facility}", UriKind.Relative));
        var body = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, r.IsSuccessStatusCode ? JsonDocument.Parse(body).RootElement.Clone() : default, body);
    }

    // SAFETY mutation checks: drop "a.CalendarId == me" in FrontDeskService.GetElsewhereAsync and Velachery's own doctor's visit is counted for Dr. Rao;
    // drop the AllowsAsync line and a membership without board access is counted (the Role.Patient assertion below).
    [Fact]
    public async Task ADoctorAtAdyarIsToldTheirOwnCountsAtVelacheryAndNothingElse()
    {
        // At Velachery: two of Dr. Rao's patients (one arrives), and one of Velachery's own doctor (arrives too).
        var (arrives, later, others) = (await BookAtBAsync(h.Doctor, h.Patient, 10), await BookAtBAsync(h.Doctor, h.SecondPatient, 11), await BookAtBAsync(doctorB, h.OtherFacilityPatient, 10));
        h.Clock.Advance(TimeSpan.FromHours(1.4)); // 09:54 IST: check-in is open
        await h.FrontDesk(receptionB).CheckInAsync(arrives.Id, new CheckIn());
        await h.FrontDesk(receptionB).CheckInAsync(others.Id, new CheckIn());

        var (status, json, body) = await ElsewhereAsync(h.Doctor);
        Assert.Equal(HttpStatusCode.OK, status);
        var row = Assert.Single(json.EnumerateArray());
        Assert.Equal(["facilityId", "waiting", "today"], row.EnumerateObject().Select(p => p.Name));
        Assert.Equal((h.OtherFacility, 1, 2), (row.GetProperty("facilityId").GetGuid(), row.GetProperty("waiting").GetInt32(), row.GetProperty("today").GetInt32()));
        // Counts only: no patient, visit or colleague.
        foreach (var leak in new[] { "Meera", "Deepak", "Lakshmi", "Rao", arrives.Id.ToString(), later.Id.ToString(), h.Patient.ToString(), doctorB.ToString() })
            Assert.DoesNotContain(leak, body, StringComparison.Ordinal);

        // Working at Velachery, the same doctor is told about Adyar instead: nothing there today.
        Assert.Empty((await ElsewhereAsync(h.Doctor, h.OtherFacility)).Json.EnumerateArray());

        // Colleagues with no membership at Velachery learn nothing about it, whatever is waiting there.
        Assert.Empty((await ElsewhereAsync(h.SecondDoctor)).Json.EnumerateArray());
        Assert.Empty((await ElsewhereAsync(h.Reception)).Json.EnumerateArray());
        // No board access where the caller is working: refused, as the board is.
        Assert.Equal(HttpStatusCode.Forbidden, (await ElsewhereAsync(h.PortalUser)).Status);
        // Another location's staff naming Adyar as their own.
        Assert.Equal(HttpStatusCode.Forbidden, (await ElsewhereAsync(receptionB)).Status);

        // A membership at Velachery that does not open its board, or is suspended, counts nothing.
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == raoAtB).ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, Role.Patient));
        Assert.Empty((await ElsewhereAsync(h.Doctor)).Json.EnumerateArray());
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == raoAtB).ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, Role.HospitalDoctor).SetProperty(m => m.Status, MembershipStatus.Suspended));
        Assert.Empty((await ElsewhereAsync(h.Doctor)).Json.EnumerateArray());
    }

    [Fact]
    public async Task AnotherOrganisationsLocationIsNeverCounted()
    {
        // The same person also works at another organisation, where a patient is booked with them today.
        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            await other.DoctorTemplateAsync();
            await using (var db = sql.Context(other.Tenant))
            {
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = other.Tenant, UserId = h.Doctor, FacilityId = other.Facility, Role = Role.HospitalDoctor, Status = MembershipStatus.Active });
                await db.SaveChangesAsync();
            }
            await other.Schedule(other.Admin).SaveTemplateAsync(other.DoctorTemplate() with { CalendarId = h.Doctor });
            var booking = other.Booking(other.Reception);
            var hold = await booking.HoldAsync(new HoldSlot { FacilityId = other.Facility, CalendarId = h.Doctor, Start = At(10), VisitType = VisitType.FollowUp });
            await booking.BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = other.Patient, ReasonCategory = ReasonCategory.General, Channel = BookingChannel.Phone });

            Assert.Empty((await ElsewhereAsync(h.Doctor)).Json.EnumerateArray());
        }
        finally
        {
            await other.CloseAsync();
        }
    }
}
