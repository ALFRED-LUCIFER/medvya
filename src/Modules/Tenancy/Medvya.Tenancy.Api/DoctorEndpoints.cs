using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
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

        // The body is the image itself (PNG or JPEG, up to 512 KB), checked by file signature like the logo. Only the doctor's own (403 otherwise).
        g.MapPut("/{id:guid}/signature", (Guid id, HttpRequest request, [FromServices] IDoctors d, CancellationToken ct) => SetSignature(id, request, d, ct));
        g.MapPut("/{id:guid}/room", async (Guid id, [FromBody] DoctorRoom body, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.SetRoomAsync(id, body.Room, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        g.MapDelete("/{id:guid}/signature", async (Guid id, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.RemoveSignatureAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // A doctor's own signature image, without practice administration (#1365): a prescriber's own profile at the location they work in
        // now, found from the session, never from an id the caller sends. 404 for anyone who is not a doctor here.
        var mine = app.MapGroup("/v1/me/signature").RequireCapability(Capability.PrescriptionsOrders, Access.Sign).AddEndpointFilter(SettingsEndpoints.MapErrors);
        mine.MapGet("/", async ([FromServices] IDoctors d, CancellationToken ct) =>
            await d.OwnSignatureAsync(ct) is { } own ? Results.Ok(new { own.HasSignature }) : Results.NotFound());
        mine.MapPut("/", async (HttpRequest request, [FromServices] IDoctors d, CancellationToken ct) =>
            await d.OwnSignatureAsync(ct) is { } own ? await SetSignature(own.DoctorId, request, d, ct) : Results.NotFound());
        mine.MapDelete("/", async ([FromServices] IDoctors d, CancellationToken ct) =>
            await d.OwnSignatureAsync(ct) is { } own && await d.RemoveSignatureAsync(own.DoctorId, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    static async Task<IResult> SetSignature(Guid id, HttpRequest request, IDoctors d, CancellationToken ct)
    {
        if (request.ContentLength > LogoImage.MaxBytes) return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        return await d.SetSignatureAsync(id, buffer.ToArray(), ct) switch
        {
            true => Results.NoContent(),
            false => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the doctor sets their own signature image"),
            null => Results.NotFound(),
        };
    }
}
