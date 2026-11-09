using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Prescribing.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Prescribing.Api;

public static class OrderEndpoints
{
    /// <summary>
    /// Investigation orders (MDV-P2-018). Signed in with "Prescriptions and orders" at Sign; the use case then checks clinical access to the
    /// encounter's patient and writes the audit event with the patient. Ids only in paths; test names and notes travel in bodies, never in URLs.
    /// Placing orders is its own call and nothing else reaches it.
    /// </summary>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireCapability(Capability.PrescriptionsOrders, Access.Sign);

        g.MapGet("/encounters/{encounterId:guid}/orders", async (Guid encounterId, [FromServices] IOrders service, CancellationToken ct) =>
                await service.ListAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.read", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/orders", async (Guid encounterId, [FromBody] AddOrder? body, [FromServices] IOrders service, CancellationToken ct) =>
                await service.AddAsync(encounterId, body ?? new AddOrder(null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.add", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/orders/from-set", async (Guid encounterId, [FromBody] AddOrderSetOrders? body, [FromServices] IOrders service, CancellationToken ct) =>
                await service.AddSetAsync(encounterId, body ?? new AddOrderSetOrders(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.set.add", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPut("/orders/{orderId:guid}/included", async (Guid orderId, [FromBody] IncludeOrder? body, [FromServices] IOrders service, CancellationToken ct) =>
                await service.IncludeAsync(orderId, body ?? new IncludeOrder(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.include", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/orders/place", async (Guid encounterId, [FromBody] PlaceOrders? body, [FromServices] IOrders service, CancellationToken ct) =>
                await service.PlaceAsync(encounterId, body ?? new PlaceOrders(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.place", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapGet("/encounters/{encounterId:guid}/orders/slip", async (Guid encounterId, [FromServices] IOrders service, CancellationToken ct) =>
                await service.SlipAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("order.slip", "order", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        return app;
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
        catch (OrdersValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (OrdersConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Orders cannot do this now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
