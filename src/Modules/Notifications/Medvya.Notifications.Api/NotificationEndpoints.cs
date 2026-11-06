using Medvya.Tenancy.Api;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medvya.Audit.Api;
using Medvya.Audit.Application;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Medvya.Notifications.Api;

/// <summary>
/// What reception sees of patient messages (MDV-P1-032/033) and the provider receipt webhook. Staff endpoints need sign-in and a view
/// membership at the facility (checked in the use case); they return statuses and codes, never text or addresses.
/// </summary>
public static class NotificationEndpoints
{
    public const string SignatureHeader = "X-Medvya-Signature";
    static readonly IResult NoFacility = Results.ValidationProblem(new Dictionary<string, string[]> { ["facilityId"] = ["required"] });
    public sealed record FacilityBody(Guid FacilityId);
    public sealed record ReceiptBody(string? Ref, string? State, string? ReasonCode);

    public static WebApplication MapNotifications(this WebApplication app)
    {
        var staff = app.MapGroup("/v1").RequireAuthorization().AddEndpointFilter(MapErrors);

        staff.MapGet("/appointments/{appointmentId:guid}/notifications", async (Guid appointmentId, Guid? facilityId, [FromServices] INotificationQueries q, CancellationToken ct) =>
                facilityId is not { } f || f == Guid.Empty ? NoFacility : Results.Ok(await q.ForAppointmentAsync(f, appointmentId, ct)))
            .Audited("notification.read", "notification", window: AuditWindow.Repeated); // the visit panel polls it every 30 s

        // Delivery rate per channel and every failure with its reason code (exit gate 4). Default window: the last 14 days.
        staff.MapGet("/notifications/delivery", async (Guid? facilityId, int? days, [FromServices] INotificationQueries q, [FromServices] TimeProvider clock, CancellationToken ct) =>
                facilityId is not { } f || f == Guid.Empty ? NoFacility : Results.Ok(await q.DeliveryAsync(f, clock.GetUtcNow().AddDays(-Math.Clamp(days ?? 14, 1, 90)), 100, ct)))
            .Audited("notification.delivery.read", "notification", "operations");

        // The notification centre (D-03) and the board's failed-message marks: ids and kinds only, no patient data, so no purpose-bound audit row.
        staff.MapGet("/notifications/inbox", async (Guid? facilityId, [FromServices] IStaffNotifications n, CancellationToken ct) =>
            facilityId is not { } f || f == Guid.Empty ? NoFacility : Results.Ok(await n.ListAsync(f, ct)));
        staff.MapPost("/notifications/inbox/read-all", async ([FromBody] FacilityBody body, [FromServices] IStaffNotifications n, CancellationToken ct) =>
        {
            await n.MarkAllReadAsync(body.FacilityId, ct);
            return Results.NoContent();
        });
        staff.MapPost("/notifications/inbox/{id:guid}/read", async (Guid id, [FromBody] FacilityBody body, [FromServices] IStaffNotifications n, CancellationToken ct) =>
            await n.MarkReadAsync(body.FacilityId, id, ct) ? Results.NoContent() : Results.NotFound());
        staff.MapGet("/notifications/failed-visits", async (Guid? facilityId, [FromServices] INotificationQueries q, CancellationToken ct) =>
            facilityId is not { } f || f == Guid.Empty ? NoFacility : Results.Ok(await q.FailedVisitsAsync(f, ct)));

        // Settings -> Notifications (MDV-P1-006): practice configuration only (like the rest of /v1/settings). No patient data; a save is audited inside the service.
        var settings = app.MapGroup("/v1/settings/notifications").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(SettingsEndpoints.MapErrors);
        settings.MapGet("", ([FromServices] INotificationPreferences p, CancellationToken ct) => p.GetAsync(ct));
        settings.MapPut("", ([FromBody] NotificationPreferencesView body, [FromServices] INotificationPreferences p, CancellationToken ct) => p.SaveAsync(body, ct));
        settings.MapGet("/templates", (string? language) => MessageCatalog.Previews(language));

        // Public on purpose: the provider (or its adapter) cannot sign in. The HMAC of the raw body is the credential; without a configured
        // secret every call is refused. The reference names the tenant, so the receipt is applied inside that tenant's scope (RLS).
        app.MapPost("/v1/notifications/receipts", async (HttpRequest request, HttpContext http, [FromServices] IOptions<NotificationOptions> options, CancellationToken ct) =>
        {
            if (request.ContentLength is null or > 4096) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (!SignatureValid(options.Value.ReceiptSecret, buffer.ToArray(), request.Headers[SignatureHeader].ToString())) return Results.Unauthorized();
            ReceiptBody? body;
            try { body = JsonSerializer.Deserialize<ReceiptBody>(buffer.ToArray(), JsonSerializerOptions.Web); }
            catch (JsonException) { return Results.BadRequest(); }
            if (body is null || !Enum.TryParse<NotificationStatus>(body.State, ignoreCase: true, out var state)
                || !NotificationReference.TryParse(body.Ref, out var tenant, out _)) return Results.BadRequest();
            await using var scope = http.RequestServices.EnterLinkTenant(tenant);
            await scope.ServiceProvider.GetRequiredService<IReceiptProcessor>().ApplyAsync(new DeliveryReceipt(body.Ref!, state, body.ReasonCode), ct);
            return Results.NoContent(); // unknown or repeated receipts are fine: the provider must not retry them
        }).AllowAnonymous().RequireRateLimiting("receipts");

        return app;
    }

    /// <summary>Hex HMAC-SHA256 of the body with the shared secret, compared in constant time. No secret means no valid signature.</summary>
    public static bool SignatureValid(string? secret, byte[] body, string? signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (NotificationAccessDeniedException) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access at this facility"); }
    }
}
