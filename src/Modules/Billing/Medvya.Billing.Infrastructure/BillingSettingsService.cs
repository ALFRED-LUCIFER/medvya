using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Billing.Infrastructure;

/// <summary>Billing settings (MDV-P4-002): the defaults until the first save, then the tenant's one row. Prefixes follow the country's numbering rules.</summary>
public sealed class BillingSettingsService(BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff, IAuditLog audit, TimeProvider clock)
    : IBillingSettings
{
    public async Task<BillingSettingsView> GetAsync(CancellationToken cancellationToken = default) =>
        View(await db.Settings.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new BillingSettings());

    public async Task<BillingSettingsView> SaveAsync(SaveBillingSettings command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rules = BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));
        var errors = new List<(string, string)>();
        var prefixes = new[] { ("invoicePrefix", command.InvoicePrefix), ("receiptPrefix", command.ReceiptPrefix), ("creditNotePrefix", command.CreditNotePrefix) };
        foreach (var (field, prefix) in prefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix)) { errors.Add((field, "required")); continue; }
            try { rules.CheckPrefix(prefix.Trim()); }
            catch (ArgumentException) { errors.Add((field, "format")); }
        }
        var methods = (command.PaymentMethods ?? []).Distinct().ToArray();
        if (methods.Length == 0) errors.Add(("paymentMethods", "required"));
        else if (methods.Any(m => !Enum.IsDefined(m))) errors.Add(("paymentMethods", "invalid"));
        var reasons = (command.DiscountReasons ?? []).Select(r => r?.Trim() ?? "").Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (reasons.Length > BillingSettings.MaxReasons) errors.Add(("discountReasons", "too_many"));
        else if (reasons.Any(r => r.Length > BillingSettings.MaxReason)) errors.Add(("discountReasons", "too_long"));
        if (command.MaxDiscountPercent is not (>= 0 and <= 100)) errors.Add(("maxDiscountPercent", "range"));
        if (command.PaymentLinkExpiryMinutes is not (>= BillingSettings.MinLinkExpiry and <= BillingSettings.MaxLinkExpiry)) errors.Add(("paymentLinkExpiryMinutes", "range"));
        BillingValidationException.ThrowIfAny(errors);

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var settings = await db.Settings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null) db.Settings.Add(settings = new BillingSettings { TenantId = tenantId });
        (settings.InvoicePrefix, settings.ReceiptPrefix, settings.CreditNotePrefix) = (command.InvoicePrefix!.Trim(), command.ReceiptPrefix!.Trim(), command.CreditNotePrefix!.Trim());
        (settings.PaymentMethods, settings.DiscountReasons) = (methods, reasons);
        (settings.MaxDiscountPercent, settings.PaymentLinkExpiryMinutes) = (command.MaxDiscountPercent!.Value, command.PaymentLinkExpiryMinutes!.Value);
        (settings.ChangedBy, settings.ChangedAt) = (user.UserId, clock.GetUtcNow());

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = user.UserId, Action = "billing.settings.update", ResourceType = "billing_settings", ResourceId = tenantId,
            Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = settings.ChangedAt, FacilityId = facility.FacilityId,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(settings);
    }

    static BillingSettingsView View(BillingSettings s) =>
        new(s.InvoicePrefix, s.ReceiptPrefix, s.CreditNotePrefix, s.PaymentMethods, s.DiscountReasons, s.MaxDiscountPercent, s.PaymentLinkExpiryMinutes);
}
