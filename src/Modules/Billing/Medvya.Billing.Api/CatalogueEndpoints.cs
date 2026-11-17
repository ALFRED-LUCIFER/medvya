using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Api;

/// <summary>
/// Settings, Services and Settings, Billing (MDV-P4-002). The catalogue, prices and settings are changed with the practice-configuration capability
/// (<see cref="StaffPolicies.ManageStaff"/>); the front desk reads the catalogue with "Invoices and payments" at Full. Every route exists only while the
/// billing flag is on (404 otherwise) and is audited: reads once per request, changes by the use case in its own transaction. No patient data.
/// </summary>
public static class CatalogueEndpoints
{
    const string Purpose = AuditPurpose.Operations;

    public static IEndpointRouteBuilder MapBillingCatalogue(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/settings").RequireAuthorization(StaffPolicies.ManageStaff).RequireFeature(Features.Billing);
        admin.MapGet("/services", List).Audited("billing.service.list", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPost("/services", async ([FromBody] SaveService? body, [FromServices] ICatalogue c, CancellationToken ct) =>
                body is null ? Required() : Results.Created("/v1/settings/services", await c.CreateAsync(body, ct)))
            .AuditedByUseCase("billing.service.create", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPut("/services/{id:guid}", async (Guid id, [FromBody] SaveService? body, [FromServices] ICatalogue c, CancellationToken ct) =>
                body is null ? Required() : await c.UpdateAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("billing.service.update", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPost("/services/{id:guid}/archive", async (Guid id, [FromServices] ICatalogue c, CancellationToken ct) =>
                await c.ArchiveAsync(id, true, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("billing.service.archive", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPost("/services/{id:guid}/unarchive", async (Guid id, [FromServices] ICatalogue c, CancellationToken ct) =>
                await c.ArchiveAsync(id, false, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("billing.service.unarchive", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapGet("/services/{id:guid}/prices", async (Guid id, [FromServices] ICatalogue c, CancellationToken ct) =>
                await c.PricesAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .Audited("billing.price.list", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPost("/services/{id:guid}/prices", async (Guid id, [FromBody] AddPrice? body, [FromServices] ICatalogue c, CancellationToken ct) =>
                body is null ? Required() : await c.AddPriceAsync(id, body, ct) is { } v ? Results.Created($"/v1/settings/services/{id}/prices", v) : Results.NotFound())
            .AuditedByUseCase("billing.price.add", "service", Purpose).AddEndpointFilter(MapErrors);
        admin.MapGet("/billing", async ([FromServices] IBillingSettings s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)))
            .Audited("billing.settings.read", "billing_settings", Purpose).AddEndpointFilter(MapErrors);
        admin.MapPut("/billing", async ([FromBody] SaveBillingSettings? body, [FromServices] IBillingSettings s, CancellationToken ct) =>
                body is null ? Required() : Results.Ok(await s.SaveAsync(body, ct)))
            .AuditedByUseCase("billing.settings.update", "billing_settings", Purpose).AddEndpointFilter(MapErrors);

        // The front desk prices a bill from the active catalogue (MDV-P4-004).
        app.MapGroup("/v1/billing").RequireCapability(Capability.InvoicesPayments, Access.Full).RequireFeature(Features.Billing)
            .MapGet("/services", ([FromServices] ICatalogue c, CancellationToken ct) => List(false, c, ct)).Audited("billing.service.list", "service", Purpose).AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> List(bool? archived, [FromServices] ICatalogue c, CancellationToken ct) => Results.Ok(await c.ListAsync(archived ?? false, ct));

    internal static IResult Required() => Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] });

    // Inside the audit filter (added after it), so the row records the answer the caller gets.
    internal static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (BillingValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (DocumentNumberingException e)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["number"] = [e.Code] }); // e.g. series_exhausted: nothing was issued
        }
        catch (PaymentProviderException e)
        {
            // The kind of failure only, never the provider's text: "provider_not_connected" tells the desk to ask the admin to connect Razorpay.
            var code = "provider_" + JsonNamingPolicy.SnakeCaseLower.ConvertName(e.Error.ToString());
            return Results.Problem(statusCode: e.Error == PaymentProviderError.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
                title: "The payment provider did not accept the request.", extensions: new Dictionary<string, object?> { ["code"] = code });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This was changed by someone else. Reload and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "changed" });
        }
    }
}
