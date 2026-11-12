using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Patients.Application;
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
        // MDV-P2-009: what the doctor may copy into the note. Same capability and care-relationship check as the note; one audit row per read, no values.
        note.MapGet("/prefill", async (Guid encounterId, [FromServices] IConsultNotes service, CancellationToken ct) =>
                await service.PrefillAsync(encounterId, ct) is { } vitals ? Results.Ok(new { vitals }) : Results.NotFound())
            .AuditedByUseCase("note.prefill", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        note.MapGet("/previous", async (Guid encounterId, [FromServices] IConsultNotes service, CancellationToken ct) =>
                await service.PreviousAsync(encounterId, ct) is { } notes ? Results.Ok(new { notes }) : Results.NotFound())
            .AuditedByUseCase("note.previous", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        note.MapPut("/", async (Guid encounterId, [FromBody] SaveNote? body, [FromServices] IConsultNotes service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] })
                : await service.SaveAsync(encounterId, body, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("note.save", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // MDV-P2-012: Sign on Clinical notes (a licence-verified doctor); the use case adds the care relationship and the 5-minute re-authentication.
        app.MapPost("/v1/encounters/{encounterId:guid}/note/sign", async (Guid encounterId, [FromBody] SignNote? body, [FromServices] IConsultNotes service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] })
                : await service.SignAsync(encounterId, body, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("note.sign", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // MDV-P2-013: an addendum is signed under the same gate as the note; the PDF is a read of the note that also stores the copy it hands out.
        app.MapPost("/v1/encounters/{encounterId:guid}/note/addenda", async (Guid encounterId, [FromBody] AddNoteAddendum? body, [FromServices] IConsultNotes service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] })
                : await service.AddAddendumAsync(encounterId, body, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("note.addendum", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        app.MapPost("/v1/encounters/{encounterId:guid}/note/pdf", async (Guid encounterId, [FromServices] IConsultNotes service, HttpContext http, CancellationToken ct) =>
            {
                if (await service.PdfAsync(encounterId, ct) is not { } pdf) return Results.NotFound();
                http.Response.Headers["X-Content-SHA256"] = pdf.Sha256;
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(pdf.Content, "application/pdf");
            })
            .RequireCapability(Capability.Patient360Chart, Access.Full)
            .AuditedByUseCase("note.pdf", "note", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // Today's pending notes (MDV-P2-002): the signed-in doctor's own unsigned consultations at this facility, with the patient's name to find them.
        // Like the allergy flags, the use case writes one note.pending row per patient answered, so each patient's access log shows it.
        app.MapGet("/v1/notes/pending", Pending).RequireCapability(Capability.Patient360Chart, Access.Full).AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Pending([FromServices] IConsultNotes service, [FromServices] IPatientDirectory patients, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        var (pending, more) = await service.PendingAsync(facilityId, ct);
        var names = await patients.SummariesAsync([.. pending.Select(p => p.PatientId).Distinct()], ct);
        return Results.Ok(new { more, items = pending.Select(p => new { p.EncounterId, p.StartedAt, p.Drafted, p.NoteSigned, p.OpenPastDay, patientName = names.GetValueOrDefault(p.PatientId)?.FullName }) });
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
        catch (NoteSignedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The note is signed", extensions: new Dictionary<string, object?> { ["code"] = "note_signed" });
        }
        catch (NoteNotSignedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The note is not signed", extensions: new Dictionary<string, object?> { ["code"] = "note_not_signed" });
        }
        catch (NotePdfUnsupportedTextException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The note has text the PDF cannot show; use the print view",
                extensions: new Dictionary<string, object?> { ["code"] = "pdf_unsupported_text" });
        }
        catch (ReauthRequiredException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Confirm your identity to sign", extensions: new Dictionary<string, object?> { ["code"] = "reauth_required" });
        }
        catch (StaleNoteException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The note changed since it was read",
                extensions: new Dictionary<string, object?> { ["code"] = "stale_note", ["currentVersion"] = e.Current });
        }
    }
}
