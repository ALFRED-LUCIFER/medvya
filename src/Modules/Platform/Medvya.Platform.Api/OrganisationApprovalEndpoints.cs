using Medvya.Platform.Application;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Platform.Api;

/// <summary>
/// The console's organisation approvals (MDV-P1-064, ADR-016, ADR-014 note): the queue, one request with its documents and history, the private
/// document viewer and the decisions. Everything goes through <see cref="IOrganisationConsole"/>, the reviewed read path and the one service that
/// sets an organisation's state, which audits it in the organisation; each action is also appended to the platform audit log. Reading is open to every
/// console role, deciding to owner, operations and reviewer (support and finance read only). The service reads a request and its documents, never clinic patient data.
/// </summary>
public static class OrganisationApprovalEndpoints
{
    const string Action = "organisation";

    public static WebApplication MapOrganisationApprovals(this WebApplication app)
    {
        var read = app.MapGroup("/v1/organisations").RequireAuthorization(PlatformPolicies.OrganisationRead).AddEndpointFilter(PlatformEndpoints.MapErrors);

        read.MapGet("/", async (ReviewFilter? filter, [FromServices] IOrganisationConsole console, [FromServices] IPlatformCaller caller, CancellationToken ct) =>
            Results.Ok(await console.ListAsync(filter ?? ReviewFilter.All, Operator(caller), ct)));

        read.MapGet("/{tenantId:guid}", async (Guid tenantId, [FromServices] IOrganisationConsole console, [FromServices] IConsoleActions actions,
            [FromServices] IPlatformCaller caller, CancellationToken ct) =>
        {
            if (await console.DetailAsync(tenantId, Operator(caller), ct) is not { } d) return Results.NotFound();
            var names = await actions.MemberNamesAsync(d.History.Select(h => h.ActorUserId).OfType<Guid>(), ct);
            return Results.Ok(new
            {
                d.Name, d.Country, d.Type, d.State, d.SubmittedAt, d.Reason, d.Address, d.ContactEmail, d.RegistrationNumber, d.TaxNumber, d.ItContactName, d.ItContactEmail, d.Documents,
                History = d.History.Select(h => new { h.At, Actor = h.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : null, h.State, h.Reason }),
            });
        });

        // The viewer's bytes. Logged in the organisation (by the service, before the read) and here; never cached; shown inline, not offered as a download.
        // shortcut: the watermark is drawn over the file on screen, not burned into it, so a determined operator can still copy what they can see; burn it
        // in (re-render the file with the operator and time) when documents may be seen by people outside the reviewer role.
        read.MapGet("/{tenantId:guid}/documents/{documentId:guid}", async (Guid tenantId, Guid documentId, HttpContext http, [FromServices] IOrganisationConsole console,
            [FromServices] IConsoleActions actions, [FromServices] IPlatformCaller caller, CancellationToken ct) =>
        {
            if (await console.OpenDocumentAsync(tenantId, documentId, Operator(caller), ct) is not { } file) return Results.NotFound();
            await actions.RecordAsync("organisation.document.view", Action, documentId, tenantId, null, ct);
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.ContentDisposition = "inline";
            return Results.File(file.Content, file.ContentType);
        });

        var act = app.Actions("/v1/organisations").RequireAuthorization(PlatformPolicies.OrganisationDecide);
        foreach (var (route, action) in new[] { ("approve", ReviewAction.Approve), ("request-changes", ReviewAction.RequestChanges), ("reject", ReviewAction.Reject),
                     ("suspend", ReviewAction.Suspend), ("reinstate", ReviewAction.Reinstate) })
            act.MapPost($"/{{tenantId:guid}}/{route}", async (Guid tenantId, [FromBody] PlatformEndpoints.ReasonBody body, [FromServices] IOrganisationConsole console,
                [FromServices] IConsoleActions actions, [FromServices] IPlatformCaller caller, CancellationToken ct) =>
            {
                var reason = Medvya.Platform.Domain.PlatformReason.Valid(body.Reason)!; // the group's filter refused anything else
                if (await console.DecideAsync(tenantId, action, reason, Operator(caller), ct) is not { } email) return Results.NotFound();
                // shortcut: the state change and this entry are in two databases (the review login and the console's), so they cannot share a transaction;
                // the organisation's own audit trail records the decision first. Add an outbox or a reconcile job if an entry here is ever missing.
                await actions.RecordAsync($"organisation.{route}", Action, tenantId, tenantId, reason, ct);
                return Results.Ok(email);
            });
        return app;
    }

    static Guid Operator(IPlatformCaller caller) => caller.MemberId ?? throw new InvalidOperationException("No signed-in console member.");
}
