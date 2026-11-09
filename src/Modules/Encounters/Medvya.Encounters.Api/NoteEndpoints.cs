using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

public static class NoteEndpoints
{
    /// <summary>
    /// The consultation note draft (MDV-P2-009). A doctor's capability (Patient 360 at Full); the use case then checks clinical access to the
    /// encounter's patient and audits with the patient, never the text. Ids only in paths; the text only in the body.
    /// </summary>
    public static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var note = app.MapGroup("/v1/encounters/{encounterId:guid}/note").RequireCapability(Capability.Patient360Chart, Access.Full);
        note.MapGet("/", async (Guid encounterId, [FromServices] IConsultNotes service, CancellationToken ct) =>
                await service.GetAsync(encounterId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("note.read", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        note.MapPut("/", async (Guid encounterId, [FromBody] SaveNote? body, [FromServices] IConsultNotes service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] })
                : await service.SaveAsync(encounterId, body, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("note.save", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        return app;
    }

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
        catch (NoteValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (StaleNoteException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The note changed since it was read",
                extensions: new Dictionary<string, object?> { ["code"] = "stale_note", ["currentVersion"] = e.Current });
        }
    }
}
