using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Billing.Api;

/// <summary>
/// Bills and payments (P-06, MDV-P4-004; split payments from MDV-P4-009) for the front desk: "Invoices and payments" at Full, only while billing is on (404
/// otherwise). Ids in paths, everything else in bodies (no PHI in URLs). Reads are audited per request, changes by the use case in their own transaction.
/// </summary>
public static class InvoiceEndpoints
{
    const string Purpose = AuditPurpose.Billing, Resource = "invoice", BillingDocumentsAction = "billing.document.print";

    public static IEndpointRouteBuilder MapInvoices(this IEndpointRouteBuilder app)
    {
        // SAFETY: mutation check: removed RequireCapability(InvoicesPayments) from this group -> BillingInvoiceTests.TheFrontDeskBillsAndRolesWithoutInvoicesAreRefused failed
        var g = app.MapGroup("/v1/billing").RequireCapability(Capability.InvoicesPayments, Access.Full).RequireFeature(Features.Billing);
        g.MapPost("/invoices/search", async ([FromBody] InvoiceSearch? body, [FromServices] IInvoices s, CancellationToken ct) => Results.Ok(await s.SearchAsync(body ?? new(), ct)))
            .Audited("billing.invoice.list", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapGet("/invoices/{id:guid}", async (Guid id, HttpContext http, [FromServices] IInvoices s, CancellationToken ct) =>
            {
                http.Items["audit.resource"] = id;
                return await s.GetAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound();
            })
            .Audited("billing.invoice.read", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices", async ([FromBody] NewInvoice? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.CreateAsync(body, ct), created: true))
            .AuditedByUseCase("billing.invoice.create", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        // The board's Billing card: the visit's open bill, or a new draft with the suggested consultation line.
        g.MapPost("/visits/{appointmentId:guid}/invoice", async (Guid appointmentId, [FromServices] IInvoices s, CancellationToken ct) => Found(await s.ForVisitAsync(appointmentId, ct)))
            .AuditedByUseCase("billing.invoice.create", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/lines", async (Guid id, [FromBody] AddLine? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.AddLineAsync(id, body, ct)))
            .AuditedByUseCase("billing.invoice.line_add", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPut("/invoices/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, [FromBody] UpdateLine? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.UpdateLineAsync(id, lineId, body, ct)))
            .AuditedByUseCase("billing.invoice.line_update", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/lines/{lineId:guid}/remove", async (Guid id, Guid lineId, [FromBody] InvoiceVersion? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.RemoveLineAsync(id, lineId, body, ct)))
            .AuditedByUseCase("billing.invoice.line_remove", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPut("/invoices/{id:guid}/discount", async (Guid id, [FromBody] SetDiscount? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.SetDiscountAsync(id, body, ct)))
            .AuditedByUseCase("billing.invoice.discount", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/issue", async (Guid id, [FromBody] InvoiceVersion? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.IssueAsync(id, body, ct)))
            .AuditedByUseCase("billing.invoice.issue", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/cancel", async (Guid id, [FromBody] CancelInvoice? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.CancelAsync(id, body, ct)))
            .AuditedByUseCase("billing.invoice.cancel", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/payments", async (Guid id, [FromBody] RecordPayment? body, [FromServices] IInvoices s, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Found(await s.RecordPaymentAsync(id, body, ct)))
            .AuditedByUseCase("billing.payment.record", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/payment-link", async (Guid id, [FromServices] IInvoices s, CancellationToken ct) =>
                await s.CreatePaymentLinkAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("billing.payment.link_create", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/payments/{paymentId:guid}/check", async (Guid id, Guid paymentId, [FromServices] IInvoices s, CancellationToken ct) =>
                Found(await s.CheckPaymentAsync(id, paymentId, ct)))
            .AuditedByUseCase("billing.payment.check", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        g.MapPost("/invoices/{id:guid}/payments/{paymentId:guid}/cancel", async (Guid id, Guid paymentId, [FromServices] IInvoices s, CancellationToken ct) =>
                Found(await s.CancelPaymentLinkAsync(id, paymentId, ct)))
            .AuditedByUseCase("billing.payment.link_cancel", Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        // MDV-P4-005: the bill or its receipt as a PDF, A5 or 80 mm thermal. POST: the first print issues and stores the paper; later ones re-print it.
        g.MapPost("/invoices/{id:guid}/documents/{kind}/{layout}", async (Guid id, string kind, string layout, HttpContext http, [FromServices] IBillingDocuments s, CancellationToken ct) =>
            {
                if (!Enum.TryParse<BillingDocumentKind>(kind, ignoreCase: true, out var k) || !Enum.IsDefined(k) || !Enum.TryParse<DocumentLayout>(layout, ignoreCase: true, out var l) || !Enum.IsDefined(l))
                    return Results.NotFound();
                if (await s.PdfAsync(id, k, l, ct) is not { } pdf) return Results.NotFound();
                http.Response.Headers["X-Content-SHA256"] = pdf.Sha256;
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(pdf.Content, "application/pdf");
            })
            .AuditedByUseCase(BillingDocumentsAction, Resource, Purpose).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        return app;
    }

    static IResult Found(InvoiceView? view, bool created = false) =>
        view is null ? Results.NotFound() : created ? Results.Created($"/v1/billing/invoices/{view.Id}", view) : Results.Ok(view);
}
