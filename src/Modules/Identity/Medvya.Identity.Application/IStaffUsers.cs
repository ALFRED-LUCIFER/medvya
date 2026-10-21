using Medvya.Identity.Domain;

namespace Medvya.Identity.Application;

public interface IStaffUsers
{
    /// <summary>The Medvya user for an identity provider account, or null: accounts are created by invitation, never on first sign-in.</summary>
    Task<StaffUser?> FindAsync(string issuer, string subject, CancellationToken cancellationToken = default);

    Task<StaffUser?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
