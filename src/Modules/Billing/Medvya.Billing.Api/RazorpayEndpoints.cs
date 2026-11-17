using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Billing.Api;

/// <summary>
/// The clinic's own Razorpay account (MDV-P4-007 part 2, ADR-023): Settings, Billing, Razorpay for the practice-configuration capability while billing is
/// on, and the clinic's public webhook. Secrets go in, never out.
/// </summary>
public static class RazorpayEndpoints
{
    public const string WebhookRateLimit = "payment-webhooks", SignatureHeader = "X-Razorpay-Signature", EventIdHeader = "X-Razorpay-Event-Id";
    const string Resource = "payment_provider";
    /// <summary>A Razorpay payment link event is a few kilobytes; anything near this is not one.</summary>
    public const int MaxBody = 64 * 1024;

    public static IEndpointRouteBuilder MapRazorpay(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/settings/billing/razorpay").RequireAuthorization(StaffPolicies.ManageStaff).RequireFeature(Features.Billing);
        admin.MapGet("", async ([FromServices] IRazorpayAccount a, CancellationToken ct) => Results.Ok(await a.GetAsync(ct)))
            .Audited("billing.razorpay.read", Resource, AuditPurpose.Operations).AddEndpointFilter(CatalogueEndpoints.MapErrors);
        admin.MapPut("", async ([FromBody] ConnectRazorpay? body, [FromServices] IRazorpayAccount a, CancellationToken ct) =>
                body is null ? CatalogueEndpoints.Required() : Results.Ok(await a.ConnectAsync(body, ct)))
            .AuditedByUseCase("billing.razorpay.connect", Resource, AuditPurpose.Operations).AddEndpointFilter(CatalogueEndpoints.MapErrors);

        // Public on purpose: Razorpay cannot sign in. The opaque key in the path picks the clinic (and so its webhook secret) and proves nothing; the HMAC of
        // the raw body under that secret is the credential, checked before anything is parsed. Not behind the billing flag: a link paid while billing was on
        // must still settle after it is turned off, and a webhook only ever settles a pending payment that already exists.
        app.MapPost("/v1/webhooks/razorpay/{webhookKey}", Webhook).AllowAnonymous().RequireRateLimiting(WebhookRateLimit);
        return app;
    }

    static async Task<IResult> Webhook(string webhookKey, HttpContext http, [FromServices] IRazorpayCredentialStore store, CancellationToken ct)
    {
        // The tenant comes only from the key, never from the payload (whose notes name one).
        // SAFETY: mutation check: entered the tenant named in the payload's notes instead of found.TenantId -> BillingInvoiceTests.AnotherClinicsKeyCannotSettleThisClinicsPayment failed
        if (await store.FindByWebhookKeyAsync(webhookKey, ct) is not { } found) return Results.NotFound();
        if (http.Request.ContentLength > MaxBody || await ReadAsync(http.Request.Body, ct) is not { } body) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        await using var scope = http.RequestServices.EnterLinkTenant(found.TenantId); // the clinic's RLS context, as for any tenant-bound link
        var account = scope.ServiceProvider.GetRequiredService<IRazorpayAccount>();
        // SAFETY: mutation check: skipped this check -> BillingInvoiceTests.AForgedOrUnsignedWebhookIsRefusedAndChangesNothing failed (the forged event settled the payment)
        if (!RazorpayWebhook.IsSignatureValid(body, http.Request.Headers[SignatureHeader], found.Credentials.WebhookSecret))
        {
            await account.RecordForgedWebhookAsync(ct);
            return Results.Unauthorized();
        }
        if (RazorpayWebhook.Parse(body, http.Request.Headers[EventIdHeader]) is not { } webhookEvent || webhookEvent.EventId.Length > ProviderEvent.MaxEventId)
            return Results.BadRequest();
        try
        {
            await account.HandleWebhookAsync(webhookEvent, ct);
        }
        catch (PaymentProviderException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); // not recorded as handled: Razorpay's retry tries again
        }
        return Results.Ok();
    }

    /// <summary>The raw body bytes, or null when it is larger than <see cref="MaxBody"/> (a chunked request has no length to check first).</summary>
    static async Task<byte[]?> ReadAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBody) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
