using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Host.Api.AuditLog;
using Medvya.Audit.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-035 on real SQL Server: the practice access log. Only roles with the Audit log capability can open it (others get 403,
/// audited Denied); filtering by patient lists every event on that record by anyone, denied attempts included; paging is stable;
/// another tenant's events never appear; the export is watermarked, audited and carries no patient names.
/// </summary>
public sealed class AccessLogTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program>? factory;

    public Task InitializeAsync() => h.SeedAsync();

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString()), new Claim("role", ctx.Request.Headers["X-Test-Role"].ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Role role, Guid? user = null)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", (user ?? h.Admin).ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role.ToString());
        return client;
    }

    static readonly DateTimeOffset Noon = new(2026, 10, 19, 6, 30, 0, TimeSpan.Zero);

    async Task Seed(Guid tenant, string action, Guid? patient, Guid actor, DateTimeOffset at, AuditOutcome outcome = AuditOutcome.Allowed, string purpose = "treatment")
    {
        await using var db = sql.Audit(tenant);
        db.Events.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, ActorUserId = actor, Action = action, ResourceType = "patient", PatientId = patient,
            Purpose = purpose, Outcome = outcome, OccurredAt = at,
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.CareCoordinator)]
    [InlineData(Role.HospitalDoctor)]
    [InlineData(Role.Patient)]
    public async Task RolesWithoutTheAuditLogCapabilityGet403AndTheAttemptIsAuditedDenied(Role role)
    {
        using var client = Client(role, h.Reception);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/audit/access-log")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/v1/audit/access-log/patients/{h.Patient}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/audit/access-log/export", new { })).StatusCode);
        await using var db = sql.Audit(h.Tenant);
        var events = await db.Events.Where(e => e.ActorUserId == h.Reception).ToListAsync();
        Assert.Equal(["audit.export", "audit.read", "audit.read"], events.Select(e => e.Action).Order());
        Assert.All(events, e => Assert.Equal(AuditOutcome.Denied, e.Outcome));
    }

    [Fact]
    public async Task FilteringByPatientListsEveryReadAndWriteByAnyoneWithTimeNamesAndOutcomeAndOnlyThenTheName()
    {
        await Seed(h.Tenant, "patient.read", h.Patient, h.Doctor, Noon.AddMinutes(-30), purpose: "treatment");
        await Seed(h.Tenant, "patient.update", h.Patient, h.Reception, Noon.AddMinutes(-20), purpose: "registration");
        await Seed(h.Tenant, "patient.read", h.Patient, h.Nurse, Noon.AddMinutes(-10), AuditOutcome.Denied, "treatment");
        await Seed(h.Tenant, "patient.read", h.SecondPatient, h.Doctor, Noon.AddMinutes(-5)); // another patient's record
        using var client = Client(Role.PracticeAdmin);

        var one = await client.GetFromJsonAsync<JsonElement>($"/v1/audit/access-log/patients/{h.Patient}");
        var items = one.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("actorUserId").GetGuid() != h.Admin).ToList();
        Assert.Equal([("patient.read", "Denied"), ("patient.update", "Allowed"), ("patient.read", "Allowed")],
            items.Select(i => (i.GetProperty("action").GetString()!, i.GetProperty("outcome").GetString()!))); // newest first
        Assert.All(items, i => Assert.Equal(h.Patient, i.GetProperty("patientId").GetGuid()));
        Assert.All(items, i => Assert.Equal("Meera Iyer (synthetic)", i.GetProperty("patientName").GetString()));
        var doctorRead = items[^1];
        Assert.Equal(("Dr. Arjun Rao (synthetic)", "SoloDoctor", "treatment", "000001", Noon.AddMinutes(-30)),
            (doctorRead.GetProperty("actorName").GetString(), doctorRead.GetProperty("role").GetString(), doctorRead.GetProperty("purpose").GetString(),
             doctorRead.GetProperty("patientMrn").GetString(), doctorRead.GetProperty("occurredAt").GetDateTimeOffset()));

        // Without choosing a patient, rows carry the MRN and never the name.
        var all = await client.GetFromJsonAsync<JsonElement>("/v1/audit/access-log");
        Assert.All(all.GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("patientName").ValueKind));
        Assert.Contains(all.GetProperty("items").EnumerateArray(), i => i.GetProperty("patientMrn").GetString() == "000002");
        // Opening the log is itself audited, and the per-patient route records which patient's name was shown.
        await using var audit = sql.Audit(h.Tenant);
        var reads = await audit.Events.Where(e => e.ActorUserId == h.Admin && e.Action == "audit.read").ToListAsync();
        Assert.Contains(reads, e => e.PatientId == h.Patient);
        Assert.Contains(reads, e => e.PatientId == null);
    }

    [Fact]
    public async Task ActorActionAndDateRangeFiltersNarrowTheListAndOtherTenantsNeverShow()
    {
        var other = Guid.NewGuid();
        await Seed(h.Tenant, "patient.read", h.Patient, h.Doctor, Noon);
        await Seed(h.Tenant, "patient.search", null, h.Reception, Noon.AddDays(-2));
        await Seed(h.Tenant, "patient.update", h.Patient, h.Reception, Noon.AddDays(-40));
        await using (var db = sql.Context(other))
        {
            db.Tenants.Add(new Tenant { Id = other, Name = "Elsewhere Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await db.SaveChangesAsync();
        }
        await Seed(other, "patient.read", Guid.NewGuid(), Guid.NewGuid(), Noon);
        using var client = Client(Role.PracticeAdmin);

        async Task<List<string>> Actions(string query) =>
            [.. (await client.GetFromJsonAsync<JsonElement>($"/v1/audit/access-log?{query}")).GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("actorUserId").GetGuid() != h.Admin).Select(i => i.GetProperty("action").GetString()!)];

        Assert.Equal(["patient.read", "patient.search", "patient.update"], (await Actions("")).Order());
        Assert.Equal(["patient.search", "patient.update"], (await Actions($"actorUserId={h.Reception}")).Order());
        Assert.Equal(["patient.update"], await Actions($"actorUserId={h.Reception}&action=patient.update"));
        Assert.Equal(["patient.read", "patient.search"], (await Actions($"from={Uri.EscapeDataString(Noon.AddDays(-30).ToString("o"))}")).Order());
        Assert.Equal(["patient.search"], await Actions($"from={Uri.EscapeDataString(Noon.AddDays(-30).ToString("o"))}&to={Uri.EscapeDataString(Noon.AddDays(-1).ToString("o"))}"));
        var list = await client.GetFromJsonAsync<JsonElement>("/v1/audit/access-log");
        Assert.Contains("patient.update", list.GetProperty("actions").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task PagingWalksEveryEventOnceNewestFirstAndABadCursorIsRefused()
    {
        for (var i = 0; i < 5; i++) await Seed(h.Tenant, "patient.read", h.Patient, h.Doctor, Noon.AddMinutes(i));
        // Two events share a time: the id breaks the tie so none is skipped or repeated.
        await Seed(h.Tenant, "patient.update", h.Patient, h.Doctor, Noon.AddMinutes(4));
        using var client = Client(Role.PracticeAdmin);

        var seen = new List<Guid>();
        string? after = null;
        var pages = 0;
        do
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/v1/audit/access-log/patients/{h.Patient}?limit=2" + (after is null ? "" : $"&after={after}"));
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            after = page.GetProperty("next").GetString();
            pages++;
        } while (after is not null);
        Assert.Equal(3, pages);
        Assert.Equal(6, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/audit/access-log?after=not-a-cursor")).StatusCode);
    }

    [Fact]
    public async Task TheExportIsWatermarkedAuditedWithoutPatientNamesAndNeutralisesFormulas()
    {
        await Seed(h.Tenant, "patient.read", h.Patient, h.Doctor, Noon);
        using var client = Client(Role.PracticeAdmin);

        var response = await client.PostAsJsonAsync("/v1/audit/access-log/export", new { patientId = h.Patient });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('\uFEFF');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Medvya practice access log. Exported by ", lines[0]);
        Assert.Contains(h.Admin.ToString(), lines[0]); // the exporter has no profile name in this fixture, so the id marks the file
        Assert.Matches(@" at \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z\.", lines[0]); // exporter and time
        Assert.Equal(lines[0], lines[^1]);
        Assert.Contains("patient.read", csv, StringComparison.Ordinal);
        Assert.Contains("000001", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Meera", csv, StringComparison.Ordinal);

        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains(await audit.Events.Where(e => e.ActorUserId == h.Admin).Select(e => e.Action).ToListAsync(), a => a == "audit.export");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("plain", "plain")]
    [InlineData(null, "")]
    public void CsvCellsNeutraliseFormulasAndQuoteSeparators(string? value, string expected) =>
        Assert.Equal(expected, AccessLogEndpoints.Cell(value));
}
