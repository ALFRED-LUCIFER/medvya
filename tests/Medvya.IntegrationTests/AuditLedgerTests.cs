using System.Net;
using Medvya.Audit.Api;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-017: every patient-data access leaves one append-only, tenant-scoped, tamper-evident audit row.</summary>
public class AuditLedgerTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid();

    static AuditEvent Event(Guid tenant, Guid? patient = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, Action = "patient.read", ResourceType = "patient",
        PatientId = patient ?? Guid.NewGuid(), Purpose = "treatment", Outcome = AuditOutcome.Allowed, OccurredAt = DateTimeOffset.UtcNow,
    };

    async Task<AuditEvent> Write(Guid tenant)
    {
        var e = Event(tenant);
        await using var db = sql.Audit(tenant);
        await new AuditLog(db).WriteAsync(e, default);
        return e;
    }

    /// <summary>A write's event is part of the write's transaction: rolled back with it, committed with it (MDV-P0-017).</summary>
    [Fact]
    public async Task AnEventWrittenInAWritesTransactionCommitsOrRollsBackWithIt()
    {
        var (rolledBack, committed) = (Event(tenantA), Event(tenantA));
        await using (var scheduling = sql.Scheduling(tenantA))
        {
            await using var tx = await scheduling.Database.BeginTransactionAsync();
            await using var unused = sql.Audit(tenantA);
            await new AuditLog(unused).WriteAsync(rolledBack, Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(tx));
            await tx.RollbackAsync(); // the change failed after its event was written
        }
        await using (var scheduling = sql.Scheduling(tenantA))
        {
            await using var tx = await scheduling.Database.BeginTransactionAsync();
            await using var unused = sql.Audit(tenantA);
            await new AuditLog(unused).WriteAsync(committed, Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(tx));
            await tx.CommitAsync();
        }

        await using var db = sql.Audit(tenantA);
        Assert.False(await db.Events.AnyAsync(e => e.Id == rolledBack.Id));
        Assert.True(await db.Events.AnyAsync(e => e.Id == committed.Id));
    }

    [Fact]
    public async Task EventsAreVisibleOnlyToTheirOwnTenant()
    {
        var a = await Write(tenantA);
        var b = await Write(tenantB);

        await using var db = sql.Audit(tenantA);
        var seen = await db.Events.IgnoreQueryFilters().Select(e => e.Id).ToListAsync();
        Assert.Contains(a.Id, seen);
        Assert.DoesNotContain(b.Id, seen); // row-level security, not just the EF filter
    }

    [Fact]
    public async Task EventsCannotBeChangedOrDeleted()
    {
        var a = await Write(tenantA);
        // Layer one: the app role has no UPDATE or DELETE right on audit.
        await using var db = sql.Audit(tenantA);
        foreach (var statement in new[] { $"""UPDATE audit."AuditEvents" SET "Action" = 'x' WHERE "Id" = '{a.Id}'""", $"""DELETE FROM audit."AuditEvents" WHERE "Id" = '{a.Id}'""" })
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(statement))).SqlState);

        // Layer two: the append-only trigger refuses even a role that holds every right (the superuser).
        await using var admin = await sql.OpenAs("dbo");
        foreach (var statement in new[] { $"""UPDATE audit."AuditEvents" SET "Action" = 'x' WHERE "Id" = '{a.Id}'""", $"""DELETE FROM audit."AuditEvents" WHERE "Id" = '{a.Id}'""" })
            Assert.Contains("append only", (await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(statement, admin).ExecuteNonQueryAsync())).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WritingIntoAnotherTenantIsBlocked()
    {
        await using var db = sql.Audit(tenantA);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => new AuditLog(db).WriteAsync(Event(tenantB), default));
        Assert.IsType<PostgresException>(ex.InnerException);
    }

    [Fact]
    public async Task LedgerDigestVerifies()
    {
        await Write(tenantA);
        await using var conn = await sql.OpenAs("app_user", tenantA); // inside the tenant: without one, RLS hides every row and an empty chain verifies
        // MDV-P0-036: audit.verify_chain returns the id of the first broken row, NULL when the chain is intact.
        await using var verify = new NpgsqlCommand("SELECT audit.verify_chain(@tenant)", conn);
        verify.Parameters.AddWithValue("tenant", tenantA);
        Assert.True(await verify.ExecuteScalarAsync() is DBNull);
    }

    [Fact]
    public async Task AuditedEndpointWritesExactlyOneEventPerRequest()
    {
        var patient = Guid.NewGuid();
        await using var app = App(tenantA, s => s.AddAuditModule(sql.ConnectionString));
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/patients/{patient}")).StatusCode);

        await using var db = sql.Audit(tenantA);
        var row = Assert.Single(await db.Events.Where(e => e.PatientId == patient).ToListAsync());
        Assert.Equal(("patient.read", "patient", "treatment", AuditOutcome.Allowed), (row.Action, row.ResourceType, row.Purpose, row.Outcome));
    }

    [Fact]
    public async Task ARefusalWithNoTenantIsRecordedInThePlatformChainAndNoPracticeCanReadIt()
    {
        var before = await PlatformCount();
        await using var app = App(null, s => s.AddAuditModule(sql.ConnectionString));
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}")).StatusCode);

        await using var platform = sql.Audit(PlatformTenant.Id);
        Assert.Equal(before + 1, await platform.Events.CountAsync());
        var row = await platform.Events.OrderByDescending(e => e.OccurredAt).FirstAsync();
        Assert.Equal(("patient.read", AuditOutcome.Denied), (row.Action, row.Outcome));
        Assert.Null(row.PatientId); // nothing is known about a patient the caller was never allowed near
        await using var practice = sql.Audit(tenantA);
        Assert.DoesNotContain(await practice.Events.IgnoreQueryFilters().ToListAsync(), e => e.TenantId == PlatformTenant.Id); // row-level security

        await using var conn = new NpgsqlConnection(sql.ConnectionString);
        await conn.OpenAsync();
        await using var verify = new NpgsqlCommand("SELECT audit.verify_chain(@tenant)", conn);
        verify.Parameters.AddWithValue("tenant", PlatformTenant.Id);
        Assert.True(await verify.ExecuteScalarAsync() is DBNull);
    }

    [Fact]
    public async Task AnAnonymousCallLeavesNoPlatformRow()
    {
        var before = await PlatformCount();
        await using var app = App(null, s => s.AddAuditModule(sql.ConnectionString), signedIn: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(before, await PlatformCount());
    }

    [Fact]
    public async Task APolicyRefusalWithNoTenantIsRecordedInThePlatformChain()
    {
        var before = await PlatformCount();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICurrentTenant>(new PostgresFixture.FixedTenant(null)).AddSingleton<ICurrentUser>(new FixedUser());
        builder.Services.AddAuditModule(sql.ConnectionString).AddDeniedAccessAudit();
        builder.Services.AddAuthentication("t").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SignedInScheme>("t", null); // signed in, so refused (403), not challenged (401)
        builder.Services.AddAuthorization(o => o.AddPolicy("never", p => p.RequireAssertion(_ => false)));
        await using var app = builder.Build();
        app.MapGet("/refused", () => "synthetic").Audited("patient.read", "patient").RequireAuthorization("never");
        await app.StartAsync();

        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/refused")).StatusCode);
        await client.GetAsync("/refused"); // the same refusal again inside the window leaves no second row (no flooding)
        Assert.Equal(before + 1, await PlatformCount());
    }

    [Fact]
    public async Task APlatformAuditWriteThatFailsStopsTheRequest()
    {
        await using var app = App(null, s => s.AddAuditModule(sql.ConnectionString).AddSingleton<IPlatformAuditLog, DownPlatformLog>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}"));
    }

    sealed class SignedInScheme(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options, Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("t")), "t")));
    }

    sealed class DownPlatformLog : IPlatformAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }

    async Task<int> PlatformCount()
    {
        await using var platform = sql.Audit(PlatformTenant.Id);
        return await platform.Events.CountAsync();
    }

    [Fact]
    public async Task AuditedEndpointFailsClosed()
    {
        await using var noTenant = App(null, s => s.AddAuditModule(sql.ConnectionString));
        Assert.Equal(HttpStatusCode.Forbidden, (await noTenant.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}")).StatusCode);
        Assert.False(handlerRan);

        await using var broken = App(tenantA, s => s.AddScoped<IAuditLog, ThrowingLog>());
        // A read with the audit store down: the request fails and no answer (no patient data) leaves. The row is written once the
        // read has answered, so the handler has run; it changed nothing. Writes audit inside their own transaction instead, so there
        // the change itself is rolled back (WriteAuditTransactionTests, PatientEndpointTests, PatientRegistryTests).
        await Assert.ThrowsAsync<InvalidOperationException>(() => broken.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}"));
        Assert.True(handlerRan);
    }

    bool handlerRan;

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
        public Task WriteAsync(AuditEvent auditEvent, System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }

    WebApplication App(Guid? tenant, Action<IServiceCollection> audit, bool signedIn = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICurrentTenant>(new PostgresFixture.FixedTenant(tenant));
        builder.Services.AddSingleton<ICurrentUser>(new FixedUser(signedIn));
        audit(builder.Services);
        var app = builder.Build();
        app.MapGet("/patients/{patientId}", () => { handlerRan = true; return "synthetic"; }).Audited("patient.read", "patient", patientRouteKey: "patientId");
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    sealed class FixedUser(bool signedIn = true) : ICurrentUser { public Guid? UserId { get; } = signedIn ? Guid.NewGuid() : null; }
}
