using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-043 AC3 on real PostgreSQL: the follow-up the doctor picks in the plan is a booking to make on the front desk's waitlist for that patient
/// (same doctor, a slot from the due day), changed or withdrawn by the doctor, refused once booked; only a doctor with a care relationship sets it,
/// and another clinic never sees it. Synthetic data only.
/// </summary>
public sealed class FollowUpTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await other.SeedAsync();
        await h.DoctorTemplateAsync();
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

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    IFollowUps FollowUps(Guid user) => h.Get<IFollowUps>(user);

    async Task<Guid> InConsultAsync()
    {
        var visit = await h.BookAsync(10);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        return (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
    }

    async Task<WaitlistView?> DeskSeesAsync() => (await h.Get<IWaitlistService>(h.Reception).ListAsync(h.Facility)).Entries.SingleOrDefault(e => e.PatientId == h.Patient);

    [Fact]
    public async Task TheFollowUpChoiceIsABookingToMakeOnTheFrontDesksListForThatPatient()
    {
        var encounter = await InConsultAsync();
        Assert.Equal(new FollowUpView(null, null, null), await FollowUps(h.Doctor).GetAsync(encounter));

        var set = (await FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp("P2W")))!;
        var today = new DateOnly(2026, 10, 19); // Monday, in the clinic's zone
        Assert.Equal(new FollowUpView("P2W", today.AddDays(14), "Waiting"), set);

        var entry = (await DeskSeesAsync())!;
        Assert.Equal(("P2W", today.AddDays(14), h.Doctor, VisitType.FollowUp, WaitlistState.Waiting), (entry.FollowUp, entry.DueOn, entry.CalendarId, entry.VisitType, entry.State));
        Assert.Equal(At(9).AddDays(14), entry.Match!.Start); // the first free slot of the due day, never an earlier one

        // A changed mind moves the same entry; none takes it off the list.
        await FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp("P1M"));
        entry = (await DeskSeesAsync())!;
        Assert.Equal(("P1M", new DateOnly(2026, 11, 19)), (entry.FollowUp, entry.DueOn));
        Assert.Equal(new FollowUpView(null, null, null), await FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp(null)));
        Assert.Null(await DeskSeesAsync());
        await FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp("P3M")); // set again: the entry comes back
        Assert.Equal(new DateOnly(2027, 1, 19), (await DeskSeesAsync())!.DueOn);
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(1, await db.Waitlist.CountAsync(w => w.PatientId == h.Patient));

        // Another clinic sees nothing of it (EF filter and row-level security).
        await using var foreign = sql.Scheduling(other.Tenant);
        Assert.Equal(0, await foreign.Waitlist.IgnoreQueryFilters().CountAsync(w => w.PatientId == h.Patient));
        Assert.Null(await other.Get<IFollowUps>(other.Doctor).GetAsync(encounter));

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "encounter.followup.set").ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, (Guid?)encounter), (r.Outcome, r.PatientId, r.ResourceId)));
    }

    [Fact]
    public async Task ABookedFollowUpCannotBeChangedAndAnUnknownPeriodOrAnEndedConsultationIsRefused()
    {
        var encounter = await InConsultAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(h.Doctor).PutAsJsonAsync(new Uri($"/v1/encounters/{encounter}/follow-up", UriKind.Relative), new { period = "P5Y" })).StatusCode);

        await FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp("P2W"));
        var entry = (await DeskSeesAsync())!;
        var desk = h.Get<IWaitlistService>(h.Reception);
        await desk.OfferAsync(entry.Id, new OfferSlot { CalendarId = entry.Match!.CalendarId, Start = entry.Match.Start });
        await desk.AcceptAsync(entry.Id);
        Assert.Equal("Booked", (await FollowUps(h.Doctor).GetAsync(encounter))!.State);
        using var booked = await Client(h.Doctor).PutAsJsonAsync(new Uri($"/v1/encounters/{encounter}/follow-up", UriKind.Relative), new { period = "P1M" });
        Assert.Equal((HttpStatusCode.Conflict, "follow_up_booked"), (booked.StatusCode, (await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()));

        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
        Assert.Equal("ended", (await Assert.ThrowsAsync<VisitTransitionException>(() => FollowUps(h.Doctor).SetAsync(encounter, new SetFollowUp(null)))).Code);
    }

    [Fact]
    public async Task OnlyADoctorWithACareRelationshipSetsTheFollowUp()
    {
        var encounter = await InConsultAsync();
        foreach (var user in new[] { h.Reception, h.Nurse })
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).PutAsJsonAsync(new Uri($"/v1/encounters/{encounter}/follow-up", UriKind.Relative), new { period = "P2W" })).StatusCode);
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => FollowUps(h.Nurse).SetAsync(encounter, new SetFollowUp("P2W")));
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => FollowUps(h.SecondDoctor).SetAsync(encounter, new SetFollowUp("P2W")));
        Assert.Null(await DeskSeesAsync());
    }
}
