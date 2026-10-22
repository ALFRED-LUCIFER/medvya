using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-011 through HTTP on real SQL Server: sign-in required, the role check at the facility, field/code errors, the JSON
/// contract the clinic app reads, and one audit row per request.
/// </summary>
public sealed class ScheduleEndpointTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), doctor = Guid.NewGuid(), reception = Guid.NewGuid(), admin = Guid.NewGuid();
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (user, role) in new[] { (doctor, Role.SoloDoctor), (reception, Role.Receptionist), (admin, Role.PracticeAdmin) })
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, FacilityId = facility, Role = role, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedInFromHeader(tenant))));
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    /// <summary>The synthetic user named in X-Test-User, in the test tenant. No header = not signed in.</summary>
    // shortcut: sets the tenant claim directly because sign-in does not issue one before the facility picker (MDV-P0-012).
    sealed class SignedInFromHeader(Guid tenant) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid? user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is { } id) client.DefaultRequestHeaders.Add("X-Test-User", id.ToString());
        return client;
    }

    async Task<List<string>> AuditActions()
    {
        await using var db = sql.Audit(tenant);
        return await db.Events.OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
    }

    // The body the clinic app sends: enum names as strings, weekday numbers (0 = Sunday), wall-clock times.
    object Template(int slotMinutes) => new
    {
        facilityId = facility, calendarId = doctor, calendarKind = "Doctor", slotMinutes,
        hours = new[] { new { weekday = 1, start = "09:00:00", end = "13:00:00", room = "Room 3" } },
        breaks = new[] { new { weekday = 1, start = "12:00:00", end = "13:00:00", label = "Lunch break" } },
        visitLengths = new[] { new { visitType = "NewConsultation", minutes = 2 * slotMinutes } },
    };

    [Fact]
    public async Task AdminSetsATemplateAndReceptionSeesTheDayInFacilityTime()
    {
        using var saved = await Client(admin).PutAsJsonAsync("/v1/schedule/templates", Template(20));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var day = await Client(reception).GetAsync($"/v1/schedule?facilityId={facility}&date=2026-10-19");
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);
        var json = JsonDocument.Parse(await day.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(("Asia/Kolkata", "2026-10-19"), (json.GetProperty("timeZone").GetString(), json.GetProperty("date").GetString()));
        var column = json.GetProperty("columns")[0];
        Assert.Equal(("Doctor", 9), (column.GetProperty("calendarKind").GetString(), column.GetProperty("slots").GetArrayLength()));
        Assert.Equal(DateTimeOffset.Parse("2026-10-19T03:30:00Z", System.Globalization.CultureInfo.InvariantCulture), column.GetProperty("slots")[0].GetProperty("start").GetDateTimeOffset());
        Assert.Equal("Break", column.GetProperty("unavailable")[0].GetProperty("reason").GetString());

        Assert.Equal(["schedule.template.update", "schedule.read"], await AuditActions());
    }

    [Fact]
    public async Task ReceptionCannotChangeTemplatesAndTheAttemptIsAudited()
    {
        using var response = await Client(reception).PutAsJsonAsync("/v1/schedule/templates", Template(20));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(["schedule.template.update"], await AuditActions());
    }

    [Fact]
    public async Task ReceptionHoldsAndBooksWhileASecondDeskIsTurnedAway()
    {
        var patient = Guid.NewGuid();
        await using (var patients = sql.Patients(tenant))
        {
            patients.Patients.Add(new Patients.Domain.Patient
            {
                Id = patient, TenantId = tenant, FacilityId = facility, Mrn = "000001", CountryCode = "IN", FullName = "Meera Iyer (synthetic)",
                DateOfBirth = new(1972, 3, 12), SexAtBirth = Patients.Domain.SexAtBirth.Female, Mobile = "+919000000101", RegisteredAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
            });
            await patients.SaveChangesAsync();
        }
        (await Client(admin).PutAsJsonAsync("/v1/schedule/templates", Template(20))).EnsureSuccessStatusCode();
        // The host runs on the real clock: book 09:00 IST next Monday.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        var start = new DateTimeOffset(monday.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(5.5));
        var slot = new { facilityId = facility, calendarId = doctor, start, visitType = "FollowUp" };

        using var held = await Client(reception).PostAsJsonAsync("/v1/slot-holds", slot);
        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
        var holdId = JsonDocument.Parse(await held.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var second = await Client(doctor).PostAsJsonAsync("/v1/slot-holds", slot);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("being_booked", JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        using var lookup = await Client(reception).GetAsync($"/v1/schedule/patients/{patient}?facilityId={facility}");
        Assert.Contains("Meera Iyer (synthetic)", await lookup.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var booked = await Client(reception).PostAsJsonAsync("/v1/appointments",
            new { holdId, patientId = patient, reasonCategory = "General", channel = "Phone", reasonText = "Synthetic: feeling dizzy" });
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);
        var body = await booked.Content.ReadAsStringAsync();
        Assert.Contains("\"state\":\"Booked\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dizzy", body, StringComparison.Ordinal); // the free-text reason never comes back

        Assert.Equal(["schedule.template.update", "slot.hold", "slot.hold", "patient.lookup", "appointment.book"], await AuditActions());
        await using var audit = sql.Audit(tenant);
        var lookup = await audit.Events.SingleAsync(e => e.Action == "patient.lookup");
        Assert.Equal((patient, "registration"), (lookup.PatientId!.Value, lookup.Purpose)); // the purpose front-desk search (MDV-P1-018) uses
    }

    [Fact]
    public async Task InvalidInputGetsFieldCodesAndNoSignInGets401()
    {
        using var invalid = await Client(admin).PutAsJsonAsync("/v1/schedule/templates", Template(7));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Equal("invalid", errors.GetProperty("slotMinutes")[0].GetString());

        using var noFacility = await Client(reception).GetAsync("/v1/schedule");
        Assert.Equal(HttpStatusCode.BadRequest, noFacility.StatusCode);

        using var anonymous = await Client(null).GetAsync($"/v1/schedule?facilityId={facility}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
