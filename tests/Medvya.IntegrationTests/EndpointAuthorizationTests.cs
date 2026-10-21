using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>Deny by default (MDV-P0-014): every endpoint must state who may call it.</summary>
public class EndpointAuthorizationTests
{
    internal static List<string> Undeclared(IEnumerable<EndpointDataSource> sources) =>
        sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => e.RoutePattern.RawText ?? e.DisplayName ?? "?")
            .ToList();

    [Fact]
    public void EveryEndpointDeclaresAnAuthorizationPolicyOrIsExplicitlyPublic()
    {
        using var factory = new WebApplicationFactory<Program>();
        Assert.Empty(Undeclared(factory.Services.GetServices<EndpointDataSource>()));
    }

    sealed class ForgottenEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseRouting();
            app.UseEndpoints(e => e.MapGet("/forgotten", () => "oops"));
        };
    }

    [Fact]
    public void CheckerCatchesAnEndpointWithoutAPolicy()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IStartupFilter, ForgottenEndpoint>()));
        _ = factory.CreateClient(); // builds the pipeline so the extra endpoint registers
        Assert.Contains("/forgotten", Undeclared(factory.Services.GetServices<EndpointDataSource>()));
    }
}
