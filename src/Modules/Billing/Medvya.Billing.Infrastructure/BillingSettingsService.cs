using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Billing.Infrastructure;

/// <summary>Billing settings (MDV-P4-002): the defaults until the first save, then the tenant's one row. Prefixes follow the country's numbering rules.</summary>
public sealed class BillingSettingsService(BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff, IAuditLog audit, TimeProvider clock,
    IServiceProvider services) : IBillingSettings
{
    public async Task<BillingSettingsView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.Settings.AsNoTracking().Select(s => new { s, Version = EF.Property<uint>(s, "xmin") }).SingleOrDefaultAsync(cancellationToken) is { } row
            ? View(row.s, row.Version) : View(new BillingSettings(), 0); // 0: nothing saved yet

    public async Task<BillingSettingsView> SaveAsync(SaveBillingSettings command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rules = BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));
        var errors = new List<(string, string)>();
        if (command.Version is null) errors.Add(("version", "required")); // a save says which version it read, so it never overwrites a change it has not seen (#889)
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
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        // SAFETY: mutation check: removed this check -> BillingInvoiceTests.RazorpayLinksNeedAConnectedAccount failed (saved without an account)
        if (methods.Contains(PaymentMethod.RazorpayLink) && !await RazorpayConnectedAsync(tenantId, cancellationToken))
            errors.Add(("paymentMethods", "razorpay_not_connected"));
        BillingValidationException.ThrowIfAny(errors);

        var settings = await db.Settings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            if (command.Version is > 0) throw new DbUpdateConcurrencyException(); // the editor read a row that is gone: not ours to recreate blindly
            db.Settings.Add(settings = new BillingSettings { TenantId = tenantId });
        }
        else db.Entry(settings).Property<uint>("xmin").OriginalValue = command.Version!.Value; // refused at save if the row moved on
        (settings.InvoicePrefix, settings.ReceiptPrefix, settings.CreditNotePrefix) = (command.InvoicePrefix!.Trim(), command.ReceiptPrefix!.Trim(), command.CreditNotePrefix!.Trim());
        (settings.PaymentMethods, settings.DiscountReasons) = (methods, reasons);
        (settings.MaxDiscountPercent, settings.PaymentLinkExpiryMinutes) = (command.MaxDiscountPercent!.Value, command.PaymentLinkExpiryMinutes!.Value);
        (settings.ChangedBy, settings.ChangedAt) = (user.UserId, clock.GetUtcNow());

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new DbUpdateConcurrencyException(); // two first saves at once: the later one read no row, so it is the stale one (409)
        }
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = user.UserId, Action = "billing.settings.update", ResourceType = "billing_settings", ResourceId = tenantId,
            Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = settings.ChangedAt, FacilityId = facility.FacilityId,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(settings, db.Entry(settings).Property<uint>("xmin").CurrentValue);
    }

    /// <summary>
    /// Resolved only here: the secret store needs the payments vault (or Development), which the other settings never do. Where it cannot be built
    /// (no Razorpay:KeyVaultUri outside Development) no clinic can have connected, so that is "not connected", not a server error.
    /// </summary>
    async Task<bool> RazorpayConnectedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        IRazorpayCredentialStore store;
        try { store = services.GetRequiredService<IRazorpayCredentialStore>(); }
        catch (InvalidOperationException) { return false; }
        return await store.GetAsync(tenantId, cancellationToken) is not null;
    }

    static BillingSettingsView View(BillingSettings s, uint version) =>
        new(s.InvoicePrefix, s.ReceiptPrefix, s.CreditNotePrefix, s.PaymentMethods, s.DiscountReasons, s.MaxDiscountPercent, s.PaymentLinkExpiryMinutes, version);
}
