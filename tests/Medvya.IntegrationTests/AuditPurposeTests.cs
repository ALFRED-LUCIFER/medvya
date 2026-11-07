using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P2-024: every audit row records a purpose of use from the fixed list; any other value is refused, never written.</summary>
public sealed class AuditPurposeTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void OnlyTheListedPurposesExist()
    {
        Assert.Equal(["audit", "billing", "operations", "registration", "support", "treatment"], AuditPurpose.All.Order());
        foreach (var purpose in AuditPurpose.All) Assert.Equal(purpose, AuditPurpose.Require(purpose));
        foreach (var other in new[] { "marketing", "Treatment", "", "research" })
            Assert.Throws<ArgumentOutOfRangeException>(() => AuditPurpose.Require(other));
    }

    [Fact]
    public void AnEndpointWithAnUnlistedPurposeFailsAtStartup()
    {
        var app = WebApplication.CreateBuilder().Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => app.MapGet("/x", () => Results.Ok()).Audited("patient.read", "patient", "marketing"));
        Assert.Throws<ArgumentOutOfRangeException>(() => app.MapPost("/y", () => Results.Ok()).AuditedByUseCase("patient.update", "patient", "research"));
    }

    [Fact]
    public async Task TheAuditLogRefusesAnUnlistedPurposeAndWritesNothing()
    {
        var tenant = Guid.NewGuid();
        await using (var tenancy = sql.Context(tenant))
        {
            tenancy.Tenants.Add(new Tenancy.Domain.Tenant { Id = tenant, Name = "Purpose Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await tenancy.SaveChangesAsync();
        }
        await using var db = sql.Audit(tenant);
        var e = new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, Action = "patient.read", ResourceType = "patient", Purpose = "marketing",
            Outcome = AuditOutcome.Allowed, OccurredAt = DateTimeOffset.UtcNow,
        };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new AuditLog(db).WriteAsync(e));
        await using var check = sql.Audit(tenant);
        Assert.False(await check.Events.AnyAsync());
    }
}
