using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Settings, Doctors (F-09, MDV-P1-007). The practice side needs the practice-configuration capability like every settings
/// endpoint. Verifying or rejecting a licence is NOT a practice capability: it belongs to the Medvya operations capability
/// (<see cref="OperationsPolicies.LicenceReview"/>), so a practice admin cannot verify their own doctor. The signature image has no read
/// endpoint: it is only for printed prescriptions, which read it from private storage on the server.
/// </summary>
public static class DoctorEndpoints
{
    public sealed record Decision(bool Verified, string? Reason);

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

        // Back office: runs inside the licence's tenant (like a tenant-bound link), so row-level security still scopes every row.
        // shortcut: operations learn which tenant and doctor to review out of band; a cross-tenant review queue needs a reviewed
        // read path that stays inside row-level security. Build it with the back-office tool (open question: MDV-P1-007 notes).
        app.MapPost("/v1/operations/licences/{tenantId:guid}/{doctorId:guid}/decision",
            async (Guid tenantId, Guid doctorId, [FromBody] Decision body, HttpContext http, CancellationToken ct) =>
            {
                try
                {
                    await using var scope = http.RequestServices.EnterLinkTenant(tenantId);
                    return await scope.ServiceProvider.GetRequiredService<ILicenceReview>().DecideAsync(doctorId, body.Verified, body.Reason, ct)
                        ? Results.NoContent() : Results.NotFound();
                }
                catch (SettingsValidationException e)
                {
                    return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(x => x.Key, x => x.Select(y => y.Code).ToArray()));
                }
            }).RequireAuthorization(OperationsPolicies.LicenceReview);

        return app;
    }
}

/// <summary>
/// The Medvya operations capability, kept apart from practice roles. Members are the staff user ids in Operations:LicenceReviewers
/// (configuration, Key Vault / user secrets); a practice role never grants it.
/// shortcut: a configured id list stands in for an operations role in the identity provider; move it to an IdP group claim with Entra (MDV-P0-011).
/// </summary>
public static class OperationsPolicies
{
    public const string LicenceReview = "operations.licence.review";

    public static AuthorizationBuilder AddOperationsPolicies(this AuthorizationBuilder builder, IConfiguration configuration) =>
        builder.AddPolicy(LicenceReview, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            Guid.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            && (configuration.GetSection("Operations:LicenceReviewers").Get<Guid[]>() ?? []).Contains(id)));
}
