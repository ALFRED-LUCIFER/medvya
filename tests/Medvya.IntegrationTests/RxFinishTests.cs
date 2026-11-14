using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-090 over HTTP on real PostgreSQL: a draft prescription left when the visit ended can still be finished and authorised by the doctor who led the
/// visit, and by nobody else; it waits on that doctor's Today until it is authorised or discarded with a reason (audited, append-only); and sign, authorise,
/// place orders and end stay four separate recorded acts. SAFETY mutation checks are named on the tests. Synthetic data only.
/// </summary>
public sealed class RxFinishTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.NoKnownAllergiesAsync(h.Patient);
        await h.DoctorTemplateAsync();
        // Both doctors hold a licence on record, so only the rule under test can refuse the second one.
        await using (var db = sql.Context(h.Tenant))
        {
            foreach (var user in new[] { h.Doctor, h.SecondDoctor })
            {
                var membership = await db.Memberships.SingleAsync(m => m.UserId == user);
                db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = user == h.Doctor ? "84213" : "84214", SubmittedAt = h.Clock.GetUtcNow() });
                db.DoctorProfiles.Add(new DoctorProfile { MembershipId = membership.Id, TenantId = h.Tenant, FullName = user == h.Doctor ? "Dr. Arjun Rao (synthetic)" : "Dr. S. Menon (synthetic)" });
            }
            await db.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test/app")
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    async Task<List<PrescriptionDiscard>> DiscardRowsAsync()
    {
        await using var db = sql.Prescribing(h.Tenant);
        return await db.PrescriptionDiscards.AsNoTracking().ToListAsync();
    }

    HttpClient Client(Guid user, int? reauthMinutesAgo = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (reauthMinutesAgo is { } m) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().AddMinutes(-m).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    static async Task<string?> Code(HttpResponseMessage r) => (await Json(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    static readonly UpdateRxLine Twice = new("1", "tablet", "oral", "BD", "after_food", 5, 10, null);

    /// <summary>Metformin on the draft with no instruction yet: the doctor still has to finish it.</summary>
    async Task<Guid> DraftLineAsync()
    {
        var id = (await h.Get<IFormulary>(h.Doctor).SearchAsync("Metformin", 1)).Single().Id;
        return (await h.Get<IPrescriptions>(h.Doctor).AddAsync(encounter, new AddRxLine(id, null, null, null)))!.Single().Id!.Value;
    }

    Task<HttpResponseMessage> UpdateAsync(Guid user, Guid line) => Client(user).PutAsJsonAsync(U($"/v1/prescription-items/{line}"), Twice);

    Task<HttpResponseMessage> AuthoriseAsync(Guid user) => Client(user, 0).PostAsync(U($"/v1/encounters/{encounter}/prescription/authorisation"), null);

    Task<HttpResponseMessage> DiscardAsync(Guid user, string? reason) => Client(user).PostAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/discard"), new { reason });

    async Task<JsonElement> PendingAsync(Guid user)
    {
        using var r = await Client(user).GetAsync(U("/v1/prescriptions/pending"));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("items");
    }

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    // SAFETY mutation check (AC6): drop "encounter.LeadClinicianId == Me" in PrescriptionService.GuardAsync and the second doctor's update and
    // authorisation go through; drop the ended-visit rule (the Status test) and the new medicine after the visit is accepted; put back the old rule
    // (ended means closed for everyone) and the leading doctor's update and authorisation are refused.
    [Fact]
    public async Task AfterTheVisitEndedOnlyTheDoctorWhoLedItFinishesAndAuthorisesTheDraft()
    {
        var line = await DraftLineAsync();
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);

        // A colleague with a licence and (under the open policy) a care relationship: the draft is not theirs to finish once the visit ended.
        using (var r = await UpdateAsync(h.SecondDoctor, line)) Assert.Equal((HttpStatusCode.Conflict, "encounter_ended"), (r.StatusCode, await Code(r)));
        using (var r = await AuthoriseAsync(h.SecondDoctor)) Assert.Equal((HttpStatusCode.Conflict, "encounter_ended"), (r.StatusCode, await Code(r)));
        using (var r = await DiscardAsync(h.SecondDoctor, "Not needed (synthetic)")) Assert.Equal((HttpStatusCode.Conflict, "encounter_ended"), (r.StatusCode, await Code(r)));
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var r = await AuthoriseAsync(h.SecondDoctor)) Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (r.StatusCode, await Code(r)));

        // No new medicine once the patient has left, even for the doctor who led the visit.
        var formulary = (await h.Get<IFormulary>(h.Doctor).SearchAsync("Metformin", 1)).Single().Id;
        using (var r = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/items"), new { formularyItemId = formulary }))
            Assert.Equal((HttpStatusCode.Conflict, "encounter_ended"), (r.StatusCode, await Code(r)));

        using (var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/authorisation")))
            Assert.True((await Json(r)).GetProperty("editable").GetBoolean());
        using (var r = await UpdateAsync(h.Doctor, line)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await AuthoriseAsync(h.Doctor))
        {
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            Assert.Equal(1, (await Json(r)).GetProperty("authorised").GetProperty("versionNo").GetInt32());
        }
        var authorise = Assert.Single(await AuditAsync("rx.authorise"), e => e.Outcome == AuditOutcome.Allowed); // its own record, by the doctor who led the visit
        Assert.Equal(h.Doctor, authorise.ActorUserId);
        Assert.Equal(0, (await PendingAsync(h.Doctor)).GetArrayLength()); // authorised: off Today
    }

    // SAFETY mutation check (AC3, AC5): drop the LeadClinicianId filter in PrescriptionService.PendingAsync and the second doctor sees Meera's draft;
    // drop the discard filter and the discarded draft stays on Today; drop the discard check in GuardAsync and the discarded draft can be authorised.
    [Fact]
    public async Task TodayListsTheDoctorsOwnEndedDraftsUntilTheyAreDiscardedWithAReasonThatIsKeptAndAudited()
    {
        var line = await DraftLineAsync();
        Assert.Equal(0, (await PendingAsync(h.Doctor)).GetArrayLength()); // still open: the doctor is working on it
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);

        var item = Assert.Single((await PendingAsync(h.Doctor)).EnumerateArray());
        Assert.Equal(encounter, item.GetProperty("encounterId").GetGuid());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("patientName").GetString()));
        Assert.Equal(1, item.GetProperty("lines").GetInt32());
        Assert.Equal(0, (await PendingAsync(h.SecondDoctor)).GetArrayLength()); // scoped to the doctor who led the visit
        Assert.Single(await AuditAsync("rx.pending"));

        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            Assert.Empty((await other.Get<IPrescriptions>(other.Doctor).PendingAsync(other.Facility)).Items); // another clinic sees nothing of it
            await using var db = sql.Prescribing(other.Tenant);
            Assert.Equal(0, await db.PrescriptionItems.IgnoreQueryFilters().CountAsync()); // row-level security, with the EF filter off
        }
        finally
        {
            await other.CloseAsync();
        }

        using (var r = await DiscardAsync(h.Doctor, " ")) Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // a reason is required
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        using (var r = await DiscardAsync(h.Doctor, "Patient will buy it later (synthetic)"))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var state = await Json(r);
            Assert.Equal("Patient will buy it later (synthetic)", state.GetProperty("discard").GetProperty("reason").GetString());
            Assert.False(state.GetProperty("editable").GetBoolean());
        }
        var discard = Assert.Single(await AuditAsync("rx.discard"), e => e.Outcome == AuditOutcome.Allowed);
        await using (var db = sql.Prescribing(h.Tenant))
        {
            var row = await db.PrescriptionDiscards.AsNoTracking().SingleAsync();
            // Who, when and the reason: the event points at the discard record, which keeps the doctor's words out of the audit log.
            Assert.Equal((h.Doctor, h.Clock.GetUtcNow(), row.Id), (discard.ActorUserId!.Value, discard.OccurredAt, discard.ResourceId!.Value));
            Assert.Equal((h.Doctor, h.Clock.GetUtcNow(), "Patient will buy it later (synthetic)"), (row.DiscardedBy, row.DiscardedAt, row.Reason));
            Assert.DoesNotContain("synthetic", discard.Detail ?? "");
        }
        Assert.Equal(0, (await PendingAsync(h.Doctor)).GetArrayLength());

        // Closed for good, and the record cannot be changed or removed, even by the application's own database role.
        using (var r = await UpdateAsync(h.Doctor, line)) Assert.Equal((HttpStatusCode.Conflict, "discarded"), (r.StatusCode, await Code(r)));
        using (var r = await AuthoriseAsync(h.Doctor)) Assert.Equal((HttpStatusCode.Conflict, "discarded"), (r.StatusCode, await Code(r)));
        using (var again = await DiscardAsync(h.Doctor, "Second reason (synthetic)")) Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(await DiscardRowsAsync());
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."PrescriptionDiscards" SET "Reason" = 'x'""", conn).ExecuteNonQueryAsync());
    }

    // SAFETY (AC7, and the immutability the ticket asks to prove): drop AddLedger for AuthorisedPrescriptions and the version is edited in place;
    // drop the signed-note check in ConsultNoteService and the signed note changes.
    [Fact]
    public async Task SignAuthorisePlaceOrdersAndEndAreFourSeparateEventsAndWhatWasSignedOrAuthorisedNeverChanges()
    {
        var line = await DraftLineAsync();
        using (var r = await UpdateAsync(h.Doctor, line)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var note = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"),
            new { baseVersion = 0, sections = new { chiefComplaint = "Thirst (synthetic)", assessment = "Synthetic review", plan = "Rest" }, recordingConsent = "Declined" }))
            Assert.True(note.IsSuccessStatusCode, await note.Content.ReadAsStringAsync());
        // Signing needs identity confirmed with the patient (MDV-P1-109).
        using (var confirmed = await Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/identity-confirmation"), null)) Assert.True(confirmed.IsSuccessStatusCode);
        using (var sign = await Client(h.Doctor, 0).PostAsJsonAsync(U($"/v1/encounters/{encounter}/note/sign"), new { baseVersion = 1 }))
            Assert.True(sign.IsSuccessStatusCode, await sign.Content.ReadAsStringAsync());
        Assert.Empty(await AuditAsync("rx.authorise")); // signing authorised nothing
        using (var r = await AuthoriseAsync(h.Doctor)) Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        await h.Get<IOrders>(h.Doctor).AddAsync(encounter, new AddOrder(OrderKind.Lab, "HbA1c", null));
        await h.Get<IOrders>(h.Doctor).PlaceAsync(encounter, new PlaceOrders(null));
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);

        var events = new[] { "note.sign", "rx.authorise", "order.place", "encounter.end" };
        foreach (var action in events) Assert.Single(await AuditAsync(action), e => e.Outcome == AuditOutcome.Allowed);

        // Finishing after the visit changes the draft, never the authorised version: a change is a new version to authorise.
        using (var r = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/prescription-items/{line}"), Twice with { DurationDays = 7 })) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/authorisation")))
            Assert.True((await Json(r)).GetProperty("changed").GetBoolean());
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
            await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."AuthorisedPrescriptions" SET "RuleSetVersion" = 'x'""", conn).ExecuteNonQueryAsync());
        using (var r = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { baseVersion = 2, sections = new { plan = "Changed (synthetic)" }, recordingConsent = "Declined" }))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }
}
