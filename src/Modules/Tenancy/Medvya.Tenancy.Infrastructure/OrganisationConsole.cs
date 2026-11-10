using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The operator and organisation of one console call (ADR-014 note, MDV-P1-064). The console host has no clinic session: each call opens a fresh DI
/// scope and names the operator (a platform member) and, for a decision or a detail, the organisation, so the tenant services, row-level security
/// and the audit trail see exactly that tenant and that actor.
/// </summary>
public sealed class ReviewContext : ICurrentTenant, ICurrentUser, ILinkTenant
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public void UseLinkTenant(Guid tenantId) => TenantId = tenantId;
    internal void Enter(Guid operatorId, Guid? tenantId) => (UserId, TenantId) = (operatorId, tenantId);
}

/// <summary>The console's use of the review path; see <see cref="IOrganisationConsole"/>.</summary>
public sealed class OrganisationConsole(IServiceScopeFactory scopes, HttpClient? client = null) : IOrganisationConsole
{
    // One client for the process: the storage link is fetched here, so the browser never holds it. A host or test may register its own.
    static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly HttpClient Storage = client ?? Shared;
    static readonly string[] ViewableTypes = ["application/pdf", "image/png", "image/jpeg"];

    AsyncServiceScope Open(Guid? tenantId, Guid operatorId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ReviewContext>().Enter(operatorId, tenantId);
        return scope;
    }

    public async Task<IReadOnlyList<QueueItem>> ListAsync(ReviewFilter filter, Guid operatorId, CancellationToken cancellationToken = default)
    {
        await using var scope = Open(null, operatorId);
        return await scope.ServiceProvider.GetRequiredService<IOrganisationReviews>().ListAsync(filter, cancellationToken);
    }

    public async Task<RequestDetail?> DetailAsync(Guid tenantId, Guid operatorId, CancellationToken cancellationToken = default)
    {
        await using var scope = Open(tenantId, operatorId);
        try { return await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DetailAsync(cancellationToken); }
        catch (InvalidOperationException) { return null; } // no readable tenant row: unknown id
        catch (SettingsValidationException) { return null; } // an organisation without a request (migrated)
    }

    public async Task<OrganisationFile?> OpenDocumentAsync(Guid tenantId, Guid documentId, Guid operatorId, CancellationToken cancellationToken = default)
    {
        await using var scope = Open(tenantId, operatorId);
        Uri? link;
        try { link = await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DocumentLinkAsync(documentId, cancellationToken); }
        catch (InvalidOperationException) { return null; }
        if (link is null) return null;
        using var response = await Storage.GetAsync(link, cancellationToken);
        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType;
        return new OrganisationFile(await response.Content.ReadAsByteArrayAsync(cancellationToken), type is not null && ViewableTypes.Contains(type) ? type : "application/octet-stream");
    }

    public async Task<EmailPreview?> DecideAsync(Guid tenantId, ReviewAction action, string reason, Guid operatorId, CancellationToken cancellationToken = default)
    {
        var (to, from) = action switch
        {
            ReviewAction.Approve => (OrganisationState.Approved, OrganisationState.Submitted),
            ReviewAction.Reinstate => (OrganisationState.Approved, OrganisationState.Suspended),
            ReviewAction.RequestChanges => (OrganisationState.NeedsChanges, (OrganisationState?)null),
            ReviewAction.Reject => (OrganisationState.Rejected, null),
            _ => (OrganisationState.Suspended, null),
        };
        // The same limits as the interim API and the console's reason filter: plain words, never personal details.
        if (reason.Trim() is not { Length: >= 5 and <= 500 } clean) throw new SettingsValidationException([new("reason", reason.Trim().Length < 5 ? "too_short" : "too_long")]);

        await using var scope = Open(tenantId, operatorId);
        var states = scope.ServiceProvider.GetRequiredService<IOrganisationState>();
        OrganisationState current;
        try { current = await states.CurrentAsync(cancellationToken); } catch (InvalidOperationException) { return null; } // unknown id: no tenant row under row-level security
        // Approve is for a request in review and Reinstate for a suspended organisation: the same move, two deliberate actions.
        if (from is { } required && current != required) throw new SettingsValidationException([new("state", "transition")]);
        await states.SetAsync(to, clean, cancellationToken);
        var name = (await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DetailAsync(cancellationToken)).Name;
        var mail = OrganisationStates.Message(string.Empty, name, to, clean);
        return new EmailPreview(mail.Subject, mail.Body);
    }
}
