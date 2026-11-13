using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-102 on real PostgreSQL: the doctor's follow-up reaches the front desk as "Follow-up due" on the board's Done card and the desk record, with the
/// date and the doctor only; booking a visit from the due day books it, dismissing it takes a reason into the audit row; another clinic sees none of it.
/// Synthetic data only.
/// </summary>
public sealed class FollowUpDueTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateOnly Due = Monday.AddDays(14);

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
        client.DefaultRequestHeaders.Add("X-Medvya-Csrf", "1");
        return client;
    }

    /// <summary>Booked, checked in, seen: the doctor sets a follow-up of 2 weeks and ends the visit, which goes to Done (billing is off).</summary>
    async Task EndedWithFollowUpAsync()
    {
        var visit = await h.BookAsync(10);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        var encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
        await h.Get<IFollowUps>(h.Doctor).SetAsync(encounter, new SetFollowUp("P2W"));
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
    }

    async Task<BoardCard> DoneCardAsync() => (await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards.Single(c => c.PatientId == h.Patient && c.State == AppointmentState.Done);

    async Task<JsonElement> DeskRecordAsync()
    {
        using var response = await Client(h.Reception).GetAsync(new Uri($"/v1/schedule/patients/{h.Patient}/visits?facilityId={h.Facility}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("followUp");
    }

    [Fact]
    public async Task TheDeskSeesTheFollowUpDueOnTheDoneCardAndTheRecordWithTheDateAndDoctorOnly()
    {
        await EndedWithFollowUpAsync();
        var due = (await DoneCardAsync()).FollowUp!;
        Assert.Equal((Due, (Guid?)h.Doctor), (due.DueOn, due.CalendarId));
        Assert.False(string.IsNullOrEmpty(due.CalendarName));

        // AC4: the date, the doctor and the entry to act on, nothing else: no period, reason, note or clinical text, in the contract or on the wire.
        Assert.Equal(["CalendarId", "CalendarName", "DueOn", "Id"], typeof(FollowUpDue).GetProperties().Select(p => p.Name).Order());
        var record = await DeskRecordAsync();
        Assert.Equal(["calendarId", "calendarName", "dueOn", "id"], record.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal((due.Id.ToString(), "2026-11-02"), (record.GetProperty("id").GetString(), record.GetProperty("dueOn").GetString()));

        // Another clinic sees none of it (EF filter and row-level security).
        Assert.Null(await other.Booking(other.Reception).FollowUpDueAsync(other.Facility, h.Patient));
        await using var foreign = sql.Scheduling(other.Tenant);
        Assert.Equal(0, await foreign.Waitlist.IgnoreQueryFilters().CountAsync(w => w.PatientId == h.Patient));
    }

    [Fact]
    public async Task AVisitBookedFromTheDueDayBooksTheFollowUpAndAnEarlierOneDoesNot()
    {
        await EndedWithFollowUpAsync();
        await h.BookAsync(10, start: At(10).AddDays(11)); // the Friday before: another matter, the line stays
        Assert.NotNull((await DoneCardAsync()).FollowUp);

        var booked = await h.BookAsync(10, start: At(10).AddDays(14));
        Assert.Null((await DoneCardAsync()).FollowUp);
        Assert.Equal(JsonValueKind.Null, (await DeskRecordAsync()).ValueKind);
        await using var db = sql.Scheduling(h.Tenant);
        var entry = await db.Waitlist.SingleAsync(w => w.PatientId == h.Patient);
        Assert.Equal((WaitlistState.Booked, (Guid?)booked.Id), (entry.State, entry.AppointmentId));
    }

    [Fact]
    public async Task DismissingTheFollowUpTakesAReasonIntoTheAuditRowAndClearsTheLine()
    {
        await EndedWithFollowUpAsync();
        var due = (await DoneCardAsync()).FollowUp!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(h.Reception).DeleteAsync(new Uri($"/v1/waitlist/{due.Id}?reason=Lost", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(h.Reception).DeleteAsync(new Uri($"/v1/waitlist/{due.Id}?reason=99", UriKind.Relative))).StatusCode);
        Assert.NotNull((await DoneCardAsync()).FollowUp);

        Assert.Equal(HttpStatusCode.NoContent, (await Client(h.Reception).DeleteAsync(new Uri($"/v1/waitlist/{due.Id}?reason=PatientDeclined", UriKind.Relative))).StatusCode);
        Assert.Null((await DoneCardAsync()).FollowUp);
        await using var audit = sql.Audit(h.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == SchedulingAudit.WaitlistRemove && e.ResourceId == due.Id && e.Detail != null);
        Assert.Equal(("reason:PatientDeclined", (Guid?)h.Patient), (row.Detail, row.PatientId));
    }
}
