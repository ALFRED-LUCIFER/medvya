using System.Security.Claims;
using Medvya.Audit.Api;
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
/// Each audited endpoint is then called against real SQL Server and must write exactly one audit row per request.
/// </summary>
public sealed class EndpointAuditTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    /// <summary>Signed-in endpoints reviewed as never reading or writing patient data. Adding one here is a reviewed decision.</summary>
    // shortcut: kept in the test, not as metadata on the endpoints, while StaffAuthentication.cs is reworked (MDV-P0-012); move to endpoint metadata if the list grows.
    static readonly HashSet<string> NoPatientData =
    [
        "POST /bff/logout",     // ends the staff session
        "GET /v1/me/context",   // the signed-in staff user, memberships and permissions
        // Session control (MDV-P0-012): staff sessions only; each change writes its own auth/session audit event (SessionControl).
        "POST /v1/me/facility",
        "GET /v1/me/sessions",
        "DELETE /v1/me/sessions/{id:guid}",
        "DELETE /v1/staff/{userId:guid}/sessions",
    ];

    readonly Guid tenant = Guid.NewGuid();

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

    /// <summary>Calls every audited endpoint once as a synthetic signed-in user and returns how many audit rows each request wrote.</summary>
    async Task<Dictionary<string, (int Rows, int Status)>> AuditRowsPerRequest(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var rows = new Dictionary<string, (int Rows, int Status)>();
        foreach (var endpoint in Endpoints(factory).Where(e => e.Metadata.GetMetadata<AuditedEndpointMetadata>() is not null))
        {
            foreach (var method in Methods(endpoint))
            {
                // shortcut: route values are fresh ids and bodies are empty JSON; give an endpoint a synthetic sample body here when it needs one to pass binding.
                using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(Path(endpoint.RoutePattern), UriKind.Relative));
                if (method is not ("GET" or "HEAD" or "DELETE")) request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                var before = await Count();
                using var response = await client.SendAsync(request);
                rows[Name(endpoint, method)] = (await Count() - before, (int)response.StatusCode);
            }
        }
        return rows;
    }

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
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(tenant, extra))));

    /// <summary>A synthetic staff user in the test tenant on every request, plus optional extra endpoints.</summary>
    // shortcut: sets the tenant claim directly because sign-in does not issue one before the facility picker (MDV-P0-012).
    sealed class SignedIn(Guid tenant, Action<IEndpointRouteBuilder>? extra) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim("tenant_id", tenant.ToString())], "synthetic"));
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
        Assert.All(await AuditRowsPerRequest(factory), r => Assert.True(r.Value.Rows == 1, $"{r.Key}: {r.Value.Rows} audit rows (HTTP {r.Value.Status})"));
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
        var rows = await AuditRowsPerRequest(factory);
        Assert.Equal((1, 200), rows["GET /test/patients/{patientId}"]);
        Assert.Equal((0, 200), rows["GET /test/tagged-only/{patientId}"]);
    }
}
