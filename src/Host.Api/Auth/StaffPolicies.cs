using System.Security.Claims;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Host.Api.Auth;

/// <summary>
/// Capability policies from the role-permission matrix (RoleMatrix), checked against the session's active membership:
/// the "tenant_id" and "role" claims, which the facility picker (MDV-P0-012) puts in the session.
/// </summary>
public static class StaffPolicies
{
    public const string TenantClaim = "tenant_id", RoleClaim = "role";

    /// <summary>Staff, roles and integrations at Config level: practice admin, solo doctor (own practice), hospital IT.</summary>
    public const string ManageStaff = "staff.manage";

    public static AuthorizationBuilder AddStaffPolicies(this AuthorizationBuilder builder) =>
        builder.AddPolicy(ManageStaff, p => p.RequireAuthenticatedUser()
            .RequireAssertion(ctx => Has(ctx.User, Capability.StaffRolesIntegrations, Access.Config)));

    static bool Has(ClaimsPrincipal user, Capability capability, Access access) =>
        Guid.TryParse(user.FindFirstValue(TenantClaim), out _)
        && Enum.TryParse<Role>(user.FindFirstValue(RoleClaim), out var role) && Enum.IsDefined(role)
        && RoleMatrix.Of(role, capability) == access;
}
