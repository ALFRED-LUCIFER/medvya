using System.Net;
using Medvya.Audit.Api;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-017: every patient-data access leaves one append-only, tenant-scoped, tamper-evident audit row.</summary>
public class AuditLedgerTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
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
        await using var db = sql.Audit(tenantA);
        var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"UPDATE audit.AuditEvents SET Action = 'x' WHERE Id = {a.Id}"));
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM audit.AuditEvents WHERE Id = {a.Id}"));
        Assert.Contains("append only", update.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("append only", delete.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WritingIntoAnotherTenantIsBlocked()
    {
        await using var db = sql.Audit(tenantA);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => new AuditLog(db).WriteAsync(Event(tenantB), default));
        Assert.IsType<SqlException>(ex.InnerException);
    }

    [Fact]
    public async Task LedgerDigestVerifies()
    {
        await Write(tenantA);
        await using var conn = new SqlConnection(sql.ConnectionString);
        await conn.OpenAsync();
        await using var gen = new SqlCommand("EXEC sys.sp_generate_database_ledger_digest", conn);
        var digest = (string)(await gen.ExecuteScalarAsync())!;
        await using var verify = new SqlCommand("EXEC sys.sp_verify_database_ledger @digests", conn);
        verify.Parameters.AddWithValue("@digests", $"[{digest}]");
        await verify.ExecuteNonQueryAsync(); // throws if any ledger row was tampered with
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
    public async Task AuditedEndpointFailsClosed()
    {
        await using var noTenant = App(null, s => s.AddScoped<IAuditLog, ThrowingLog>());
        Assert.Equal(HttpStatusCode.Forbidden, (await noTenant.GetTestClient().GetAsync($"/patients/{Guid.NewGuid()}")).StatusCode);

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

    WebApplication App(Guid? tenant, Action<IServiceCollection> audit)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICurrentTenant>(new SqlServerFixture.FixedTenant(tenant));
        builder.Services.AddSingleton<ICurrentUser>(new FixedUser());
        audit(builder.Services);
        var app = builder.Build();
        app.MapGet("/patients/{patientId}", () => { handlerRan = true; return "synthetic"; }).Audited("patient.read", "patient", patientRouteKey: "patientId");
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    sealed class FixedUser : ICurrentUser { public Guid? UserId { get; } = Guid.NewGuid(); }
}
