using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

public sealed record OrganisationDocumentView(Guid Id, string Kind, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

/// <summary>The applicant's view of the onboarding request (ADR-016). <see cref="Reason"/> is what Medvya last said when it asked for changes, rejected or suspended.</summary>
public sealed record RequestView(
    OrganisationType Type, OrganisationState State, string? Reason, string? ItContactName, string? ItContactEmail, DateTimeOffset? SubmittedAt,
    IReadOnlyList<string> RequiredDocuments, IReadOnlyList<OrganisationDocumentView> Documents);

public sealed record SaveItContact(string? Name, string? Email);

/// <summary>
/// The onboarding request of the current organisation (MDV-P1-062): hospital IT contact, documents in private Blob, and submit. The caller's
/// capability is checked by the endpoints; the organisation's state changes only through <see cref="IOrganisationState"/>.
/// Errors are <see cref="SettingsValidationException"/> with field and code only.
/// </summary>
public interface IOrganisationRequests
{
    /// <summary>Null for an organisation without a request (one that existed before ADR-016 and was migrated to Approved).</summary>
    Task<RequestView?> GetAsync(CancellationToken cancellationToken = default);

    Task SetItContactAsync(SaveItContact contact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the file of one kind, replacing an earlier one. Allowed only while the request can still be edited (Draft, NeedsChanges).
    /// The type comes from the bytes (PDF, PNG, JPEG), at most <see cref="OrganisationDocumentRules.MaxBytes"/>.
    /// </summary>
    /// <exception cref="SettingsValidationException">"kind:unknown", "file:type", "file:too_large", "request:locked".</exception>
    Task<OrganisationDocumentView> PutDocumentAsync(string kind, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Draft or NeedsChanges to Submitted when every required document and field is there.</summary>
    /// <exception cref="SettingsValidationException">"request:locked", "document.{kind}:required", "itContact.name:required", "itContact.email:format".</exception>
    Task SubmitAsync(CancellationToken cancellationToken = default);

    /// <summary>The whole request as Medvya's reviewer reads it: profile, documents and history. Business details only, never patient data.</summary>
    Task<RequestDetail> DetailAsync(CancellationToken cancellationToken = default);

    /// <summary>A short-lived read link to one document. For Medvya operations only: the endpoint is behind the approval capability.</summary>
    Task<Uri?> DocumentLinkAsync(Guid documentId, CancellationToken cancellationToken = default);
}

public sealed record HistoryEntry(DateTimeOffset At, Guid? ActorUserId, string State, string? Reason);

/// <summary>The reviewer's detail (console design, 10 Nov 2026): the organisation's profile, its documents and who decided what.</summary>
public sealed record RequestDetail(
    string Name, string Country, OrganisationType Type, OrganisationState State, DateTimeOffset? SubmittedAt, string? Reason, string? Address, string? ContactEmail,
    string? RegistrationNumber, string? TaxNumber, string? ItContactName, string? ItContactEmail, IReadOnlyList<OrganisationDocumentView> Documents, IReadOnlyList<HistoryEntry> History);

/// <summary>The queue's filters (console design): Waiting is Submitted, Decided is Approved, Rejected or Suspended.</summary>
public enum ReviewFilter { Waiting, NeedsChanges, Decided, All }

public static class ReviewFilters
{
    public static IReadOnlyList<OrganisationState> States(ReviewFilter filter) => filter switch
    {
        ReviewFilter.Waiting => [OrganisationState.Submitted],
        ReviewFilter.NeedsChanges => [OrganisationState.NeedsChanges],
        ReviewFilter.Decided => [OrganisationState.Approved, OrganisationState.Rejected, OrganisationState.Suspended],
        _ => [.. Enum.GetValues<OrganisationState>()],
    };
}

/// <summary>One organisation in Medvya's review queue: control-plane facts and document metadata, never clinic data.</summary>
public sealed record QueueItem(
    Guid TenantId, string Name, string Country, OrganisationType Type, OrganisationState State, DateTimeOffset? SubmittedAt, string? Reason,
    string? ItContactName, string? ItContactEmail, IReadOnlyList<OrganisationDocumentView> Documents);

/// <summary>
/// Reads requests across organisations for Medvya operations, inside row-level security: its own connection carries <c>medvya.operator</c>
/// and no tenant, and the policies on the tenant, request and document tables (migration OrganisationRequests) then show those rows only.
/// </summary>
public interface IOrganisationReviews
{
    /// <summary>Organisations whose state is in <paramref name="filter"/>, oldest submission first.</summary>
    Task<IReadOnlyList<QueueItem>> ListAsync(ReviewFilter filter, CancellationToken cancellationToken = default);
}
