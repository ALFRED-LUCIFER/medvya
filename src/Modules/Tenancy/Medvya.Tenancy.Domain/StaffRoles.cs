using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// A practice's own role (MDV-P1-008): a built-in <see cref="BaseRole"/> whose rows stay locked, plus extra capabilities from
/// <see cref="CustomRoleRules.Extras"/>. Business labels only, never a person's data.
/// </summary>
public sealed class CustomRole
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public Role BaseRole { get; set; }
    /// <summary>Comma-separated <see cref="Capability"/> names granted on top of the base role.</summary>
    [Classification(DataClass.Internal)] public string Capabilities { get; set; } = "";
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

public static class CustomRoleRules
{
    /// <summary>Built-in roles a person can be added as here. Doctors need licence verification (MDV-P1-007); hospital IT is not a practice role.</summary>
    public static readonly IReadOnlyList<Role> Assignable = [Role.Receptionist, Role.Nurse, Role.CareCoordinator, Role.PracticeAdmin];

    /// <summary>
    /// Capabilities a custom role may add to <paramref name="baseRole"/>: those the base role has no access to and practice admin has.
    /// Each is granted at practice admin's level, so a custom role can never exceed practice admin. Staff and roles administration
    /// is never granted this way: it would let a role create more privileged roles.
    /// </summary>
    public static IReadOnlyList<Capability> Extras(Role baseRole) =>
        [.. Enum.GetValues<Capability>().Where(c => c != Capability.StaffRolesIntegrations
            && RoleMatrix.Of(baseRole, c) == Access.None && RoleMatrix.Of(Role.PracticeAdmin, c) != Access.None)];

    public static bool IsValid(Role baseRole, IEnumerable<Capability> extras) =>
        Assignable.Contains(baseRole) && extras.All(Extras(baseRole).Contains);

    public static string Join(IEnumerable<Capability> extras) => string.Join(',', extras.Distinct().Order());

    public static IReadOnlyList<Capability> Split(string stored) =>
        [.. stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Capability>)];

    /// <summary>Access of a person with <paramref name="baseRole"/> plus stored extras; an extra that is not valid for the base role is ignored (fail closed).</summary>
    public static Access Effective(Role baseRole, string? storedExtras, Capability capability)
    {
        var access = RoleMatrix.Of(baseRole, capability);
        return access == Access.None && storedExtras is not null && Extras(baseRole).Contains(capability) && Split(storedExtras).Contains(capability)
            ? RoleMatrix.Of(Role.PracticeAdmin, capability) : access;
    }
}
