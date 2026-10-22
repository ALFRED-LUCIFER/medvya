using System.Security.Claims;
using Medvya.Audit.Api;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-017: enumerates every endpoint in the host. Each signed-in endpoint is either <c>.Audited(...)</c> (patient data)
/// or reviewed below as never touching patient data, so a new endpoint fails here until someone decides (fail closed).
/// Each audited endpoint is then called against real SQL Server once per staff role, so both the allowed path (the endpoint
/// filter, then the handler) and the refused path (denied-attempt audit) run, and every request must write exactly one audit row.
/// </summary>
public sealed class EndpointAuditTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    /// <summary>Signed-in endpoints reviewed as never reading or writing patient data. Adding one here is a reviewed decision.</summary>
    // shortcut: kept in the test, not as metadata on the endpoints, while StaffAuthentication.cs is reworked (MDV-P0-012); move to endpoint metadata if the list grows.
    static readonly HashSet<string> NoPatientData =
    [
        "POST /bff/logout",     // ends the staff session
        "GET /v1/me/context",   // the signed-in staff user, memberships and permissions
        "GET /v1/invitations/facilities",   // facility ids and names of the admin's tenant (MDV-P0-013)
        "POST /v1/invitations/",            // invites a staff member by email; audited as invitation.create inside the service
        "DELETE /v1/invitations/{id:guid}", // revokes a staff invitation; audited as invitation.revoke inside the service
    ];

    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid();
    readonly Dictionary<Role, Guid> staff = Enum.GetValues<Role>().ToDictionary(r => r, _ => Guid.NewGuid());

    /// <summary>One active member per role at one facility of a synthetic tenant, so every capability policy has a member it lets through.</summary>
    public async Task InitializeAsync()
    {
        await using var db = sql.Context(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Audit Enumeration Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
        db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
        foreach (var (role, user) in staff)
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, FacilityId = facility, Role = role, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static IEnumerable<RouteEndpoint> Endpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();

    static IEnumerable<string> Methods(RouteEndpoint e) => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"];

    static string Name(RouteEndpoint e, string method) => $"{method} {e.RoutePattern.RawText}";

    /// <summary>Signed-in endpoints that are neither audited nor reviewed as free of patient data.</summary>
    internal static List<string> Unclassified(WebApplicationFactory<Program> factory) =>
        Endpoints(factory)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<AuditedEndpointMetadata>() is null)
            .SelectMany(e => Methods(e).Select(m => Name(e, m)))
            .Where(n => !NoPatientData.Contains(n))
            .ToList();

    /// <summary>A request to an audited endpoint: who sent it, how many audit rows it wrote and the HTTP status.</summary>
    internal sealed record Call(Role Role, int Rows, int Status)
    {
        /// <summary>401/403 means authorization refused it; anything else went through the endpoint filter to the handler.</summary>
        public bool Allowed => Status is not (401 or 403);
    }

    /// <summary>Calls every audited endpoint once per staff role and returns, per endpoint, what each request did.</summary>
    async Task<Dictionary<string, List<Call>>> AuditRowsPerRequest(WebApplicationFactory<Program> factory)
    {
        var calls = new Dictionary<string, List<Call>>();
        foreach (var (role, user) in staff)
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            client.DefaultRequestHeaders.Add(SignedIn.UserHeader, user.ToString());
            foreach (var endpoint in Endpoints(factory).Where(e => e.Metadata.GetMetadata<AuditedEndpointMetadata>() is not null))
            {
                foreach (var method in Methods(endpoint))
                {
                    // shortcut: route values are fresh ids and bodies are empty JSON unless SampleBody has one; add a synthetic sample body there when an endpoint needs one.
                    var name = Name(endpoint, method);
                    using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(Path(endpoint.RoutePattern), UriKind.Relative));
                    if (method is not ("GET" or "HEAD" or "DELETE")) request.Content = new StringContent(SampleBody(name, role), System.Text.Encoding.UTF8, "application/json");
                    var before = await Count();
                    using var response = await client.SendAsync(request);
                    if (!calls.TryGetValue(name, out var list)) calls[name] = list = [];
                    list.Add(new(role, await Count() - before, (int)response.StatusCode));
                }
            }
        }
        return calls;
    }

    /// <summary>
    /// A synthetic body that gets through validation, for endpoints whose use case audits only what it actually does
    /// (<c>.AuditedByUseCase</c>): with an empty body they refuse the input before any patient data is touched, and the
    /// allowed path, where the use case must write its event, would never run. One distinct synthetic person per role, so no
    /// call stops at a duplicate warning.
    /// </summary>
    static string SampleBody(string endpoint, Role role) => endpoint switch
    {
        "POST /v1/patients/" => System.Text.Json.JsonSerializer.Serialize(new
        {
            quick = true, fullName = $"{role} Audit Probe (synthetic)", dateOfBirth = $"{1950 + (int)role}-02-0{1 + (int)role % 9}",
            sexAtBirth = "Female", mobile = $"+91 90000 09{(int)role:D3}",
        }),
        _ => "{}",
    };

    async Task<int> Count()
    {
        await using var db = sql.Audit(tenant);
        return await db.Events.CountAsync();
    }

    static string Path(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart => Guid.NewGuid().ToString(),
            _ => "",
        }))));

    WebApplicationFactory<Program> Host(Action<IEndpointRouteBuilder>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(tenant, facility, extra))));

    /// <summary>The synthetic staff user named by <see cref="UserHeader"/>, in the test tenant and facility, plus optional extra endpoints.</summary>
    // shortcut: sets the tenant and facility claims directly because sign-in does not issue them before the facility picker (MDV-P0-012).
    sealed class SignedIn(Guid tenant, Guid facility, Action<IEndpointRouteBuilder>? extra) : IStartupFilter
    {
        public const string UserHeader = "X-Test-User";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ctx.Request.Headers[UserHeader].ToString()), new Claim("tenant_id", tenant.ToString()),
                     new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
            if (extra is null) return;
            app.UseRouting();
            app.UseEndpoints(extra);
        };
    }

    [Fact]
    public void EverySignedInEndpointIsAuditedOrReviewedAsFreeOfPatientData()
    {
        using var factory = Host();
        Assert.Empty(Unclassified(factory));
    }

    [Fact]
    public async Task EveryAuditedEndpointWritesExactlyOneAuditRowPerRequest()
    {
        using var factory = Host();
        var calls = await AuditRowsPerRequest(factory);
        Assert.NotEmpty(calls);
        Assert.All(calls, e =>
        {
            Assert.All(e.Value, c => Assert.True(c.Rows == 1, $"{e.Key} as {c.Role}: {c.Rows} audit rows (HTTP {c.Status})"));
            // Without this a capability-guarded endpoint could pass on refused requests alone and its allowed path never be counted.
            Assert.True(e.Value.Exists(c => c.Allowed), $"{e.Key}: every role was refused, so the allowed path was not checked");
        });
    }

    [Fact]
    public async Task CheckerCatchesUnauditedAndUncountedPatientEndpoints()
    {
        // Not marked public, so each counts as signed-in. No RequireAuthorization: these route after the host's authorization middleware, which would refuse them.
        using var factory = Host(e =>
        {
            e.MapGet("/test/patients/{patientId}", () => "synthetic").Audited("patient.read", "patient", patientRouteKey: "patientId");
            e.MapGet("/test/unaudited/{patientId}", () => "synthetic");
            // Tagged as audited but the filter is bypassed: the count must catch it.
            e.MapGet("/test/tagged-only/{patientId}", () => "synthetic").WithMetadata(new AuditedEndpointMetadata("patient.read", "patient", "treatment", "patientId"));
        });
        _ = factory.CreateClient(); // builds the pipeline so the extra endpoints register

        Assert.Equal(["GET /test/unaudited/{patientId}"], Unclassified(factory));
        var calls = await AuditRowsPerRequest(factory);
        Assert.All(calls["GET /test/patients/{patientId}"], c => Assert.Equal((1, 200), (c.Rows, c.Status)));
        Assert.All(calls["GET /test/tagged-only/{patientId}"], c => Assert.Equal((0, 200), (c.Rows, c.Status)));
    }
}
