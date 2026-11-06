using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Named capability policies. They go through <see cref="CapabilityHandler"/> (IStaffAccess), so the membership, its role and a
/// practice-defined role's extras are read from the database on every request, never from the "role" claim (MDV-P1-008).
/// </summary>
public static class StaffPolicies
{
    public const string TenantClaim = "tenant_id";

    /// <summary>Staff, roles and integrations at Config level: practice admin, solo doctor (own practice), hospital IT.</summary>
    public const string ManageStaff = "staff.manage";

    public static AuthorizationBuilder AddStaffPolicies(this AuthorizationBuilder builder) =>
        builder.AddPolicy(ManageStaff, p => p.RequireAuthenticatedUser()
            .AddRequirements(new CapabilityRequirement(Capability.StaffRolesIntegrations, [Access.Config])));
}
