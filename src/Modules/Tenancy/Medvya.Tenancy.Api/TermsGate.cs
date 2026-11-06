using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

public static class TermsGate
{
    /// <summary>
    /// The endpoint is refused (403, code terms_not_accepted) until the tenant's owner accepts the current terms (MDV-P1-019).
    /// Call it after the endpoint's audit filter: it runs inside it, so a refused attempt is audited Denied with the reason.
    /// </summary>
    public static TBuilder RequireAcceptedTerms<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            if (await ctx.HttpContext.RequestServices.GetRequiredService<ITermsAcceptance>().IsSatisfiedAsync(ctx.HttpContext.RequestAborted)) return await next(ctx);
            ctx.HttpContext.Items["audit.detail"] = "terms_not_accepted";
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Terms not accepted", extensions: new Dictionary<string, object?> { ["code"] = "terms_not_accepted" });
        });
}
