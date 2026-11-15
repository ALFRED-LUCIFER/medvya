using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

public static class FeatureGate
{
    /// <summary>
    /// The endpoint exists only for tenants with this feature on (Medvya's release and the clinic's switch): otherwise 404 (not 403), so a disabled
    /// feature is not revealed. Runs before every other endpoint filter (e.g. .Audited()), whatever the call order.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, Feature feature) where TBuilder : IEndpointConventionBuilder =>
        builder.Gate((flags, ct) => flags.IsEnabledAsync(feature, ct));

    /// <summary>As <see cref="RequireFeature"/>, but Medvya's release alone: for the clinic's own switch of the feature, which exists only once released.</summary>
    public static TBuilder RequireRelease<TBuilder>(this TBuilder builder, Feature feature) where TBuilder : IEndpointConventionBuilder =>
        builder.Gate((flags, ct) => flags.IsReleasedAsync(feature, ct));

    static TBuilder Gate<TBuilder>(this TBuilder builder, Func<IFeatureFlags, CancellationToken, Task<bool>> isOn) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(b => b.FilterFactories.Insert(0, (_, next) => async ctx =>
            await isOn(ctx.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>(), ctx.HttpContext.RequestAborted)
                ? await next(ctx)
                : Results.NotFound()));
        return builder;
    }
}
