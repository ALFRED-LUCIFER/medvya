using Medvya.Platform.Domain;

namespace Medvya.Platform.Application;

/// <summary>The signed-in console member for the current request (the host reads it from the session).</summary>
public interface IPlatformCaller
{
    Guid? MemberId { get; }
    string? ClientIp { get; }
    string? CorrelationId { get; }
}

/// <summary>A console action's body: every action carries the reason that goes into the platform audit log (MDV-P5-040).</summary>
public interface IReasoned
{
    string? Reason { get; }
}

/// <summary>Input the console refused, by field and code ("required", "too_long", "taken" ...).</summary>
public sealed class PlatformValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The request is not valid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static void ThrowIfAny(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0) throw new PlatformValidationException(errors);
    }
}

public sealed record TeamMemberView(Guid Id, string DisplayName, string Email, PlatformRole Role, bool HasKey, bool Joined,
    DateTimeOffset? LastActiveAt, DateOnly? AccessEndsOn);

/// <summary>The team and the quarterly access review: due 90 days after the last one, or now when there has been none.</summary>
public sealed record TeamView(IReadOnlyList<TeamMemberView> Members, DateTimeOffset? LastAccessReviewAt, DateTimeOffset AccessReviewDueAt);

/// <summary>A one-hour key link. <see cref="Token"/> exists only in this answer: the database keeps its hash.</summary>
public sealed record IssuedLink(Guid MemberId, string Token, DateTimeOffset ExpiresAt);

public sealed record Invite(string? DisplayName, string? Email, PlatformRole Role, DateOnly? AccessEndsOn, string? Reason) : IReasoned;

/// <summary>Refused: the change is not allowed for this member (yourself, or the last owner).</summary>
public enum TeamOutcome { Done, NotFound, Refused }

public interface IPlatformTeam
{
    Task<TeamView> ListAsync(CancellationToken cancellationToken = default);
    Task<IssuedLink> InviteAsync(Invite invite, CancellationToken cancellationToken = default);
    /// <summary>Lost key: revokes the member's keys and sessions at once and issues a one-hour link to register a new key.</summary>
    Task<(TeamOutcome Outcome, IssuedLink? Link)> IssueRecoveryAsync(Guid memberId, string reason, CancellationToken cancellationToken = default);
    Task<TeamOutcome> RemoveAsync(Guid memberId, string reason, CancellationToken cancellationToken = default);
    Task RecordAccessReviewAsync(string reason, CancellationToken cancellationToken = default);
}

public sealed record AuditEntryView(Guid Id, DateTimeOffset OccurredAt, string Action, string ResourceType, Guid? ResourceId, Guid? SubjectTenantId,
    string? Actor, string? Reason);

public interface IPlatformAuditLog
{
    /// <summary>Newest first, <paramref name="limit"/> at most, after the entry (<paramref name="before"/>, <paramref name="beforeId"/>) when given.</summary>
    Task<IReadOnlyList<AuditEntryView>> ListAsync(DateTimeOffset? before, Guid? beforeId, int limit, CancellationToken cancellationToken = default);
    /// <summary>The whole log as CSV, for auditors. The export itself is logged with its reason first.</summary>
    Task<string> ExportCsvAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>Console pages that act on an organisation (MDV-P1-064) log each action here, in the platform audit log, with the organisation it concerns.</summary>
public interface IConsoleActions
{
    /// <summary>Appends an entry naming the signed-in member, the action, the organisation and the reason (null for a view).</summary>
    Task RecordAsync(string action, string resourceType, Guid resourceId, Guid subjectTenantId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Display names of console members, for a history that records them as actors. Unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> MemberNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}

public sealed record ConsoleSession(Guid MemberId, Guid SessionId);

/// <summary><see cref="CanRegister"/>: this session may register a key (it signed on with a usable link, or it is the first owner's first key).</summary>
public sealed record SessionState(Guid MemberId, string DisplayName, PlatformRole Role, bool KeyVerified, bool HasKey, bool CanRegister);

/// <summary>
/// Console sign-in (MDV-P5-040): company sign-on starts a session that can only take the security-key step; a verified key (WebAuthn)
/// opens the console. Options and responses are WebAuthn JSON, exactly as the browser's PublicKeyCredential reads and writes them.
/// </summary>
public interface IPlatformSignIn
{
    /// <summary>Null when this company account is not a console member (or the link does not fit it).</summary>
    Task<ConsoleSession?> SignOnAsync(string issuer, string subject, string? linkToken, CancellationToken cancellationToken = default);
    /// <summary>The live session, or null when it has ended, been revoked, or the member has lost access.</summary>
    Task<SessionState?> ValidateAsync(Guid sessionId, Guid memberId, bool activity, CancellationToken cancellationToken = default);
    /// <summary>Assertion options, or null when the member has no live key.</summary>
    Task<string?> BeginKeyCheckAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<bool> CompleteKeyCheckAsync(Guid sessionId, string responseJson, CancellationToken cancellationToken = default);
    /// <summary>Creation options, or null when this session may not register a key (see <see cref="SessionState.CanRegister"/>).</summary>
    Task<string?> BeginKeyRegistrationAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<bool> CompleteKeyRegistrationAsync(Guid sessionId, string responseJson, CancellationToken cancellationToken = default);
    Task SignOutAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
