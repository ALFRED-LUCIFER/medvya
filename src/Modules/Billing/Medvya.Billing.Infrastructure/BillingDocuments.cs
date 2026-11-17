using System.Globalization;
using System.Security.Cryptography;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Prints a bill or its receipt (MDV-P4-005). The first print of a paper takes the bill under its lock, gives a receipt its number from the receipt series
/// (the A5 and thermal copies of one receipt share it), renders the PDF, writes it to Blob, stores its row with the SHA-256 and audits it last before the
/// commit. A later print of the same paper reads those bytes back, checks them against the hash and audits a re-print: the same document, byte for byte.
/// </summary>
public sealed class BillingDocuments(
    BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff, IFacilityDirectory facilities,
    IBillingSettings settings, IDocumentNumbering numbering, IPatientDirectory patients, IDocumentStore files, IAuditLog audit, TimeProvider clock) : IBillingDocuments
{
    public const string Action = "billing.document.print";
    static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(10);

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid FacilityId => facility.FacilityId ?? throw new InvalidOperationException("No facility context.");

    static string T(string key, params (string, string)[] values) => BillText.Get(key, values);

    public async Task<BillingPdf?> PdfAsync(Guid invoiceId, BillingDocumentKind kind, DocumentLayout layout, CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId;
        var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invoiceId && i.FacilityId == facilityId, cancellationToken);
        if (invoice is null) return null;
        Check(invoice, kind);
        if (await StoredAsync(invoice, kind, layout, cancellationToken) is { } stored) return await ReprintAsync(invoice, stored, cancellationToken);

        // Everything from the other modules and Blob first: no network call inside the transaction below, apart from the PDF's own write.
        var rules = BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));
        var place = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("No facility context.");
        var person = (await patients.SummariesAsync([invoice.PatientId], cancellationToken)).GetValueOrDefault(invoice.PatientId);
        var doctor = invoice.DoctorId is { } membership ? await facilities.MembershipPrescriberAsync(membership, cancellationToken) : null;
        var logo = layout == DocumentLayout.A5 ? await facilities.LogoAsync(cancellationToken) : null;
        var prefix = (await settings.GetAsync(cancellationToken)).ReceiptPrefix;

        byte[] pdf;
        BillingDocument row;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // The bill locked: two first prints at once make one paper, and no payment lands between reading the paid total and printing it.
            await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM billing."Invoices" WHERE "Id" = {invoiceId} AND "FacilityId" = {facilityId} FOR UPDATE""", cancellationToken);
            invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId && i.FacilityId == facilityId, cancellationToken);
            Check(invoice, kind);
            if (await StoredAsync(invoice, kind, layout, cancellationToken) is { } meanwhile)
            {
                await tx.RollbackAsync(cancellationToken);
                return await ReprintAsync(invoice, meanwhile, cancellationToken);
            }
            var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.CreatedAt).ToListAsync(cancellationToken);
            // In the order they succeeded, which is the order the paid total grew in (each success locks the bill).
            var payments = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == invoiceId && p.State == PaymentState.Succeeded)
                .OrderBy(p => p.ConfirmedAt ?? p.RecordedAt).ThenBy(p => p.RecordedAt).ToListAsync(cancellationToken);
            var paid = PaidFor(invoice, kind);
            var now = clock.GetUtcNow();
            // A receipt is for the money received since the bill's last receipt, never again for money already receipted.
            var before = kind == BillingDocumentKind.Receipt
                ? await db.BillingDocuments.Where(d => d.InvoiceId == invoiceId && d.Kind == BillingDocumentKind.Receipt && d.PaidMinor < paid).MaxAsync(d => (long?)d.PaidMinor, cancellationToken) ?? 0
                : 0;
            // The receipt's other layout, if printed already, has its number and date; else the next of the receipt series (gap-free: in this transaction),
            // dated now, so the number's financial year and the printed date are the same instant.
            var sibling = kind == BillingDocumentKind.Receipt
                ? await db.BillingDocuments.Where(d => d.InvoiceId == invoiceId && d.Kind == BillingDocumentKind.Receipt && d.PaidMinor == paid)
                    .Select(d => new { d.Number, d.IssuedAt }).FirstOrDefaultAsync(cancellationToken)
                : null;
            var number = kind == BillingDocumentKind.Bill ? invoice.Number!
                : sibling?.Number ?? (await numbering.IssueAsync(rules.CountryCode, DocumentType.Receipt, prefix, now, cancellationToken)).Value;
            var covered = new List<Payment>();
            long running = 0;
            foreach (var p in payments) { running += p.AmountMinor; if (running > before) covered.Add(p); }
            pdf = BillPdf.Render(Paper(kind, rules, place, logo, person, doctor, invoice, number, lines, covered, before, sibling?.IssuedAt ?? now), layout);
            var id = Guid.CreateVersion7();
            var key = $"{TenantId}/billing/{id}"; // ids only, no names in blob paths
            // Written before the row, as the prescription's PDF: a failed commit leaves only an unreachable blob, never a row without its copy.
            // shortcut: the upload runs while the bill (and, for a receipt, the clinic's receipt series) is locked, so a slow Blob holds payments and the
            // Razorpay webhook for up to this timeout; move the upload out of the transaction (reserve, commit, upload) before more than a few desks share a clinic (#1432).
            using (var upload = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                upload.CancelAfter(UploadTimeout);
                await using var stream = new MemoryStream(pdf, writable: false);
                await files.PutAsync(key, stream, "application/pdf", upload.Token);
            }
            row = new BillingDocument
            {
                Id = id, TenantId = invoice.TenantId, FacilityId = invoice.FacilityId, InvoiceId = invoiceId, Kind = kind, Layout = layout, Number = number, PaidMinor = paid,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf)), BlobKey = key, IssuedBy = user.UserId ?? throw new InvalidOperationException("No user."), IssuedAt = now,
            };
            db.BillingDocuments.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(invoice, row, "original"), tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return new BillingPdf(pdf, row.Sha256);
    }

    /// <summary>A draft has no number and a cancelled bill is void: neither prints. A receipt needs something paid.</summary>
    static void Check(Invoice invoice, BillingDocumentKind kind)
    {
        if (invoice.State is InvoiceState.Draft or InvoiceState.Cancelled) throw new BillingValidationException([("state", "invalid")]);
        if (kind == BillingDocumentKind.Receipt && invoice.PaidMinor == 0) throw new BillingValidationException([("payment", "none")]);
    }

    /// <summary>A receipt is for the paid total at its print; payments only add up (refunds are credit notes, MDV-P4-006), so the total names the payments.</summary>
    static long PaidFor(Invoice invoice, BillingDocumentKind kind) => kind == BillingDocumentKind.Receipt ? invoice.PaidMinor : 0;

    Task<BillingDocument?> StoredAsync(Invoice invoice, BillingDocumentKind kind, DocumentLayout layout, CancellationToken cancellationToken)
    {
        var paid = PaidFor(invoice, kind);
        return db.BillingDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.InvoiceId == invoice.Id && d.Kind == kind && d.Layout == layout && d.PaidMinor == paid, cancellationToken);
    }

    async Task<BillingPdf> ReprintAsync(Invoice invoice, BillingDocument stored, CancellationToken cancellationToken)
    {
        var pdf = await files.ReadAsync(stored.BlobKey, cancellationToken);
        // SAFETY: mutation check: removed this hash check -> BillingDocumentTests.ARePrintIsTheStoredPaperByteForByteAndAChangedBlobIsRefused failed (the altered blob was handed out)
        if (Convert.ToHexStringLower(SHA256.HashData(pdf)) != stored.Sha256) throw new InvalidOperationException("The stored paper does not match its hash.");
        await audit.WriteAsync(Event(invoice, stored, "reprint"), cancellationToken);
        return new BillingPdf(pdf, stored.Sha256);
    }

    AuditEvent Event(Invoice invoice, BillingDocument document, string copy) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = invoice.TenantId, ActorUserId = user.UserId, Action = Action, ResourceType = Invoices.Resource, ResourceId = invoice.Id,
        PatientId = invoice.PatientId, Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = invoice.FacilityId,
        Detail = $"kind:{document.Kind};layout:{document.Layout};copy:{copy}",
    };

    /// <summary>The paper's words and numbers. A bill: its lines and totals, words of the total. A receipt: the same lines, the paid total in words and each payment.</summary>
    static BillPaper Paper(BillingDocumentKind kind, BillingCountryRules rules, FacilityInfo place, byte[]? logo, PatientSummary? person, PrescriberInfo? doctor,
        Invoice invoice, string number, List<InvoiceLine> lines, List<Payment> payments, long receivedBefore, DateTimeOffset issuedAt)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(place.TimeZone);
        string When(DateTimeOffset at) => T("billing.print.when", ("when", TimeZoneInfo.ConvertTime(at, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)), ("zone", place.TimeZone));
        var amount = BillText.Amount;
        var receipt = kind == BillingDocumentKind.Receipt;

        var taxLine = string.Join(" · ", new[]
        {
            place.TaxNumber is { } tax ? T("billing.print.gstin", ("number", tax)) : null,
            place.Registration is { } reg ? T("rxPrint.registration", ("number", reg)) : null,
        }.OfType<string>());
        List<(string, string)> facts = [];
        if (receipt) facts.Add((T("billing.print.against"), invoice.Number!));
        if (doctor is not null)
            facts.Add((T("billing.print.doctor"), string.Join(" · ", new[] { doctor.FullName, doctor.Qualifications, T("printFamily.regShort", ("number", doctor.LicenceNumber)) }.OfType<string>())));
        if (!receipt && rules.PlaceOfSupply(place.TaxNumber) is { } supply) facts.Add((T("billing.print.placeOfSupply"), supply));

        var printed = lines.Select(l => new BillPaperLine(l.Description,
            string.Join(" · ", new[]
            {
                l.Sac is { } sac ? T("billing.invoice.sac", ("sac", sac)) : null,
                l.TaxTreatment == TaxTreatment.Exempt ? T("billing.tax.Exempt")
                    : T("billing.print.gst", ("rate", ((l.GstRateBasisPoints ?? 0) / 100m).ToString("0.##", CultureInfo.InvariantCulture)), ("amount", amount(l.TaxMinor))),
            }.OfType<string>()),
            l.Quantity.ToString(CultureInfo.InvariantCulture), amount(l.UnitMinor), amount(l.AmountMinor))).ToList();

        List<(string, string)> totals;
        string grandLabel, grand, words;
        if (receipt)
        {
            var received = invoice.PaidMinor - receivedBefore;
            totals = [(T("billing.print.billTotal"), amount(invoice.TotalMinor))];
            if (receivedBefore > 0) totals.Add((T("billing.print.receivedBefore"), amount(receivedBefore)));
            if (invoice.TotalMinor > invoice.PaidMinor) totals.Add((T("billing.print.balance"), amount(invoice.TotalMinor - invoice.PaidMinor)));
            (grandLabel, grand) = (T("billing.print.paid", ("currency", invoice.Currency)), amount(received));
            words = rules.AmountInWords(new Money(received, Currency.From(invoice.Currency)));
        }
        else
        {
            totals = [(T("billing.invoice.subtotal"), amount(invoice.SubtotalMinor))];
            if (invoice.DiscountMinor > 0) totals.Add((T("billing.print.discount"), amount(-invoice.DiscountMinor))); // the reason stays on screen: it may name the patient
            if (invoice.TaxMinor > 0) totals.Add((T("billing.print.tax"), amount(invoice.TaxMinor)));
            totals.Add((T("billing.invoice.roundOff"), amount(invoice.RoundOffMinor))); // always printed, even 0.00 (MDV-P4-004)
            (grandLabel, grand) = (T("billing.print.total", ("currency", invoice.Currency)), amount(invoice.TotalMinor));
            words = rules.AmountInWords(new Money(invoice.TotalMinor, Currency.From(invoice.Currency)));
        }

        var paidBy = receipt ? payments.Select(p => T((p.ProviderPaymentId ?? p.Reference) is null ?"billing.print.payment" : "billing.print.paymentRef",
            ("method", T($"billing.method.{p.Method}")), ("reference", p.ProviderPaymentId ?? p.Reference ?? ""), ("amount", amount(p.AmountMinor)),
            ("when", When(p.ConfirmedAt ?? p.RecordedAt)))).ToList() : [];
        var treatments = lines.Select(l => l.TaxTreatment).ToList();
        // A receipt is dated by its own issue (its A5 and thermal copies share it), the instant its number's financial year comes from; a bill by its issue.
        var at = receipt ? issuedAt : invoice.IssuedAt!.Value;
        // The business that supplies the care (the practice's name), as GST asks; the location's own address under it.
        return new BillPaper(place.TenantName, place.Address, place.Phone, taxLine.Length == 0 ? null : taxLine, logo,
            T(receipt ? "billing.print.receipt" : rules.BillTitle(treatments)), number, When(at), facts, person?.FullName ?? "", person?.Mrn ?? "", printed, totals,
            grandLabel, grand, words, paidBy, T(treatments.Contains(TaxTreatment.Exempt) ? "billing.print.exemptNote" : "billing.print.taxNote"));
    }
}
