using Medvya.BuildingBlocks.Domain;

namespace Medvya.Identity.Domain;

/// <summary>
/// A person who signs in. Links the identity provider's account (issuer + subject) to Medvya's own user id, which
/// memberships use, so the provider can change (Entra External ID, hospital SSO, UAE PASS) without touching clinic data.
/// </summary>
public sealed class StaffUser
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public required string Issuer { get; init; }
    [Classification(DataClass.Internal)] public required string Subject { get; init; }
    [Classification(DataClass.Personal)] public required string DisplayName { get; set; }
}

public static class IdentityProviders
{
    /// <summary>Issuer of the local development provider (Host.Api, Development only). Seeded synthetic users sign in through it.</summary>
    public const string Development = "medvya-dev-idp";
}
