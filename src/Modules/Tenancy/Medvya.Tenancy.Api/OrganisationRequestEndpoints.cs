using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Medvya operations' approval capability (ADR-016, MDV-P1-064), kept apart from practice roles. Approvers (the console's operations and reviewer
/// roles) are the staff user ids in <c>Operations:OrganisationApprovers</c>; readers (support, finance) in <c>Operations:OrganisationReaders</c> also
/// see the queue; an approver can read too. Configuration (Key Vault / user secrets), never a claim a practice can obtain.
/// shortcut: a configured id list stands in for the console roles until the console runs on Azure and calls the same service (MDV-P5-044); move to the platform member roles then.
/// </summary>
public static class OperationsPolicies
{
    public const string OrganisationRead = "operations.organisation.read", OrganisationApprove = "operations.organisation.approve";

    public static AuthorizationBuilder AddOperationsPolicies(this AuthorizationBuilder builder, IConfiguration configuration) => builder
        .AddPolicy(OrganisationApprove, p => p.RequireAuthenticatedUser().RequireAssertion(ctx => Listed(ctx.User, configuration, "OrganisationApprovers")))
        .AddPolicy(OrganisationRead, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            Listed(ctx.User, configuration, "OrganisationApprovers") || Listed(ctx.User, configuration, "OrganisationReaders")));

    static bool Listed(System.Security.Claims.ClaimsPrincipal user, IConfiguration configuration, string list) =>
        Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && (configuration.GetSection($"Operations:{list}").Get<Guid[]>() ?? []).Contains(id);
}

/// <summary>
/// The applicant's side of the onboarding request (F-04, MDV-P1-062): status, hospital IT contact, documents, submit. Set-up capability, so it works
/// before approval, for the owner only (a practice admin or owner-doctor). The review side is <see cref="OrganisationApprovalEndpoints"/>.
/// </summary>
public static class OrganisationRequestEndpoints
{
    public static WebApplication MapOrganisationRequest(this WebApplication app)
    {
        var g = app.MapGroup("/v1/organisation/request").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(SettingsEndpoints.MapErrors);

        g.MapGet("/", async ([FromServices] IOrganisationRequests r, CancellationToken ct) => await r.GetAsync(ct) is { } v ? Results.Ok(v) : Results.NotFound());
        g.MapPut("/it-contact", async ([FromBody] SaveItContact body, [FromServices] IOrganisationRequests r, CancellationToken ct) =>
        {
            await r.SetItContactAsync(body, ct);
            return Results.NoContent();
        });
        // The body is the file itself (PDF, PNG or JPEG, up to 5 MB), so no multipart parser sits in front of the type check.
        g.MapPut("/documents/{kind}", async (string kind, HttpRequest request, [FromServices] IOrganisationRequests r, CancellationToken ct) =>
        {
            if (request.ContentLength > OrganisationDocumentRules.MaxBytes) return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            return Results.Ok(await r.PutDocumentAsync(kind, buffer.ToArray(), ct));
        });
        g.MapPost("/submit", async ([FromServices] IOrganisationRequests r, CancellationToken ct) =>
        {
            await r.SubmitAsync(ct);
            return Results.NoContent();
        });
        return app;
    }
}

/// <summary>
/// The interim approval API of Medvya operations (ADR-016, MDV-P1-064; runbook docs/runbooks/organisation-approval.md): list requests, open a
/// document, approve, ask for changes, reject, suspend. Every action needs a reason of 5 to 500 characters and runs inside the organisation's tenant,
/// so row-level security still scopes every row, and the review reads only the request and its documents, never clinic patient data.
/// </summary>
public static class OrganisationApprovalEndpoints
{
    public sealed record Decision(string? Reason);
    public sealed record DocumentLink(Uri Url, DateTimeOffset ExpiresAt);

    public static WebApplication MapOrganisationApproval(this WebApplication app)
    {
        var read = app.MapGroup("/v1/operations/organisations").RequireAuthorization(OperationsPolicies.OrganisationRead).AddEndpointFilter(SettingsEndpoints.MapErrors);
        var act = app.MapGroup("/v1/operations/organisations").RequireAuthorization(OperationsPolicies.OrganisationApprove).AddEndpointFilter(SettingsEndpoints.MapErrors);

        read.MapGet("/", async (ReviewFilter? filter, [FromServices] IOrganisationReviews queue, CancellationToken ct) => Results.Ok(await queue.ListAsync(filter ?? ReviewFilter.All, ct)));
        read.MapGet("/{tenantId:guid}", async (Guid tenantId, HttpContext http, CancellationToken ct) =>
        {
            await using var scope = http.RequestServices.EnterLinkTenant(tenantId);
            RequestDetail detail;
            try { detail = await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DetailAsync(ct); }
            catch (InvalidOperationException) { return Results.NotFound(); } // no readable tenant row
            catch (SettingsValidationException) { return Results.NotFound(); } // an organisation without a request (migrated)
            var names = await scope.ServiceProvider.GetRequiredService<IStaffUsers>().DisplayNamesAsync([.. detail.History.Select(h => h.ActorUserId).OfType<Guid>().Distinct()], ct);
            return Results.Ok(new { detail.Name, detail.Country, detail.Type, detail.State, detail.SubmittedAt, detail.Reason, detail.Address, detail.ContactEmail, detail.RegistrationNumber,
                detail.TaxNumber, detail.ItContactName, detail.ItContactEmail, detail.Documents,
                History = detail.History.Select(h => new { h.At, Actor = h.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : null, h.State, h.Reason }) });
        });
        read.MapGet("/{tenantId:guid}/documents/{documentId:guid}", async (Guid tenantId, Guid documentId, HttpContext http, CancellationToken ct) =>
        {
            await using var scope = http.RequestServices.EnterLinkTenant(tenantId);
            if (await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DocumentLinkAsync(documentId, ct) is not { } url) return Results.NotFound();
            return Results.Ok(new DocumentLink(url, DateTimeOffset.UtcNow.AddMinutes(5)));
        });

        act.MapPost("/{tenantId:guid}/approve", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, OrganisationState.Approved, body, ct, OrganisationState.Submitted));
        act.MapPost("/{tenantId:guid}/reinstate", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, OrganisationState.Approved, body, ct, OrganisationState.Suspended));
        act.MapPost("/{tenantId:guid}/request-changes", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, OrganisationState.NeedsChanges, body, ct));
        act.MapPost("/{tenantId:guid}/reject", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, OrganisationState.Rejected, body, ct));
        act.MapPost("/{tenantId:guid}/suspend", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, OrganisationState.Suspended, body, ct));
        return app;
    }

    static async Task<IResult> Apply(HttpContext http, Guid tenantId, OrganisationState to, Decision body, CancellationToken ct, OrganisationState? from = null)
    {
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new SettingsValidationException([new("reason", "required")]);
        if (reason.Length < 5) throw new SettingsValidationException([new("reason", "too_short")]);
        if (reason.Length > 500) throw new SettingsValidationException([new("reason", "too_long")]);
        await using var scope = http.RequestServices.EnterLinkTenant(tenantId);
        var states = scope.ServiceProvider.GetRequiredService<IOrganisationState>();
        // An unknown organisation id reads no tenant row under row-level security.
        OrganisationState current;
        try { current = await states.CurrentAsync(ct); } catch (InvalidOperationException) { return Results.NotFound(); }
        // Approve is for a request in review and Reinstate for a suspended organisation: the same move, two deliberate actions.
        if (from is { } required && current != required) throw new SettingsValidationException([new("state", "transition")]);
        await states.SetAsync(to, reason, ct);
        return Results.NoContent();
    }
}
