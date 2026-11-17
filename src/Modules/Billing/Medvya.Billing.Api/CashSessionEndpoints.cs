using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Billing.Api;

/// <summary>
/// The day close (P-07, P-13, MDV-P4-012) for the front desk: "Invoices and payments" at Full, only while billing is on (404 otherwise). The second-person
/// rule is the use case's, not the screen's. Ids in paths, amounts and notes in bodies.
/// </summary>
public static class CashSessionEndpoints
{
    const string Purpose = AuditPurpose.Billing, Resource = "cash_session";

    public static IEndpointRouteBuilder MapCashSessions(this IEndpointRouteBuilder app)
    {
        // SAFETY: mutation check: removed RequireCapability(InvoicesPayments) from this group -> BillingInvoiceTests.OnlyTheFrontDeskClosesTheDayAndBillingOffIs404 failed (a nurse read the drawer: 200, expected 403)
        var g = app.MapGroup("/v1/billing/cash-sessions").RequireCapability(Capability.InvoicesPayments, Access.Full).RequireFeature(Features.Billing);
        g.MapGet("", async ([FromServices] ICashSessions s, CancellationToken ct) => Results.Ok(await s.DayAsync(ct)))
            .Audited("billing.cash.read", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("", async ([FromBody] OpenCashSession? body, [FromServices] ICashSessions s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Results.Ok(await s.OpenAsync(body, ct)))
            .AuditedByUseCase("billing.cash.open", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/{id:guid}/count", async (Guid id, [FromBody] CountCashSession? body, [FromServices] ICashSessions s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.CountAsync(id, body, ct)))
            .AuditedByUseCase("billing.cash.count", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/{id:guid}/confirm", async (Guid id, [FromBody] ConfirmCashSession? body, [FromServices] ICashSessions s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.ConfirmAsync(id, body, ct)))
            .AuditedByUseCase("billing.cash.confirm", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/{id:guid}/adjustments", async (Guid id, [FromBody] AddCashAdjustment? body, [FromServices] ICashSessions s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.AdjustAsync(id, body, ct)))
            .AuditedByUseCase("billing.cash.adjust", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        // POST, as the other papers: rendered on request, never cached.
        g.MapPost("/{id:guid}/report/pdf", async (Guid id, HttpContext http, [FromServices] ICashSessions s, CancellationToken ct) =>
            {
                http.Items["audit.resource"] = id;
                if (await s.ReportAsync(id, ct) is not { } pdf) return Results.NotFound();
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(pdf.Content, "application/pdf");
            })
            .Audited("billing.cash.report", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        return app;
    }

    static IResult Found(CashSessionView? view) => view is null ? Results.NotFound() : Results.Ok(view);
}
