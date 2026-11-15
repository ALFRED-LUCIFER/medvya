extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Platform.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ConsoleProgram = platform::Program;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P5-064 (ADR-014 note) on real PostgreSQL: the console's Medvya-wide medicine list, through the console host and its narrow medvya_formulary login,
/// as clinics then see it. Mutation checks: let every console role write (PlatformPolicies.MedicinesWrite to Console) and the read-only roles test fails;
/// drop the platform audit call in MedicineListEndpoints and the audit asserts fail; drop the restrictive shared_list_only policy (migration
/// SharedFormularyLogin) and TheFormularyLoginReachesOnlyTheSharedRowsEvenWithATenantSet fails; grant medvya_formulary another table and
/// TheFormularyLoginCannotReadAnyOtherTable fails. Synthetic data only.
/// </summary>
public sealed class SharedFormularyTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness clinic = new(sql);
    WebApplicationFactory<ConsoleProgram>? factory;
    const string Why = "Advisor reviewed the Schedule class (synthetic)";

    public Task InitializeAsync() => clinic.SeedAsync();

    public async Task DisposeAsync()
    {
        await clinic.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    async Task<HttpClient> Console(PlatformRole role)
    {
        factory ??= PlatformConsoleTests.CreateFactory(sql, signInPerMinute: 1000, configure: b => b.UseSetting("ConnectionStrings:Formulary", sql.As("medvya_formulary")));
        var (member, key) = await PlatformConsoleTests.Seed(sql, role);
        return await PlatformConsoleTests.SignedIn(factory, member, key);
    }

    static Task<HttpResponseMessage> Post(HttpClient c, string path, object body) => PlatformConsoleTests.Post(c, path, body);

    static object New(string generic, string? reason = Why, string country = "IN", string regClass = "NotVerified", string form = "Tablet") =>
        new { country, generic, strength = "5 mg", form, brands = new[] { "Synthbrand" }, regClass, atc = "A10BB12", reason };

    static async Task<JsonElement> AddAsync(HttpClient c, string generic)
    {
        using var r = await Post(c, "/v1/medicines/", New(generic));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    async Task<List<PlatformAuditEntry>> AuditOf(Guid id)
    {
        await using var db = PlatformConsoleTests.Db(sql);
        return await db.AuditEntries.Where(a => a.ResourceId == id).OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync();
    }

    Task<IReadOnlyList<FormularyItemView>> ClinicSearch(string q) => clinic.Get<IFormulary>(clinic.Doctor).SearchAsync(q, 50);

    [Theory]
    [InlineData(PlatformRole.Owner), InlineData(PlatformRole.Operations)]
    public async Task OwnerAndOperationsManageTheListWithAReasonAuditedAndClinicsSeeIt(PlatformRole role)
    {
        using var c = await Console(role);
        var generic = $"Synthetic {role} medicine";
        var added = await AddAsync(c, generic);
        var id = added.GetProperty("id").GetGuid();
        Assert.Equal(("Medvya", "NotVerified", "IN"), (added.GetProperty("source").GetString(), added.GetProperty("regClass").GetString(), added.GetProperty("country").GetString()));

        // SAFETY: a clinic sees the unverified class, and the paper's line that says so, never an ordinary medicine.
        var seen = Assert.Single(await ClinicSearch(generic));
        Assert.Equal((RegulatoryClass.NotVerified, FormularySource.Medvya), (seen.RegClass, seen.Source));
        Assert.Equal([FormularyRules.ClassNotVerified], seen.PrintRequirements);
        Assert.Single(await ClinicSearch("synthbrand"), m => m.Id == id); // found by its brand too

        Assert.Equal(HttpStatusCode.OK, (await Post(c, $"/v1/medicines/{id}/class", new { regClass = "ScheduleH", reason = Why })).StatusCode);
        Assert.Equal(["warning_schedule_h"], Assert.Single(await ClinicSearch(generic)).PrintRequirements);

        using (var corrected = await Post(c, $"/v1/medicines/{id}/correction", new { generic, strength = "10 mg", form = "Tablet", brands = Array.Empty<string>(), atc = "A10BB12", reason = "Strength typed wrongly" }))
        {
            Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
            var body = await corrected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("10 mg", "ScheduleH"), (body.GetProperty("strength").GetString(), body.GetProperty("regClass").GetString())); // the class is not part of a correction
        }

        Assert.Equal(HttpStatusCode.OK, (await Post(c, $"/v1/medicines/{id}/deactivation", new { reason = "Withdrawn from the market" })).StatusCode);
        Assert.Empty(await ClinicSearch(generic)); // no longer offered...
        var listed = await c.GetFromJsonAsync<JsonElement>(new Uri($"/v1/medicines?country=IN&q={Uri.EscapeDataString(generic)}", UriKind.Relative));
        Assert.False(Assert.Single(listed.EnumerateArray()).GetProperty("active").GetBoolean()); // ...and never deleted

        var audit = await AuditOf(id);
        Assert.Equal(["medicine.add", "medicine.class", "medicine.correct", "medicine.deactivate"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.True(a.ActorMemberId is not null && a.SubjectTenantId is null && a.ResourceType == "medicine" && a.Reason is { Length: >= 5 }));
        Assert.Equal(Why, audit[0].Reason);
    }

    [Theory]
    [InlineData(PlatformRole.Support), InlineData(PlatformRole.Finance), InlineData(PlatformRole.Reviewer)]
    public async Task OtherRolesReadTheListButEveryWriteIs403(PlatformRole role)
    {
        using var c = await Console(role);
        var tramadol = Assert.Single((await c.GetFromJsonAsync<JsonElement>(new Uri("/v1/medicines?country=AE&q=tram", UriKind.Relative))).EnumerateArray());
        Assert.Equal(("Starter", "Controlled", true), (tramadol.GetProperty("source").GetString(), tramadol.GetProperty("regClass").GetString(), tramadol.GetProperty("active").GetBoolean()));
        var id = tramadol.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/v1/medicines/", New($"Synthetic {role} refused"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/v1/medicines/{id}/class", new { regClass = "None", reason = Why })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/v1/medicines/{id}/correction", new { generic = "Renamed", strength = "50 mg", form = "Capsule", reason = Why })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/v1/medicines/{id}/deactivation", new { reason = Why })).StatusCode);
        Assert.Empty(await AuditOf(id));
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>(new Uri($"/v1/medicines?country=IN&q={Uri.EscapeDataString($"Synthetic {role} refused")}", UriKind.Relative))).EnumerateArray());
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("four")]
    public async Task EveryWriteNeedsAReasonAndNothingChangesWithoutOne(string? reason)
    {
        using var c = await Console(PlatformRole.Owner);
        var id = (await AddAsync(c, $"Synthetic reasonless {reason?.Length ?? -1}")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, "/v1/medicines/", New($"Synthetic never added {reason?.Length ?? -1}", reason))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/v1/medicines/{id}/class", new { regClass = "None", reason })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/v1/medicines/{id}/correction", new { generic = "Renamed", strength = "1 mg", form = "Tablet", reason })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/v1/medicines/{id}/deactivation", new { reason })).StatusCode);
        Assert.Equal(["medicine.add"], (await AuditOf(id)).Select(a => a.Action));
        var row = Assert.Single(await ClinicSearch($"Synthetic reasonless {reason?.Length ?? -1}"));
        Assert.Equal((RegulatoryClass.NotVerified, "5 mg", true), (row.RegClass, row.Strength, row.Active));
    }

    [Fact]
    public async Task TheClinicAddRulesApplyAndAListedMedicineIsRefused()
    {
        using var c = await Console(PlatformRole.Operations);
        async Task<Dictionary<string, string[]>> Refused(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        }

        var errors = await Refused(await Post(c, "/v1/medicines/", New("Synthetic lozenge", regClass: "Controlled", form: "Lozenge")));
        Assert.Equal(["unknown"], errors["form"]);
        Assert.Equal(["not_in_country"], errors["regClass"]); // Controlled is the UAE's class, not India's
        Assert.Equal(["unknown"], (await Refused(await Post(c, "/v1/medicines/", New("Synthetic elsewhere", country: "US"))))["country"]);
        Assert.Equal(["unknown"], (await Refused(await c.GetAsync(new Uri("/v1/medicines?country=US", UriKind.Relative))))["country"]);

        using (var listed = await Post(c, "/v1/medicines/", New("METFORMIN")))
            Assert.Equal(HttpStatusCode.Created, listed.StatusCode); // 5 mg is not on the starter list...
        using (var twice = await Post(c, "/v1/medicines/", New("metformin")))
            Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode); // ...but now it is, whatever the case

        var paracetamol = Assert.Single((await c.GetFromJsonAsync<JsonElement>(new Uri("/v1/medicines?country=IN&q=paracetamol", UriKind.Relative))).EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(["not_in_country"], (await Refused(await Post(c, $"/v1/medicines/{paracetamol}/class", new { regClass = "Controlled", reason = Why })))["regClass"]);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/v1/medicines/{Guid.NewGuid()}/deactivation", new { reason = Why })).StatusCode);
    }

    [Fact]
    public async Task AClinicsOwnMedicineIsNotFoundOrChangedThroughTheConsole()
    {
        var own = await clinic.Get<IFormulary>(clinic.Admin).AddAsync(new AddMedicine("Synthetic clinic-only", "1 mg", "Tablet", null, null, RegulatoryClass.None, null, null));
        using var c = await Console(PlatformRole.Owner);
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>(new Uri("/v1/medicines?country=IN&q=synthetic%20clinic", UriKind.Relative))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/v1/medicines/{own.Id}/class", new { regClass = "ScheduleX", reason = Why })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/v1/medicines/{own.Id}/deactivation", new { reason = Why })).StatusCode);
        var after = Assert.Single(await ClinicSearch("synthetic clinic-only"));
        Assert.Equal((own.Id, RegulatoryClass.None, true), (after.Id, after.RegClass, after.Active));
    }

    // SAFETY (tenant isolation, AGENTS.md 1.4): whatever the console's code does, the database gives medvya_formulary the shared rows and nothing else,
    // even when its session claims a clinic's tenant.
    [Fact]
    public async Task TheFormularyLoginReachesOnlyTheSharedRowsEvenWithATenantSet()
    {
        var own = await clinic.Get<IFormulary>(clinic.Admin).AddAsync(new AddMedicine("Synthetic private", "2 mg", "Tablet", null, null, RegulatoryClass.None, null, null));
        await using var conn = await sql.OpenAs("formulary_user", clinic.Tenant);
        async Task<long> Count(string where)
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM prescribing.\"FormularyItems\" WHERE {where}", conn);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.Equal(0L, await Count("\"TenantId\" IS NOT NULL"));
        await using (var admin = new NpgsqlConnection(sql.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var shared = new NpgsqlCommand("SELECT count(*) FROM prescribing.\"FormularyItems\" WHERE \"TenantId\" IS NULL", admin);
            Assert.Equal((long)(await shared.ExecuteScalarAsync())!, await Count("true")); // every shared row, and only those
        }
        await using (var update = new NpgsqlCommand($"UPDATE prescribing.\"FormularyItems\" SET \"Active\" = false WHERE \"Id\" = '{own.Id}'", conn))
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
        await using (var insert = new NpgsqlCommand($"INSERT INTO prescribing.\"FormularyItems\" (\"Id\", \"TenantId\", \"Country\", \"Generic\", \"Strength\", \"Form\", \"Brands\", \"RegClass\", \"Source\", \"Active\") VALUES (gen_random_uuid(), '{clinic.Tenant}', 'IN', 'Sneaky', '1 mg', 'Tablet', '{{}}', 'None', 'ClinicAdded', true)", conn))
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync())).SqlState);
        // A shared row cannot be handed to a clinic, nor its source or register columns rewritten, nor anything deleted.
        foreach (var statement in new[] { $"UPDATE prescribing.\"FormularyItems\" SET \"TenantId\" = '{clinic.Tenant}'", "UPDATE prescribing.\"FormularyItems\" SET \"Source\" = 'Register'",
                     "UPDATE prescribing.\"FormularyItems\" SET \"RegisterCode\" = 'X1'", "DELETE FROM prescribing.\"FormularyItems\"" })
        {
            await using var cmd = new NpgsqlCommand(statement, conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
        }
        Assert.Equal(RegulatoryClass.None, Assert.Single(await ClinicSearch("synthetic private")).RegClass);
    }

    [Theory]
    [InlineData("prescribing.\"FormularyBrands\"")]
    [InlineData("prescribing.\"PrescriptionItems\"")]
    [InlineData("prescribing.\"OrderSets\"")]
    [InlineData("patients.\"Patients\"")]
    [InlineData("encounters.\"Encounters\"")]
    [InlineData("tenancy.\"Tenants\"")]
    [InlineData("audit.\"AuditEvents\"")]
    [InlineData("platform.\"AuditEntries\"")]
    public async Task TheFormularyLoginCannotReadAnyOtherTable(string table)
    {
        await using var conn = await sql.OpenAs("formulary_user", clinic.Tenant);
        await using var cmd = new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1", conn);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync())).SqlState);
    }
}
