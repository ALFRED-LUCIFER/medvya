namespace Medvya.Tenancy.Domain;

/// <summary>An organisation's standing with Medvya (ADR-016, MDV-P1-063). Only Approved is fully open; see <see cref="OrganisationRules"/>.</summary>
public enum OrganisationState { Draft, Submitted, NeedsChanges, Approved, Rejected, Suspended }

public static class OrganisationRules
{
    /// <summary>What an organisation that is not Approved can still do: set itself up (settings, staff) and read its audit log.</summary>
    static readonly HashSet<Capability> SetUp = new HashSet<Capability> { Capability.StaffRolesIntegrations, Capability.AuditLog };

    /// <summary>
    /// The access the role's matrix grants, narrowed by the organisation's state. Before approval (Draft, Submitted, NeedsChanges, Rejected)
    /// only set-up capabilities stay, so no patient data can exist. Suspended keeps reading existing records and refuses anything that
    /// creates or signs (Full and Sign become Read).
    /// </summary>
    public static Access Apply(Access access, Capability capability, OrganisationState state) => state switch
    {
        OrganisationState.Approved => access,
        OrganisationState.Suspended => access is Access.Full or Access.Sign ? Access.Read : access,
        _ => SetUp.Contains(capability) ? access : Access.None,
    };
}
