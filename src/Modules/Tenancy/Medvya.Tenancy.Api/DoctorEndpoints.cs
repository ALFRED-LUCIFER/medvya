using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Settings, Doctors (F-09, MDV-P1-007). The practice side needs the practice-configuration capability like every settings
/// endpoint. The owner attests each doctor's licence (ADR-016); Medvya does not verify doctors. The signature image has no read
/// endpoint: it is only for printed prescriptions, which read it from private storage on the server.
/// </summary>
public static class DoctorEndpoints
{
    public static WebApplication MapDoctors(this WebApplication app)
    {
        var g = app.MapGroup("/v1/settings/doctors").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(SettingsEndpoints.MapErrors);

        g.MapGet("/", ([FromServices] IDoctors d, CancellationToken ct) => d.ListAsync(ct));
        g.MapPost("/", async ([FromBody] AddDoctor body, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.AddAsync(body, ct) is { } v ? Results.Created($"/v1/settings/doctors/{v.Id}", v) : Results.NotFound());
        g.MapPut("/{id:guid}/licence", async (Guid id, [FromBody] ResubmitLicence body, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.ResubmitLicenceAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // The body is the image itself (PNG or JPEG, up to 512 KB), checked by file signature like the logo.
        g.MapPut("/{id:guid}/signature", async (Guid id, HttpRequest request, [FromServices] IDoctors d, CancellationToken ct) =>
        {
            if (request.ContentLength > LogoImage.MaxBytes) return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            return await d.SetSignatureAsync(id, buffer.ToArray(), ct) ? Results.NoContent() : Results.NotFound();
        });
        g.MapDelete("/{id:guid}/signature", async (Guid id, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.RemoveSignatureAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
