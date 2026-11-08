using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

/// <summary>A document in the list, with who uploaded it by name.</summary>
public sealed record DocumentRow(Guid Id, DocumentType Type, DateOnly DocumentDate, ChartSource Source, ScanStatus ScanStatus, string ContentType, long SizeBytes, string? UploadedBy, DateTimeOffset UploadedAt);

/// <summary>Who the documents are for, so no file is added to the wrong patient: name, MRN and date of birth (the front desk reads these anyway).</summary>
public sealed record DocumentPatient(string FullName, string Mrn, DateOnly DateOfBirth);

public sealed record DocumentListResponse(DocumentPatient Patient, IReadOnlyList<DocumentRow> Documents, bool CanOpen, string TimeZone);

/// <summary>What the viewer needs: the link, when it stops working, and who opens it and when (printed over the document as a watermark).</summary>
public sealed record ViewLinkResponse(Uri Url, DateTimeOffset ExpiresAt, string ContentType, string Viewer, DateTimeOffset ViewedAt);

public sealed record UpdateDocumentRequest(DocumentType Type, DateOnly DocumentDate);

public static class DocumentEndpoints
{
    /// <summary>
    /// Documents (MDV-P2-006). List and upload: Register / book patients at Read or Full (nurses hold Read, the front desk Full); the use case then decides between a
    /// clinician with a care relationship and the desk (which never opens a file). Opening and editing the type or date: Patient 360 roles only.
    /// The file travels as the request body (no multipart: nothing about it is in a URL, and the CSRF header rides on it like every POST); the
    /// type, date and source in the query string are codes and a date. A refusal, a rejected file or an unviewable one is audited here.
    /// </summary>
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/patients/{patientId:guid}/documents", List)
            .RequireCapability(Capability.RegisterBookPatients, Access.Read, Access.Full)
            .AuditedByUseCase(DocumentPolicy.ListAction, "document", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);

        // Slightly over the limit so that the use case, not Kestrel, answers "too large" for a file at the limit; Kestrel stops anything bigger.
        app.MapPost("/v1/patients/{patientId:guid}/documents", Upload)
            .RequireCapability(Capability.RegisterBookPatients, Access.Read, Access.Full)
            .WithMetadata(new RequestSizeLimitAttribute(DocumentPolicy.MaxBytes + 4096))
            .AuditedByUseCase(DocumentPolicy.UploadAction, "document", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);

        // POST, not GET: issuing a link is an audited action, never prefetched or cached.
        app.MapPost("/v1/documents/{documentId:guid}/view-link", ViewLink)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase(DocumentPolicy.ViewAction, "document", AuditPurpose.Treatment)
            .AddEndpointFilter(MapErrors);

        app.MapPatch("/v1/documents/{documentId:guid}", Update)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase(DocumentPolicy.UpdateAction, "document", AuditPurpose.Treatment)
            .AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> List(Guid patientId, [FromServices] IDocuments documents, [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users,
        [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!(await patients.SummariesAsync([patientId], ct)).TryGetValue(patientId, out var p)) return Results.NotFound();
        var list = await documents.ListAsync(patientId, facilityId, ct); // refuses before anything about the patient is returned
        var names = await users.DisplayNamesAsync([.. list.Documents.Select(d => d.UploadedBy).Distinct()], ct);
        return Results.Ok(new DocumentListResponse(new(p.FullName, p.Mrn, p.DateOfBirth), [.. list.Documents.Select(d => Row(d, names))], list.CanOpen,
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone));
    }

    static async Task<IResult> Upload(Guid patientId, string? type, DateOnly? date, string? source, HttpRequest request, [FromServices] IDocuments documents,
        [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!Enum.TryParse<DocumentType>(type, out var t) || date is not { } d || !Enum.TryParse<ChartSource>(source, out var s))
            return Rejected("invalid");
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        var view = await documents.UploadAsync(patientId, facilityId, t, d, s, request.Body, ct);
        return Results.Ok(Row(view, await users.DisplayNamesAsync([view.UploadedBy], ct)));
    }

    static async Task<IResult> ViewLink([FromRoute] Guid documentId, [FromServices] IDocuments documents, [FromServices] IStaffUsers users, [FromServices] ICurrentUser user,
        [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (await documents.ViewLinkAsync(documentId, ct) is not { } issued) return Results.NotFound();
        var viewer = user.UserId is { } me ? (await users.DisplayNamesAsync([me], ct)).GetValueOrDefault(me) : null;
        return Results.Ok(new ViewLinkResponse(issued.Link.Url, issued.Link.ExpiresAt, issued.Link.ContentType, viewer ?? "", clock.GetUtcNow()));
    }

    static async Task<IResult> Update(Guid documentId, [FromBody] UpdateDocumentRequest? body, [FromServices] IDocuments documents, [FromServices] IStaffUsers users, CancellationToken ct)
    {
        if (body is null) return Rejected("invalid");
        return await documents.UpdateAsync(documentId, body.Type, body.DocumentDate, ct) is { } view
            ? Results.Ok(Row(view, await users.DisplayNamesAsync([view.UploadedBy], ct))) : Results.NotFound();
    }

    static DocumentRow Row(DocumentView d, IReadOnlyDictionary<Guid, string> names) =>
        new(d.Id, d.Type, d.DocumentDate, d.Source, d.ScanStatus, d.ContentType, d.SizeBytes, names.GetValueOrDefault(d.UploadedBy), d.UploadedAt);

    static IResult Rejected(string code) => Results.Problem(
        statusCode: code switch { "too_large" => StatusCodes.Status413PayloadTooLarge, "unsupported_type" => StatusCodes.Status415UnsupportedMediaType, _ => StatusCodes.Status400BadRequest },
        title: "Document rejected", extensions: new Dictionary<string, object?> { ["code"] = code });

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
        catch (DocumentRejectedException e)
        {
            return Rejected(e.Code);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Rejected("too_large");
        }
        catch (DocumentNotViewableException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Document cannot be opened",
                extensions: new Dictionary<string, object?> { ["code"] = e.Status == ScanStatus.Pending ? "scan_pending" : "quarantined" });
        }
        catch (DocumentStorageUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Document storage is not available");
        }
    }
}
