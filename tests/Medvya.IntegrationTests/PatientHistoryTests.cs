using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-032 on real PostgreSQL, over HTTP: a doctor or a nurse saves a history section, every save keeps the earlier text with its author
/// and time (append-only, enforced by the database too), the banner shows blood group and language only when recorded, the life stage and
/// its sections come with the chart, another tenant sees nothing (EF filter and row-level security), and the desk, a care coordinator and a
/// colleague without a care relationship are refused.
/// </summary>
public sealed class PatientHistoryTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient); // the doctor's care relationship with Meera
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

    Task<HttpResponseMessage> Save(Guid user, string section, string? text, Guid? patient = null) =>
        Client(user).PutAsJsonAsync(new Uri($"/v1/patients/{patient ?? h.Patient}/history/{section}", UriKind.Relative), new { text });

    async Task<JsonElement> ChartAsync()
    {
        using var r = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/chart", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task EverySaveKeepsTheEarlierTextWithItsAuthorAndTime()
    {
        using (var first = await Save(h.Nurse, "Social", "Teacher. Never smoked (synthetic).")) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        using var second = await Save(h.Doctor, "Social", "  Teacher. Never smoked. Walks daily (synthetic).  ");
        var items = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();

        // Newest first: the current text, then the one it replaced, each with who and when; the text is trimmed.
        Assert.Equal([("Social", "Teacher. Never smoked. Walks daily (synthetic).", "Dr. Arjun Rao (synthetic)"), ("Social", "Teacher. Never smoked (synthetic).", null)],
            items.Select(i => (i.GetProperty("section").GetString(), i.GetProperty("text").GetString(), i.GetProperty("author").GetString())));
        Assert.True(items[0].GetProperty("recordedAt").GetDateTimeOffset() > items[1].GetProperty("recordedAt").GetDateTimeOffset());
        Assert.Equal(2, (await ChartAsync()).GetProperty("chart").GetProperty("history").GetArrayLength());

        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal([h.Doctor, h.Nurse], await db.HistoryEntries.OrderByDescending(x => x.RecordedAt).Select(x => x.RecordedBy).ToListAsync());
    }

    [Theory]
    [InlineData("Social", "", "required")]
    [InlineData("Social", "   ", "required")]
    [InlineData("BloodGroup", "O positive", "invalid")] // only the eight written forms
    [InlineData("BloodGroup", "o+", "invalid")]
    public async Task ARefusedSaveSaysWhyAndSavesNothing(string section, string text, string code)
    {
        using var r = await Save(h.Doctor, section, text);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((HttpStatusCode.BadRequest, code), (r.StatusCode, body.GetProperty("errors").GetProperty("text")[0].GetString()));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.HistoryEntries.CountAsync());
    }

    [Fact]
    public async Task TooLongTextOrAnUnknownSectionIsRefused()
    {
        using var tooLong = await Save(h.Doctor, "Family", new string('x', HistoryRules.MaxText + 1));
        using var unknown = await Save(h.Doctor, "Horoscope", "Leo");
        using var number = await Save(h.Doctor, "99", "Leo");
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (tooLong.StatusCode, unknown.StatusCode, number.StatusCode));
    }

    [Fact]
    public async Task TheBannerShowsBloodGroupLanguageAndCareTeamOnlyWhenRecorded()
    {
        var before = await ChartAsync();
        Assert.Equal(JsonValueKind.Null, before.GetProperty("chart").GetProperty("bloodGroup").ValueKind); // never guessed
        Assert.Equal(JsonValueKind.Null, before.GetProperty("patient").GetProperty("preferredLanguage").ValueKind);
        Assert.Equal(0, before.GetProperty("chart").GetProperty("careTeam").GetArrayLength());

        using (var r = await Save(h.Nurse, "BloodGroup", "O+")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using (var patients = sql.Patients(h.Tenant))
        {
            (await patients.Patients.SingleAsync(p => p.Id == h.Patient)).PreferredLanguage = "Tamil";
            await patients.SaveChangesAsync();
        }
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Encounters.Add(new()
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, AppointmentId = Guid.NewGuid(), LeadClinicianId = h.Doctor,
                Status = EncounterStatus.Ended, AuthorityMode = AuthorityMode.Medvya, StartedAt = h.Clock.GetUtcNow().AddDays(-3), EndedAt = h.Clock.GetUtcNow().AddDays(-3),
            });
            await db.SaveChangesAsync();
        }

        var after = await ChartAsync();
        Assert.Equal(("O+", "Tamil"), (after.GetProperty("chart").GetProperty("bloodGroup").GetString(), after.GetProperty("patient").GetProperty("preferredLanguage").GetString()));
        Assert.Equal(["Dr. Arjun Rao (synthetic)"], after.GetProperty("chart").GetProperty("careTeam").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task TheChartSaysTheLifeStageAndTheSectionsItOffers()
    {
        var adult = await ChartAsync(); // Meera, 54, female
        Assert.Equal("Adult", adult.GetProperty("lifeStage").GetString());
        Assert.Contains("WomensHealth", adult.GetProperty("historySections").EnumerateArray().Select(x => x.GetString()));

        await using (var patients = sql.Patients(h.Tenant))
        {
            (await patients.Patients.SingleAsync(p => p.Id == h.Patient)).DateOfBirth = DateOnly.FromDateTime(h.Clock.GetUtcNow().UtcDateTime).AddYears(-4); // synthetic
            await patients.SaveChangesAsync();
        }
        var child = await ChartAsync();
        Assert.Equal("Child", child.GetProperty("lifeStage").GetString());
        Assert.Equal("Birth", child.GetProperty("historySections")[0].GetString());
    }

    /// <summary>SAFETY, mutation check: drop the clinical access check in PatientHistoryService and the colleague's save goes through.</summary>
    [Fact]
    public async Task OnlyADoctorOrNurseWhoTreatsThePatientMaySaveAndEverySaveIsAudited()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var desk = await Save(h.Reception, "Social", "x")) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var coordinator = await Save(h.CareCoordinator, "Social", "x")) Assert.Equal(HttpStatusCode.Forbidden, coordinator.StatusCode);
        using (var colleague = await Save(h.SecondDoctor, "Social", "x"))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await colleague.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()));
        using (var missing = await Save(h.Doctor, "Social", "x", Guid.NewGuid())) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var ok = await Save(h.Doctor, "Social", "Retired engineer (synthetic)")) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await using (var db = sql.Encounters(h.Tenant)) Assert.Equal(1, await db.HistoryEntries.CountAsync());
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "patient.history.save").OrderBy(e => e.Id).ToListAsync();
        Assert.Contains(rows, r => r.Outcome == AuditOutcome.Allowed && r.PatientId == h.Patient && r.ActorUserId == h.Doctor && r.Purpose == AuditPurpose.Treatment);
        Assert.Contains(rows, r => r.Outcome == AuditOutcome.Denied && r.ActorUserId == h.SecondDoctor);
        Assert.DoesNotContain(rows, r => JsonSerializer.Serialize(r).Contains("engineer", StringComparison.Ordinal)); // never the text
    }

    [Fact]
    public async Task AnotherTenantSeesNoHistoryEvenWithTheFilterOff()
    {
        using (var r = await Save(h.Doctor, "Family", "Mother: diabetes (synthetic)")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await other.HistoryEntries.ToListAsync());
        Assert.Empty(await other.HistoryEntries.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security on its own
    }

    /// <summary>SAFETY, mutation check: drop the trigger from the migration and the rewrite goes through.</summary>
    [Fact]
    public async Task TheDatabaseRefusesToRewriteOrDeleteAVersionButAMergeMayMoveIt()
    {
        using (var r = await Save(h.Doctor, "Surgical", "Appendicectomy 2001 (synthetic)")) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using var db = sql.Encounters(h.Tenant);
        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => db.HistoryEntries.ExecuteUpdateAsync(s => s.SetProperty(x => x.Text, "None")));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.HistoryEntries.ExecuteDeleteAsync());
        // The trigger refuses the rewrite; the module's login has no DELETE at all (infra/postgres/roles.sql), and the trigger backs that up for the owner.
        Assert.Equal((PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.InsufficientPrivilege), (rewrite.SqlState, delete.SqlState));
        Assert.Equal(1, await db.HistoryEntries.ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, h.SecondPatient).SetProperty(x => x.MergedFromPatientId, h.Patient)));
    }
}
