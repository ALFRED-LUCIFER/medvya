using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>The onboarding request of the current organisation (MDV-P1-062); see <see cref="IOrganisationRequests"/>.</summary>
public sealed class OrganisationRequests(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IOrganisationState state, ILogoStore files, IAuditLog audit, TimeProvider clock)
    : IOrganisationRequests
{
    // shortcut: the private container is the logo store's; a file is checked by signature and size only, with no malware scan yet.
    // Add the Defender scan the clinical documents use (follow-up criterion of MDV-P2-006) before the first real applicant uploads, and move to its own container.
    static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<RequestView?> GetAsync(CancellationToken cancellationToken = default)
    {
        var request = await db.OrganisationRequests.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (request is null) return null;
        var current = await db.Tenants.AsNoTracking().Select(t => t.State).SingleAsync(cancellationToken);
        var documents = await db.OrganisationDocuments.AsNoTracking().Where(d => !d.Kind.StartsWith("old-")).OrderBy(d => d.UploadedAt)
            .Select(d => new OrganisationDocumentView(d.Id, d.Kind, d.ContentType, d.SizeBytes, d.UploadedAt)).ToListAsync(cancellationToken);
        return new RequestView(request.Type, current, request.LastReason, request.ItContactName, request.ItContactEmail, request.SubmittedAt,
            OrganisationDocumentRules.Required(request.Type, await OwnerIsDoctorAsync(cancellationToken)), documents);
    }

    public async Task SetItContactAsync(SaveItContact contact, CancellationToken cancellationToken = default)
    {
        var request = await EditableAsync(cancellationToken);
        var (name, email) = (contact.Name?.Trim(), contact.Email?.Trim());
        var errors = new List<ValidationError>();
        if (request.Type != OrganisationType.Hospital) errors.Add(new("itContact", "notAsked"));
        ItContactErrors(name, email, errors);
        SettingsValidationException.ThrowIfAny(errors);
        (request.ItContactName, request.ItContactEmail) = (name, email);
        await SettingsAudit.SaveAsync(db, audit, Tenant(), user.UserId, clock.GetUtcNow(), "organisation.request.itcontact", "organisation", Tenant(), null, new { itContact = "set" }, cancellationToken);
    }

    public async Task<OrganisationDocumentView> PutDocumentAsync(string kind, byte[] content, CancellationToken cancellationToken = default)
    {
        var request = await EditableAsync(cancellationToken);
        var errors = new List<ValidationError>();
        var contentType = OrganisationDocumentRules.ContentTypeOf(content);
        if (!OrganisationDocumentRules.Required(request.Type, ownerIsDoctor: true).Contains(kind)) errors.Add(new("kind", "unknown")); // the longest list of the type
        if (content.Length > OrganisationDocumentRules.MaxBytes) errors.Add(new("file", "too_large"));
        else if (contentType is null) errors.Add(new("file", "type"));
        SettingsValidationException.ThrowIfAny(errors);

        var tenantId = Tenant();
        var now = clock.GetUtcNow();
        var document = await db.OrganisationDocuments.SingleOrDefaultAsync(d => d.Kind == kind, cancellationToken);
        var replaced = document?.BlobKey;
        var blobKey = $"onboarding/{tenantId}/{Guid.CreateVersion7()}";
        var uploader = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        await files.PutAsync(blobKey, content, contentType!, cancellationToken);
        // A new file of the same kind replaces the row in place (documents are never deleted from the database).
        if (document is null)
            db.OrganisationDocuments.Add(document = new OrganisationDocument { Id = Guid.CreateVersion7(), TenantId = tenantId, Kind = kind, ContentType = contentType!, BlobKey = blobKey });
        (document.ContentType, document.SizeBytes, document.BlobKey, document.UploadedAt, document.UploadedBy) = (contentType!, content.Length, blobKey, now, uploader);
        await SettingsAudit.SaveAsync(db, audit, tenantId, user.UserId, now, "organisation.request.document", "organisation", document.Id, null, new { kind }, cancellationToken);
        if (replaced is not null) await files.DeleteAsync(replaced, cancellationToken);
        return new OrganisationDocumentView(document.Id, kind, contentType!, content.Length, now);
    }

    public async Task SubmitAsync(CancellationToken cancellationToken = default)
    {
        var request = await EditableAsync(cancellationToken);
        var have = await db.OrganisationDocuments.Select(d => d.Kind).ToListAsync(cancellationToken);
        var errors = OrganisationDocumentRules.Required(request.Type, await OwnerIsDoctorAsync(cancellationToken))
            .Where(k => !have.Contains(k)).Select(k => new ValidationError($"document.{k}", "required")).ToList();
        if (request.Type == OrganisationType.Hospital) ItContactErrors(request.ItContactName, request.ItContactEmail, errors);
        SettingsValidationException.ThrowIfAny(errors);
        request.SubmittedAt = clock.GetUtcNow();
        await state.SetAsync(OrganisationState.Submitted, null, cancellationToken); // saves the request's change with the state and its audit row
    }

    public async Task StartNewAsync(CancellationToken cancellationToken = default)
    {
        var request = await db.OrganisationRequests.SingleOrDefaultAsync(cancellationToken) ?? throw new SettingsValidationException([new("request", "missing")]);
        if (await db.Tenants.Select(t => t.State).SingleAsync(cancellationToken) != OrganisationState.Rejected) throw new SettingsValidationException([new("request", "notRejected")]);
        // Documents are never deleted (the table is append and update only): the old files move aside under a unique "old-" kind, so the new request
        // asks for each kind again and Medvya can still open the rejected request's files. shortcut: the applicant no longer sees them; add an archive view if it is needed.
        foreach (var d in await db.OrganisationDocuments.ToListAsync(cancellationToken)) d.Kind = $"old-{Guid.NewGuid():N}"[..12] + $"-{d.Kind}";
        (request.SubmittedAt, request.ItContactName, request.ItContactEmail) = (null, null, null);
        await state.SetAsync(OrganisationState.Draft, null, cancellationToken); // saves these changes with the state and its audit row
    }

    public async Task<RequestDetail> DetailAsync(CancellationToken cancellationToken = default)
    {
        var request = await db.OrganisationRequests.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw new SettingsValidationException([new("request", "missing")]);
        var tenantRow = await db.Tenants.AsNoTracking().SingleAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var documents = await db.OrganisationDocuments.AsNoTracking().OrderBy(d => d.UploadedAt)
            .Select(d => new OrganisationDocumentView(d.Id, d.Kind, d.ContentType, d.SizeBytes, d.UploadedAt)).ToListAsync(cancellationToken);
        // The state changes recorded by IOrganisationState, oldest first: {"state":"NeedsChanges","reason":"..."}.
        var changes = await db.SettingsChanges.AsNoTracking().Where(c => c.Section == "organisation" && c.After != null).OrderBy(c => c.OccurredAt).ToListAsync(cancellationToken);
        var history = changes.Select(c =>
        {
            using var json = System.Text.Json.JsonDocument.Parse(c.After!);
            var state = json.RootElement.TryGetProperty("state", out var s) ? s.GetString() : null;
            var reason = json.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString() : null;
            return state is null ? null : new HistoryEntry(c.OccurredAt, c.ActorUserId, state, reason);
        }).OfType<HistoryEntry>().ToList();
        return new RequestDetail(tenantRow.Name, tenantRow.CountryCode, request.Type, tenantRow.State, request.SubmittedAt, request.LastReason, profile?.Address, profile?.Email,
            profile?.RegistrationNumber, profile?.TaxNumber, request.ItContactName, request.ItContactEmail, documents, history);
    }

    public async Task<Uri?> DocumentLinkAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (await db.OrganisationDocuments.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.BlobKey).SingleOrDefaultAsync(cancellationToken) is not { } key) return null;
        // Opening an applicant's document is an access: audited in the organisation, naming the operator, before the link exists.
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = Tenant(), ActorUserId = user.UserId, Action = "organisation.document.view", ResourceType = "organisation",
            ResourceId = documentId, Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
        }, cancellationToken);
        return await files.LinkForAsync(key, LinkLifetime, cancellationToken);
    }

    static void ItContactErrors(string? name, string? email, List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(name)) errors.Add(new("itContact.name", "required"));
        else if (name.Length > 200) errors.Add(new("itContact.name", "tooLong"));
        if (string.IsNullOrEmpty(email)) errors.Add(new("itContact.email", "required"));
        else if (!OnboardingRules.IsBareEmail(email)) errors.Add(new("itContact.email", "format"));
    }

    Guid Tenant() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    Task<bool> OwnerIsDoctorAsync(CancellationToken ct) => db.Memberships.AnyAsync(m => m.Role == Role.SoloDoctor, ct);

    /// <summary>The request, only while it can still be edited: Draft, or NeedsChanges after Medvya asked for changes.</summary>
    async Task<OrganisationRequest> EditableAsync(CancellationToken ct)
    {
        var request = await db.OrganisationRequests.SingleOrDefaultAsync(ct) ?? throw new SettingsValidationException([new("request", "missing")]);
        if (await db.Tenants.Select(t => t.State).SingleAsync(ct) is not (OrganisationState.Draft or OrganisationState.NeedsChanges))
            throw new SettingsValidationException([new("request", "locked")]);
        return request;
    }
}
