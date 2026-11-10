using Medvya.BuildingBlocks.Domain;
using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Platform.Api;

/// <summary>
/// The console's organisation approval queue (MDV-P1-064, ADR-016; ADR-014 amended): the same Tenancy services as the interim API of Host.Api,
/// called over the console's review login (medvya_review), which holds only the onboarding tables and an append to the clinic audit log.
/// Reads go through the medvya.operator row-level security branch; a decision runs inside the organisation's tenant. Business details only,
/// never patient data. Each decision is in the clinic audit (with its settings history row) and in the platform audit log.
/// </summary>
public static class OrganisationReviewEndpoints
{
    public sealed record Decision(string? Reason) : IReasoned;
    public sealed record DocumentLink(Uri Url, DateTimeOffset ExpiresAt);

    public static WebApplication MapOrganisationReview(this WebApplication app)
    {
        var read = app.MapGroup("/v1/organisations").RequireAuthorization(PlatformPolicies.OrganisationRead).AddEndpointFilter(MapErrors);
        read.MapGet("/", async (ReviewFilter? filter, [FromServices] IOrganisationReviews queue, CancellationToken ct) => Results.Ok(await queue.ListAsync(filter ?? ReviewFilter.Waiting, ct)));
        read.MapGet("/{tenantId:guid}", async (Guid tenantId, HttpContext http, [FromServices] IPlatformTeam team, CancellationToken ct) =>
        {
            await using var scope = InTenant(http, tenantId);
            RequestDetail detail;
            try { detail = await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DetailAsync(ct); }
            catch (InvalidOperationException) { return Results.NotFound(); } // no readable tenant row
            catch (SettingsValidationException) { return Results.NotFound(); } // an organisation without a request (migrated)
            // Console decisions name a platform member; a decision made through the interim API names a clinic user, shown as Medvya operations.
            var names = await team.NamesAsync([.. detail.History.Select(h => h.ActorUserId).OfType<Guid>().Distinct()], ct);
            return Results.Ok(new { detail.Name, detail.Country, detail.Type, detail.State, detail.SubmittedAt, detail.Reason, detail.Address, detail.ContactEmail,
                detail.RegistrationNumber, detail.TaxNumber, detail.ItContactName, detail.ItContactEmail, detail.Documents,
                History = detail.History.Select(h => new { h.At, Actor = h.ActorUserId is { } id && names.TryGetValue(id, out var n) ? n : null, h.State, h.Reason }) });
        });
        read.MapGet("/{tenantId:guid}/documents/{documentId:guid}", async (Guid tenantId, Guid documentId, HttpContext http, CancellationToken ct) =>
        {
            await using var scope = InTenant(http, tenantId);
            if (await scope.ServiceProvider.GetRequiredService<IOrganisationRequests>().DocumentLinkAsync(documentId, ct) is not { } url) return Results.NotFound();
            return Results.Ok(new DocumentLink(url, DateTimeOffset.UtcNow.AddMinutes(5)));
        });

        var act = app.Actions("/v1/organisations").RequireAuthorization(PlatformPolicies.OrganisationApprove).AddEndpointFilter(MapErrors);
        act.MapPost("/{tenantId:guid}/approve", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, "approve", OrganisationState.Approved, body, ct, OrganisationState.Submitted));
        act.MapPost("/{tenantId:guid}/reinstate", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, "reinstate", OrganisationState.Approved, body, ct, OrganisationState.Suspended));
        act.MapPost("/{tenantId:guid}/request-changes", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, "request-changes", OrganisationState.NeedsChanges, body, ct));
        act.MapPost("/{tenantId:guid}/reject", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, "reject", OrganisationState.Rejected, body, ct));
        act.MapPost("/{tenantId:guid}/suspend", (Guid tenantId, [FromBody] Decision body, HttpContext http, CancellationToken ct) => Apply(http, tenantId, "suspend", OrganisationState.Suspended, body, ct));
        return app;
    }

    /// <summary>A DI scope whose Tenancy and Audit services (and the row-level security setting) run in <paramref name="tenantId"/>.</summary>
    static AsyncServiceScope InTenant(HttpContext http, Guid tenantId)
    {
        var scope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ILinkTenant>().UseLinkTenant(tenantId);
        return scope;
    }

    // The Actions group has already refused a missing, short or long reason (PlatformReason, 5 to 500 characters).
    static async Task<IResult> Apply(HttpContext http, Guid tenantId, string action, OrganisationState to, Decision body, CancellationToken ct, OrganisationState? from = null)
    {
        var reason = PlatformReason.Valid(body.Reason)!;
        await using (var scope = InTenant(http, tenantId))
        {
            var states = scope.ServiceProvider.GetRequiredService<IOrganisationState>();
            OrganisationState current;
            try { current = await states.CurrentAsync(ct); } catch (InvalidOperationException) { return Results.NotFound(); } // unknown id: no readable row
            // Approve is for a request in review and Reinstate for a suspended organisation: the same move, two deliberate actions.
            if (from is { } required && current != required) throw new SettingsValidationException([new("state", "transition")]);
            await states.SetAsync(to, reason, ct);
        }
        // After the decision is saved with its clinic audit row (the authoritative record), so the console log never shows a decision that did not happen.
        await http.RequestServices.GetRequiredService<IPlatformAuditLog>().RecordAsync($"organisation.{action}", "organisation", tenantId, reason, ct);
        return Results.NoContent();
    }

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try { return await next(ctx); }
        catch (SettingsValidationException e) { return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(x => x.Key, x => x.Select(y => y.Code).ToArray())); }
    }
}
