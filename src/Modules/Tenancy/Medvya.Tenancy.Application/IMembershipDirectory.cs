using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>One active membership as the facility picker shows it.</summary>
public sealed record MembershipChoice(Guid Id, Guid TenantId, string TenantName, Guid FacilityId, string FacilityName, Role Role, int IdleTimeoutMinutes);

/// <summary>The signed-in user's active memberships across every tenant (facility picker, MDV-P0-012).</summary>
public interface IMembershipDirectory
{
    Task<IReadOnlyList<MembershipChoice>> ForCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the user has a suspended or offboarded membership: they lost access, as opposed to never having had any (F-05b).</summary>
    Task<bool> HasEndedMembershipAsync(CancellationToken cancellationToken = default);
}
