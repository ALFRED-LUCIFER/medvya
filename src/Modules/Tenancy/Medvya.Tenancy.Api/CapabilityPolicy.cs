using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

/// <summary>The signed-in user's active membership must grant <see cref="Capability"/> at one of <see cref="Levels"/>.</summary>
public sealed record CapabilityRequirement(Capability Capability, IReadOnlyList<Access> Levels) : IAuthorizationRequirement;

/// <summary>Checks the role-permission matrix server-side (PRD v2 P0-05). Not registered = never satisfied (fail closed).</summary>
public sealed class CapabilityHandler(IStaffAccess access) : AuthorizationHandler<CapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CapabilityRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (requirement.Levels.Contains(await access.AccessAsync(requirement.Capability))) context.Succeed(requirement);
    }
}

public static class CapabilityPolicy
{
    /// <summary>Registers the handler; call it with the Tenancy module (it needs <see cref="IStaffAccess"/>).</summary>
    public static IServiceCollection AddCapabilityPolicies(this IServiceCollection services) =>
        services.AddScoped<IAuthorizationHandler, CapabilityHandler>();

    /// <summary>
    /// Signed in, and the active membership's role grants <paramref name="capability"/> at one of <paramref name="levels"/>.
    /// Tenant, facility and role are checked here; patient-level checks (facility of the record, care relationship) stay in the use case.
    /// </summary>
    public static TBuilder RequireCapability<TBuilder>(this TBuilder builder, Capability capability, params Access[] levels)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().AddRequirements(new CapabilityRequirement(capability, levels)));
}
