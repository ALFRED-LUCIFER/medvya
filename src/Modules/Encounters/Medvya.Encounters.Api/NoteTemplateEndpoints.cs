using Medvya.Encounters.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

public static class NoteTemplateEndpoints
{
    /// <summary>Note templates (MDV-P2-025): a doctor's capability. No patient data here, so no patient audit (listed in EndpointAuditTests).</summary>
    public static IEndpointRouteBuilder MapNoteTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/note-templates").RequireCapability(Capability.Patient360Chart, Access.Full);
        g.MapGet("/", async ([FromServices] INoteTemplates service, CancellationToken ct) => Results.Ok(await service.CatalogAsync(ct)));
        g.MapPut("/mine", async ([FromBody] ChooseNoteTemplates? body, [FromServices] INoteTemplates service, CancellationToken ct) =>
            body is not null && await service.ChooseAsync(body, ct) is { } mine ? Results.Ok(mine)
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["keys"] = ["invalid"] }));
        return app;
    }
}
