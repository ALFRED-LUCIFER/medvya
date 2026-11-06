using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

public static class TermsGate
{
    /// <summary>The endpoint is refused (403, code terms_not_accepted) until the tenant's owner accepts the current terms (MDV-P1-019). Runs before the other filters.</summary>
    public static TBuilder RequireAcceptedTerms<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(b => b.FilterFactories.Insert(0, (_, next) => async ctx =>
            await ctx.HttpContext.RequestServices.GetRequiredService<ITermsAcceptance>().IsSatisfiedAsync(ctx.HttpContext.RequestAborted)
                ? await next(ctx)
                : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Terms not accepted", extensions: new Dictionary<string, object?> { ["code"] = "terms_not_accepted" })));
        return builder;
    }
}
