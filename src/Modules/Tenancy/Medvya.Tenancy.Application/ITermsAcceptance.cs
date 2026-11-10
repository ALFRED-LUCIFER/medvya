namespace Medvya.Tenancy.Application;

public static class TermsPolicy
{
    /// <summary>
    /// The Terms of Service and DPA wording an owner accepts. One version for both. Bumped to -2 for the organisation's licence responsibility (MDV-P1-067) so every owner re-accepts through the terms gate; no longer shared with the consent wording version.
    /// shortcut: DRAFT text, not legally reviewed (MDV-P1-044), one built-in version; go-live needs the reviewed en/hi/ar text.
    /// </summary>
    public const string CurrentVersion = "2026-11-draft-2";
}

public sealed record TermsStatus(string Version, bool Accepted, DateTimeOffset? AcceptedAt, bool Required);

/// <summary>
/// The tenant's accepted-terms record (MDV-P1-019, decision 5 Nov 2026): until the owner accepts the current version the tenant cannot
/// book appointments or send messages. Enforced server-side where booking and sending happen (<c>Terms:RequireAcceptance</c>, off only in Development).
/// </summary>
public interface ITermsAcceptance
{
    /// <summary>True when acceptance is not required in this deployment, or the current version is accepted. False without a tenant.</summary>
    Task<bool> IsSatisfiedAsync(CancellationToken cancellationToken = default);

    Task<TermsStatus> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records acceptance of the current version by the signed-in owner (practice admin or owner-doctor), audited in the same transaction.
    /// Repeating it adds nothing. False when the caller is not an owner.
    /// </summary>
    Task<bool> AcceptAsync(CancellationToken cancellationToken = default);
}
