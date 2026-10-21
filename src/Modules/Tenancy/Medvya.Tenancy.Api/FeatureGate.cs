using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

public static class FeatureGate
{
    /// <summary>
    /// The endpoint exists only for tenants with this flag on: otherwise 404 (not 403), so a disabled feature is not revealed.
    /// Runs before every other endpoint filter (e.g. .Audited()), whatever the call order.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(b => b.FilterFactories.Insert(0,(_, next) => async ctx =>
            await ctx.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>().IsEnabledAsync(key, ctx.HttpContext.RequestAborted)
                ? await next(ctx)
                : Results.NotFound()));
        return builder;
    }
}
