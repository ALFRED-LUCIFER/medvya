using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Prescribing.Api;

public static class PrescriptionEndpoints
{
    /// <summary>The per-IP sign-in limit of Host.Api (StaffAuthentication.SignInRateLimit): guessing tokens is as cheap as guessing sign-ins.</summary>
    public const string VerifyRateLimit = "sign-in";

    public sealed record VerifyRx(string? Token);
    public sealed record SharedRx(string? Token, string? Code);

    /// <summary>
    /// The draft prescription composer (MDV-P2-015). Signed in with "Prescriptions and orders" at Sign; the use case then checks clinical access
    /// to the encounter's patient and writes the audit event with the patient. Ids only in paths; medicine names and dose fields travel in bodies.
    /// Authorising (MDV-P2-017) is its own POST, separate from signing the note; print, PDF and the layout approval follow it. The QR verify check is
    /// public on purpose: the token is the credential and travels in the body.
    /// </summary>
    public static IEndpointRouteBuilder MapPrescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireCapability(Capability.PrescriptionsOrders, Access.Sign);

        g.MapGet("/encounters/{encounterId:guid}/prescription", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.ListAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.read", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/items", async (Guid encounterId, [FromBody] AddRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.AddAsync(encounterId, body ?? new AddRxLine(null, null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.add", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPut("/prescription-items/{itemId:guid}", async (Guid itemId, [FromBody] UpdateRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.UpdateAsync(itemId, body ?? new UpdateRxLine(null, null, null, null, null, null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.update", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPut("/prescription-items/{itemId:guid}/included", async (Guid itemId, [FromBody] IncludeRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.IncludeAsync(itemId, body ?? new IncludeRxLine(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.include", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // Safety checks (MDV-P2-016): run after every change; an override carries its reason in the body.
        g.MapGet("/encounters/{encounterId:guid}/prescription/checks", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.CheckAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.check", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/overrides", async (Guid encounterId, [FromBody] OverrideRxAlert? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.OverrideAsync(encounterId, body ?? new OverrideRxAlert(null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.override", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // MDV-P2-017: the authorisation (re-authentication, the checks run again), then the print view and the stored PDF of what was authorised.
        g.MapGet("/encounters/{encounterId:guid}/prescription/authorisation", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.AuthorisationAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.authorisation", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/authorisation", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.AuthoriseAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.authorise", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapGet("/encounters/{encounterId:guid}/prescription/print", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.PrintAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.print", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/pdf", async (Guid encounterId, string? paper, [FromServices] IPrescriptions service, HttpContext http, CancellationToken ct) =>
            {
                if (await service.PdfAsync(encounterId, paper, ct) is not { } pdf) return Results.NotFound();
                http.Response.Headers["X-Content-SHA256"] = pdf.Sha256;
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(pdf.Content, "application/pdf");
            })
            .AuditedByUseCase("rx.pdf", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // The signed-in doctor's own approval of the print layout (exit gate 4): no patient data, audited inside the service.
        g.MapGet("/prescription-layout", ([FromServices] IPrescriptions service, CancellationToken ct) => service.LayoutAsync(ct));
        g.MapPost("/prescription-layout/approval", ([FromServices] IPrescriptions service, CancellationToken ct) => service.ApproveLayoutAsync(ct));
        // MDV-P2-020: share the authorised prescription by secure link (the address is read from the record or travels in the body), and the pharmacy dispatch.
        // shortcut: doctors only (the role matrix gives the desk no Prescriptions access); the receptionist's share waits for a matrix decision.
        g.MapGet("/encounters/{encounterId:guid}/prescription/shares", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.SharesAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.share.read", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/shares", async (Guid encounterId, [FromBody] ShareRx? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.ShareAsync(encounterId, body ?? new ShareRx(null, null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.share", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/prescription-shares/{shareId:guid}/dispatch", async (Guid shareId, [FromBody] UpdateDispatch? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.DispatchAsync(shareId, body ?? new UpdateDispatch(null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.dispatch", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        // Public on purpose: whoever holds the paper (patient, pharmacist) scans it without signing in. Malformed and unknown tokens are both 404;
        // the answer is validity only (no patient, no medicines). The token names the tenant, so the look-up runs inside it (row-level security).
        app.MapPost("/v1/prescriptions/verify", async ([FromBody] VerifyRx? body, HttpContext http, CancellationToken ct) =>
        {
            if (RxVerifyToken.Read(body?.Token) is not { } token) return Results.NotFound();
            await using var scope = http.RequestServices.EnterLinkTenant(token.TenantId);
            return await scope.ServiceProvider.GetRequiredService<IPrescriptionVerification>().VerifyAsync(token.Code, ct) is { } v ? Results.Ok(v) : Results.NotFound();
        }).AllowAnonymous().RequireRateLimiting(VerifyRateLimit);

        // Public on purpose (MDV-P2-020): the patient or pharmacy opening a share link is not signed in. The link token (in the body) names the tenant;
        // malformed, unknown and expired links are all 404. The PDF comes only with the one-time code sent to the patient's registered mobile.
        app.MapPost("/v1/prescriptions/shared/code", async ([FromBody] SharedRx? body, HttpContext http, CancellationToken ct) =>
        {
            if (InvitationToken.Read(body?.Token) is not { } link) return Results.NotFound();
            await using var scope = http.RequestServices.EnterLinkTenant(link.TenantId);
            return await Shared(() => scope.ServiceProvider.GetRequiredService<IRxShareLinks>().SendCodeAsync(link.Hash, ct), sent => sent ? Results.NoContent() : Results.NotFound());
        }).AllowAnonymous().RequireRateLimiting(VerifyRateLimit);
        app.MapPost("/v1/prescriptions/shared/open", async ([FromBody] SharedRx? body, HttpContext http, CancellationToken ct) =>
        {
            if (InvitationToken.Read(body?.Token) is not { } link) return Results.NotFound();
            await using var scope = http.RequestServices.EnterLinkTenant(link.TenantId);
            return await Shared(() => scope.ServiceProvider.GetRequiredService<IRxShareLinks>().OpenAsync(link.Hash, body!.Code, ct), pdf =>
            {
                if (pdf is null) return Results.NotFound();
                http.Response.Headers["X-Content-SHA256"] = pdf.Sha256;
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(pdf.Content, "application/pdf");
            });
        }).AllowAnonymous().RequireRateLimiting(VerifyRateLimit);
        return app;
    }

    /// <summary>A refusal of a live link as its code: replaced (410), wrong or no code (403), too many codes (429), the code could not be sent (503).</summary>
    static async Task<IResult> Shared<T>(Func<Task<T>> call, Func<T, IResult> ok)
    {
        try
        {
            return ok(await call());
        }
        catch (PrescriptionConflictException e)
        {
            var status = e.Code switch
            {
                "replaced" => StatusCodes.Status410Gone,
                "wrong_code" or "no_code" => StatusCodes.Status403Forbidden,
                "too_soon" or "code_limit" => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
            return Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }

    // Inside the audit filter (added after it), so the row records the answer the caller gets.
    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ClinicalAccessDeniedException e)
        {
            return ClinicalAccessProblem.For(e);
        }
        catch (PrescriptionValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (ReauthRequiredException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Confirm your identity to authorise", extensions: new Dictionary<string, object?> { ["code"] = "reauth_required" });
        }
        catch (PdfUnsupportedTextException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The prescription has text the PDF cannot show; use the print view",
                extensions: new Dictionary<string, object?> { ["code"] = "pdf_unsupported_text" });
        }
        catch (PrescriptionConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The prescription cannot change now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
