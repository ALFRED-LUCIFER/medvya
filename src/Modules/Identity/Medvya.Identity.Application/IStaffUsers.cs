using Medvya.Identity.Domain;

namespace Medvya.Identity.Application;

public interface IStaffUsers
{
    /// <summary>The Medvya user for an identity provider account, or null: accounts are created by invitation, never on first sign-in.</summary>
    Task<StaffUser?> FindAsync(string issuer, string subject, CancellationToken cancellationToken = default);

    Task<StaffUser?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Medvya user for a provider account, created if new. Only for accepting a valid invitation (MDV-P0-013).
    /// Safe under races: a concurrent create for the same account returns the one that won.
    /// </summary>
    Task<StaffUser> FindOrCreateAsync(string issuer, string subject, string displayName, CancellationToken cancellationToken = default);
}
