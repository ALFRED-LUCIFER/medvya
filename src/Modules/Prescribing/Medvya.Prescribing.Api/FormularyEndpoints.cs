using Medvya.Prescribing.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Prescribing.Api;

public static class FormularyEndpoints
{
    /// <summary>
    /// Formulary and order sets (MDV-P2-014). Doctors read them with "Prescriptions and orders" at Sign (the prescription composer searches here);
    /// the practice adds medicines and builds order sets under settings with the practice-configuration capability. Neither touches patient data,
    /// so neither is patient-audited; every change is audited inside the service. The query holds a medicine name, never a patient value.
    /// </summary>
    public static IEndpointRouteBuilder MapFormularyEndpoints(this IEndpointRouteBuilder app)
    {
        var doctor = app.MapGroup("/v1").RequireCapability(Capability.PrescriptionsOrders, Access.Sign).AddEndpointFilter(MapErrors);
        doctor.MapGet("/formulary", Search);
        doctor.MapGet("/order-sets", async ([FromServices] IFormulary f, CancellationToken ct) => Results.Ok(await f.OrderSetsAsync(ct)));

        var admin = app.MapGroup("/v1/settings").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(MapErrors);
        admin.MapGet("/formulary", Search);
        admin.MapPost("/formulary", async ([FromBody] AddMedicine? body, [FromServices] IFormulary f, CancellationToken ct) =>
            body is null ? Required() : Results.Created("/v1/settings/formulary", await f.AddAsync(body, ct)));
        admin.MapGet("/order-sets", async ([FromServices] IFormulary f, CancellationToken ct) => Results.Ok(await f.OrderSetsAsync(ct)));
        admin.MapPost("/order-sets", async ([FromBody] CreateOrderSet? body, [FromServices] IFormulary f, CancellationToken ct) =>
            body is null ? Required() : Results.Created("/v1/settings/order-sets", await f.CreateOrderSetAsync(body, ct)));
        return app;
    }

    static async Task<IResult> Search(string? q, int? limit, [FromServices] IFormulary f, CancellationToken ct) =>
        Results.Ok(await f.SearchAsync(q, limit ?? 25, ct));

    static IResult Required() => Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] });

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (FormularyValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (FormularyConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already in the list", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
