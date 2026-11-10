using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-063 and MDV-P1-068 on real PostgreSQL under row-level security: until the organisation is Approved only set-up works (the server
/// answers 403 on patient endpoints, whatever the role), Suspended is read-only, and every state change is audited. Mutation check: remove
/// OrganisationRules.Apply from StaffAccess and the 403 steps fail.
/// </summary>
public sealed class OrganisationStateTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Role role)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", h.ByRole[role].ToString());
        return client;
    }

    async Task SetStateAsync(OrganisationState state)
    {
        await using var db = sql.Context(h.Tenant);
        await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(t => t.State, state));
    }

    sealed class Who(Guid id) : ICurrentUser { public Guid? UserId => id; }
    sealed class At(Guid id) : ICurrentFacility { public Guid? FacilityId => id; }

    async Task<Access> AccessAsync(Role role, Capability capability)
    {
        await using var db = sql.Context(h.Tenant);
        return await new StaffAccess(db, new FacilityDirectory(db), new Who(h.ByRole[role]), new At(h.Facility)).AccessAsync(capability);
    }

    [Theory]
    [InlineData(OrganisationState.Draft)]
    [InlineData(OrganisationState.Submitted)]
    [InlineData(OrganisationState.NeedsChanges)]
    [InlineData(OrganisationState.Rejected)]
    public async Task UntilApprovedPatientEndpointsAreRefusedForEveryRoleAndSetUpStillWorks(OrganisationState state)
    {
        await SetStateAsync(state);
        foreach (var role in new[] { Role.Receptionist, Role.PracticeAdmin, Role.HospitalDoctor })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/patients/search", new { })).StatusCode);
        }
        using var admin = Client(Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/v1/settings/profile")).StatusCode); // set-up: profile, staff, hours
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/v1/settings/doctors")).StatusCode);

        await SetStateAsync(OrganisationState.Approved);
        using var desk = Client(Role.Receptionist);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/v1/patients/search", new { })).StatusCode); // the same call works once approved
    }

    [Fact]
    public async Task SuspendedKeepsExistingRecordsReadableAndRefusesNewRegistrationAndSigning()
    {
        await SetStateAsync(OrganisationState.Suspended);
        Assert.Equal(Access.Read, await AccessAsync(Role.Receptionist, Capability.RegisterBookPatients)); // was Full: open a record, no new registration or booking
        Assert.Equal(Access.Read, await AccessAsync(Role.HospitalDoctor, Capability.Patient360Chart)); // was Full
        Assert.Equal(Access.Read, await AccessAsync(Role.HospitalDoctor, Capability.ClinicalNotes)); // was Sign: no signing
        Assert.Equal(Access.Config, await AccessAsync(Role.PracticeAdmin, Capability.StaffRolesIntegrations));
        await SetStateAsync(OrganisationState.Approved);
        Assert.Equal(Access.Full, await AccessAsync(Role.Receptionist, Capability.RegisterBookPatients));
        Assert.Equal(Access.Sign, await AccessAsync(Role.HospitalDoctor, Capability.ClinicalNotes));
    }

    [Fact]
    public async Task ATenantRowThatCannotBeReadFailsClosedAsDraft()
    {
        await SetStateAsync(OrganisationState.Draft);
        await using var other = sql.Context(Guid.NewGuid());
        Assert.Equal(OrganisationState.Draft, await new StaffAccess(other, new FacilityDirectory(other), new Who(h.Admin), new At(h.Facility)).OrganisationStateAsync()); // no readable tenant row fails closed
    }

    [Fact]
    public async Task ChangingTheStateIsAuditedWithTheActorAndAReasonWhereOneIsRequired()
    {
        await SetStateAsync(OrganisationState.Submitted);
        OrganisationStates States(Guid user, TenancyDbContext db) =>
            new(db, new PostgresFixture.FixedTenant(h.Tenant), new Who(user), new Medvya.Audit.Infrastructure.AuditLog(sql.Audit(h.Tenant)), TimeProvider.System);

        await using (var db = sql.Context(h.Tenant))
        {
            await Assert.ThrowsAsync<SettingsValidationException>(() => States(h.Admin, db).SetAsync(OrganisationState.Rejected, "  ")); // a reason is required
            await States(h.Admin, db).SetAsync(OrganisationState.NeedsChanges, "Registration certificate is unreadable");
            await States(h.Admin, db).SetAsync(OrganisationState.NeedsChanges, "again"); // already there: nothing more is written
        }
        await using (var db = sql.Context(h.Tenant))
            await States(h.Admin, db).SetAsync(OrganisationState.Approved, null);

        await using var check = sql.Context(h.Tenant);
        Assert.Equal(OrganisationState.Approved, await new OrganisationStates(check, new PostgresFixture.FixedTenant(h.Tenant), new Who(h.Admin), null!, TimeProvider.System).CurrentAsync());
        var changes = await check.SettingsChanges.Where(c => c.Section == "organisation").OrderBy(c => c.OccurredAt).ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.Contains("Registration certificate is unreadable", changes[0].After, StringComparison.Ordinal);
        Assert.All(changes, c => Assert.Equal(h.Admin, c.ActorUserId));
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(["organisation.state.Approved", "organisation.state.NeedsChanges"], (await audit.Events.Where(e => e.Action.StartsWith("organisation.state.")).Select(e => e.Action).ToListAsync()).Order());
    }
}
