using System.Text.RegularExpressions;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure.Razorpay;

/// <summary>
/// The current clinic's Razorpay account (MDV-P4-007 part 2, ADR-023): its keys go only to the secret store; its webhooks are de-duplicated by event id
/// and only ever prompt a fresh fetch of the link (<see cref="IInvoices.ConfirmPaymentLinkAsync"/>), never trusted for an amount or a state.
/// </summary>
public sealed partial class RazorpayAccount(
    BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IRazorpayCredentialStore store, IInvoices invoices, IAuditLog audit,
    TimeProvider clock) : IRazorpayAccount
{
    public const string Provider = "razorpay", Resource = "payment_provider";
    const int MaxSecret = 256;

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    [GeneratedRegex("^rzp_(test|live)_[A-Za-z0-9]{1,40}$")]
    private static partial Regex KeyIdFormat();

    public async Task<RazorpayStatus> GetAsync(CancellationToken cancellationToken = default) => Status(await store.GetAsync(TenantId, cancellationToken));

    public async Task<RazorpayStatus> ConnectAsync(ConnectRazorpay command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (keyId, keySecret, webhookSecret) = (command.KeyId?.Trim() ?? "", command.KeySecret?.Trim() ?? "", command.WebhookSecret?.Trim() ?? "");
        var errors = new List<(string, string)>();
        if (keyId.Length == 0) errors.Add(("keyId", "required"));
        else if (!KeyIdFormat().IsMatch(keyId)) errors.Add(("keyId", "format"));
        foreach (var (field, value) in new[] { ("keySecret", keySecret), ("webhookSecret", webhookSecret) })
            if (value.Length == 0) errors.Add((field, "required"));
            else if (value.Length > MaxSecret) errors.Add((field, "too_long"));
        BillingValidationException.ThrowIfAny(errors);

        // shortcut: the keys are not tried against Razorpay before saving; a wrong key shows as "provider_unauthorized" on the first payment link.
        var saved = await store.SaveAsync(TenantId, keyId, keySecret, webhookSecret, cancellationToken);
        // After the secret store's write (it has no transaction to join); the row names the action only, never a key.
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "billing.razorpay.connect", ResourceType = Resource, ResourceId = TenantId,
            Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = facility.FacilityId,
        }, cancellationToken);
        return Status(saved);
    }

    public async Task<bool> HandleWebhookAsync(RazorpayWebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        var tenantId = TenantId;
        // SAFETY: mutation check: removed this replay check (and the row below) -> BillingInvoiceTests.AReplayedEventChangesStateOnce failed (each replay fetched the link again)
        if (await db.ProviderEvents.AnyAsync(e => e.Provider == Provider && e.EventId == webhookEvent.EventId, cancellationToken)) return false;
        // payment_link.paid/expired/cancelled carry the link; payment.failed carries none and changes nothing (the patient may pay again on the same link).
        if (webhookEvent.PaymentLinkId is { Length: > 0 } linkId && webhookEvent.Type.StartsWith("payment", StringComparison.Ordinal))
            await invoices.ConfirmPaymentLinkAsync(tenantId, linkId, cancellationToken);
        db.ProviderEvents.Add(new ProviderEvent
        {
            TenantId = tenantId, Provider = Provider, EventId = webhookEvent.EventId,
            Type = webhookEvent.Type.Length > ProviderEvent.MaxType ? webhookEvent.Type[..ProviderEvent.MaxType] : webhookEvent.Type, ReceivedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return false; // the same event delivered twice at once: the other delivery recorded it, and settling is once only anyway
        }
    }

    public Task RecordForgedWebhookAsync(CancellationToken cancellationToken = default) => audit.WriteAsync(new AuditEvent
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, Action = "billing.webhook.forged", ResourceType = Resource, ResourceId = TenantId,
        Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Denied, OccurredAt = clock.GetUtcNow(),
    }, cancellationToken);

    static RazorpayStatus Status(RazorpayCredentials? c) => c is null
        ? new(false, null, null, null)
        : new(true, c.Mode, c.KeyId.Length > 4 ? c.KeyId[^4..] : null, RazorpayCredentials.IsWebhookKey(c.WebhookKey) ? RazorpayWebhook.PathFor(c.WebhookKey) : null);
}
