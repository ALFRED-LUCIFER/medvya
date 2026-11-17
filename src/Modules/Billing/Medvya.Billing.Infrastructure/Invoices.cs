using System.Text.RegularExpressions;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Bills and payments (MDV-P4-004, split payments from MDV-P4-009). Each change locks the bill row, checks the move with <see cref="InvoiceFlow"/>, saves,
/// writes its audit row last and commits. Payments are serialised by that lock, so two desks cannot together collect more than the balance; the
/// database's CK_Invoices_Paid is the backstop. Provider calls (Razorpay) happen between transactions, never inside one.
/// </summary>
public sealed partial class Invoices(
    BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff, IFacilityDirectory facilities,
    IBillingSettings settings, ICatalogue catalogue, IDocumentNumbering numbering, IPatientDirectory patients, IConsultVisits visits, IPaymentLinks links,
    IAuditLog audit, TimeProvider clock, ILogger<Invoices> log) : IInvoices
{
    public const string Resource = "invoice";
    /// <summary>Razorpay wants a link to expire at least 15 minutes after it receives it; this covers the time the request takes.</summary>
    static readonly TimeSpan LinkExpiryMargin = TimeSpan.FromMinutes(2);
    /// <summary>How long a pending payment may wait for its link (the provider call takes at most 10 s) before "Check status" may fail it.</summary>
    static readonly TimeSpan LinkCreationGrace = TimeSpan.FromMinutes(2);
    /// <summary>Razorpay's smallest link amount is 100 paise.</summary>
    const long MinLinkMinor = 100;
    static readonly AppointmentState[] Attended =
        [AppointmentState.CheckedIn, AppointmentState.WithNurse, AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing, AppointmentState.Done];

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid FacilityId => facility.FacilityId ?? throw new InvalidOperationException("No facility context.");

    // ---- Reads ----

    public async Task<IReadOnlyList<InvoiceRow>> SearchAsync(InvoiceSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var facilityId = FacilityId;
        var query = db.Invoices.AsNoTracking().Where(i => i.FacilityId == facilityId);
        if (search.State is { } state) query = query.Where(i => i.State == state);
        if (search.PatientId is { } patient) query = query.Where(i => i.PatientId == patient);
        if (search.From is not null || search.To is not null)
        {
            var zone = await ZoneAsync(cancellationToken);
            if (search.From is { } from) { var start = StartOfDay(from, zone); query = query.Where(i => i.CreatedAt >= start); }
            if (search.To is { } to) { var end = StartOfDay(to.AddDays(1), zone); query = query.Where(i => i.CreatedAt < end); }
        }
        // shortcut: the newest 200 bills of the filter, no paging; page on the server when a clinic's day holds more (MDV-P4-012 day close reads totals itself).
        var rows = await query.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new { i, Awaiting = db.Payments.Any(p => p.InvoiceId == i.Id && p.State == PaymentState.Pending) }).ToListAsync(cancellationToken);
        var people = await patients.SummariesAsync([.. rows.Select(r => r.i.PatientId).Distinct()], cancellationToken);
        return [.. rows.Select(r => new InvoiceRow(r.i.Id, r.i.State, r.i.Number, r.i.PatientId, people.GetValueOrDefault(r.i.PatientId)?.FullName ?? "",
            people.GetValueOrDefault(r.i.PatientId)?.Mrn ?? "", r.i.Currency, r.i.TotalMinor, r.i.PaidMinor, r.Awaiting, r.i.CreatedAt, r.i.IssuedAt))];
    }

    public async Task<InvoiceView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId;
        var row = await db.Invoices.AsNoTracking().Where(i => i.Id == id && i.FacilityId == facilityId)
            .Select(i => new { i, Version = EF.Property<uint>(i, "xmin") }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == id).OrderBy(l => l.CreatedAt).ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == id).OrderBy(p => p.RecordedAt).ToListAsync(cancellationToken);
        var person = (await patients.SummariesAsync([row.i.PatientId], cancellationToken)).GetValueOrDefault(row.i.PatientId);
        var s = await settings.GetAsync(cancellationToken);
        var config = await staff.AccessAsync(Capability.StaffRolesIntegrations, cancellationToken) == Access.Config;
        var i = row.i;
        return new InvoiceView(i.Id, i.State, i.Number, i.IssuedAt,
            new InvoicePatient(i.PatientId, person?.FullName ?? "", person?.Mrn ?? "", person?.DateOfBirth ?? default, person?.SexAtBirth.ToString() ?? ""),
            i.AppointmentId, i.DoctorId, i.Currency,
            [.. lines.Select(l => new InvoiceLineView(l.Id, l.ServiceId, l.PriceVersionId, l.Description, l.Sac, l.Quantity, l.UnitMinor, l.TaxTreatment, l.GstRateBasisPoints, l.TaxMinor, l.AmountMinor, l.Suggested))],
            i.SubtotalMinor, i.DiscountMinor, i.DiscountReason, i.TaxMinor, i.RoundOffMinor, i.TotalMinor, i.PaidMinor, Balance(i),
            [.. payments.Select(p => new PaymentView(p.Id, p.Method, p.AmountMinor, p.State, p.Reference, p.State == PaymentState.Pending ? p.LinkUrl : null, p.RecordedAt, p.ConfirmedAt))],
            i.CancelReason, i.CancelledAt, i.CreatedAt, new InvoiceOptions(s.PaymentMethods, s.DiscountReasons, s.MaxDiscountPercent, config), row.Version);
    }

    static long Balance(Invoice i) => i.State is InvoiceState.Draft or InvoiceState.Cancelled ? 0 : i.TotalMinor - i.PaidMinor;

    // ---- Drafts ----

    public async Task<InvoiceView?> CreateAsync(NewInvoice command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PatientId is not { } patientId) throw new BillingValidationException([("patientId", "required")]);
        if ((await patients.SummariesAsync([patientId], cancellationToken)).Count == 0) return null; // not one of the tenant's patients
        var invoice = await NewDraftAsync(patientId, null, null, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, "billing.invoice.create", invoice, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(invoice.Id, cancellationToken);
    }

    public async Task<InvoiceView?> ForVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var visit = await visits.FindAsync(appointmentId, cancellationToken);
        if (visit is null || visit.FacilityId != FacilityId) return null;
        if (await OpenForVisitAsync(appointmentId, cancellationToken) is { } open)
        {
            var existing = await GetAsync(open, cancellationToken);
            await audit.WriteAsync(new AuditEvent // opening the visit's bill again changes nothing, but it is a look at the bill
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "billing.invoice.read", ResourceType = Resource, ResourceId = open,
                PatientId = visit.PatientId, Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = visit.FacilityId,
            }, cancellationToken);
            return existing;
        }
        if (!Attended.Contains(visit.State)) throw new BillingValidationException([("visit", "not_attended")]);

        var doctor = visit.CalendarKind == CalendarKind.Doctor ? await facilities.DoctorMembershipAsync(visit.CalendarId, visit.FacilityId, cancellationToken) : null;
        var invoice = await NewDraftAsync(visit.PatientId, appointmentId, doctor, cancellationToken);
        // The consultation the visit was booked as; a procedure visit has no consultation to suggest (its services are added by hand).
        ConsultationKind? kind = visit.VisitType switch
        {
            VisitType.FollowUp => ConsultationKind.FollowUp,
            VisitType.NewConsultation or VisitType.Teleconsult => ConsultationKind.New,
            _ => null,
        };
        var lines = new List<InvoiceLine>();
        var service = kind is null ? null : await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.ConsultationKind == kind && s.ArchivedAt == null, cancellationToken);
        if (service is not null && await catalogue.ResolvePriceAsync(service.Id, doctor, clock.GetUtcNow(), cancellationToken) is { } price && price.Currency == invoice.Currency)
            lines.Add(NewLine(invoice, service, price, 1, suggested: true));
        invoice.Recalculate(lines, (await RulesAsync(cancellationToken)).RoundOff);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Invoices.Add(invoice);
            db.InvoiceLines.AddRange(lines);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.invoice.create", invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // another desk opened the visit's bill at the same moment: open theirs
        }
        return await GetAsync(await OpenForVisitAsync(appointmentId, cancellationToken) ?? invoice.Id, cancellationToken);
    }

    Task<Guid?> OpenForVisitAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        db.Invoices.AsNoTracking().Where(i => i.AppointmentId == appointmentId && i.State != InvoiceState.Cancelled).Select(i => (Guid?)i.Id).SingleOrDefaultAsync(cancellationToken);

    async Task<Invoice> NewDraftAsync(Guid patientId, Guid? appointmentId, Guid? doctorId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return new Invoice
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = FacilityId, PatientId = patientId, AppointmentId = appointmentId, DoctorId = doctorId,
            State = InvoiceState.Draft, Currency = (await RulesAsync(cancellationToken)).Currency.Code, CreatedBy = user.UserId, CreatedAt = now, ChangedBy = user.UserId, ChangedAt = now,
        };
    }

    InvoiceLine NewLine(Invoice invoice, Service service, ResolvedPrice price, int quantity, bool suggested)
    {
        var line = new InvoiceLine
        {
            Id = Guid.CreateVersion7(), TenantId = invoice.TenantId, InvoiceId = invoice.Id, ServiceId = service.Id, PriceVersionId = price.PriceVersionId,
            Description = service.Name, Sac = service.Sac, UnitMinor = price.AmountMinor, TaxTreatment = service.TaxTreatment,
            GstRateBasisPoints = service.GstRateBasisPoints, Suggested = suggested, CreatedAt = clock.GetUtcNow(),
        };
        line.SetQuantity(quantity);
        return line;
    }

    public Task<InvoiceView?> AddLineAsync(Guid id, AddLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EditDraftAsync(id, command.Version, "billing.invoice.line_add", async (invoice, lines) =>
        {
            var errors = new List<(string, string)>();
            if (command.ServiceId is null) errors.Add(("serviceId", "required"));
            if (command.Quantity is not (>= 1 and <= InvoiceLine.MaxQuantity)) errors.Add(("quantity", "range"));
            BillingValidationException.ThrowIfAny(errors);
            var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == command.ServiceId && s.ArchivedAt == null, cancellationToken)
                ?? throw new BillingValidationException([("serviceId", "unknown")]);
            // The doctor's own price beats the clinic default (MDV-P4-002); an unpriced service cannot be billed.
            var price = await catalogue.ResolvePriceAsync(service.Id, invoice.DoctorId, clock.GetUtcNow(), cancellationToken);
            if (price is null || price.Currency != invoice.Currency) throw new BillingValidationException([("serviceId", "no_price")]);
            var line = NewLine(invoice, service, price, command.Quantity!.Value, suggested: false);
            db.InvoiceLines.Add(line);
            lines.Add(line);
        }, cancellationToken);
    }

    public Task<InvoiceView?> UpdateLineAsync(Guid id, Guid lineId, UpdateLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EditDraftAsync(id, command.Version, "billing.invoice.line_update", (_, lines) =>
        {
            if (command.Quantity is not (>= 1 and <= InvoiceLine.MaxQuantity)) throw new BillingValidationException([("quantity", "range")]);
            var line = lines.SingleOrDefault(l => l.Id == lineId) ?? throw new BillingValidationException([("lineId", "unknown")]);
            line.SetQuantity(command.Quantity.Value);
            line.Suggested = false; // the desk has looked at it: confirmed
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task<InvoiceView?> RemoveLineAsync(Guid id, Guid lineId, InvoiceVersion command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EditDraftAsync(id, command.Version, "billing.invoice.line_remove", (_, lines) =>
        {
            var line = lines.SingleOrDefault(l => l.Id == lineId) ?? throw new BillingValidationException([("lineId", "unknown")]);
            db.InvoiceLines.Remove(line);
            lines.Remove(line);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task<InvoiceView?> SetDiscountAsync(Guid id, SetDiscount command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EditDraftAsync(id, command.Version, "billing.invoice.discount", (invoice, lines) =>
        {
            var amount = command.AmountMinor ?? -1;
            var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
            var subtotal = lines.Sum(l => l.AmountMinor);
            var errors = new List<(string, string)>();
            if (amount < 0 || amount > subtotal) errors.Add(("discountMinor", "range"));
            if (amount > 0 && reason is null) errors.Add(("reason", "required")); // a reason is always required, not a setting
            else if (reason?.Length > Invoice.MaxReason) errors.Add(("reason", "too_long"));
            BillingValidationException.ThrowIfAny(errors);
            (invoice.DiscountMinor, invoice.DiscountReason) = (amount, amount == 0 ? null : reason);
            return Task.CompletedTask; // the cap and the taxable-line rule are checked after every draft edit (EditDraftAsync)
        }, cancellationToken);
    }

    /// <summary>
    /// The discount rule: none on a bill with a taxable line, never more than the subtotal, and (when <paramref name="capped"/>) at most the settings'
    /// percentage unless the user has practice configuration.
    /// </summary>
    async Task CheckDiscountAsync(long discountMinor, List<InvoiceLine> lines, bool capped, CancellationToken cancellationToken)
    {
        if (discountMinor == 0) return;
        // shortcut: a discount is refused while any line is taxable, because the bill-level discount would not lower that line's GST value; the CA review
        // (MDV-P4-031) decides how a discount spreads over taxable lines, then this refusal goes.
        // SAFETY: mutation check: removed the taxable-line refusal -> BillingInvoiceTests.ADiscountIsRefusedWhileAnyLineIsTaxable failed (₹180 GST kept on a ₹100-off bill)
        if (lines.Any(l => l.TaxTreatment == TaxTreatment.Taxable)) throw new BillingValidationException([("discountMinor", "taxable_lines")]);
        var subtotal = lines.Sum(l => l.AmountMinor);
        if (discountMinor > subtotal) throw new BillingValidationException([("discountMinor", "range")]);
        var max = (await settings.GetAsync(cancellationToken)).MaxDiscountPercent;
        if (capped && discountMinor * 100 > subtotal * max && await staff.AccessAsync(Capability.StaffRolesIntegrations, cancellationToken) != Access.Config)
            throw new BillingValidationException([("discountMinor", "over_limit")]);
    }

    /// <summary>A draft edit: the bill locked, its version checked, the change applied and the totals recalculated, then audited and committed.</summary>
    async Task<InvoiceView?> EditDraftAsync(Guid id, uint? version, string action, Func<Invoice, List<InvoiceLine>, Task> change, CancellationToken cancellationToken)
    {
        if (version is null) throw new BillingValidationException([("version", "required")]);
        var rules = await RulesAsync(cancellationToken);
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await LockAsync(id, version, cancellationToken) is not { } invoice) return null;
            if (!InvoiceFlow.CanEdit(invoice.State)) throw new BillingValidationException([("state", "invalid")]);
            var lines = await db.InvoiceLines.Where(l => l.InvoiceId == id).ToListAsync(cancellationToken);
            await change(invoice, lines);
            // Any edit (a discount, or removing a line under one) leaves the discount within what this user may give.
            // SAFETY: mutation check: removed this check after draft edits -> BillingInvoiceTests.RemovingLinesCannotPushADiscountPastTheCap failed (the line went, leaving 100 % off)
            await CheckDiscountAsync(invoice.DiscountMinor, lines, capped: true, cancellationToken);
            invoice.Recalculate(lines, rules.RoundOff);
            Touch(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, action, invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    // ---- Issue and cancel ----

    public async Task<InvoiceView?> IssueAsync(Guid id, InvoiceVersion command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Version is null) throw new BillingValidationException([("version", "required")]);
        var rules = await RulesAsync(cancellationToken);
        var prefix = (await settings.GetAsync(cancellationToken)).InvoicePrefix;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await LockAsync(id, command.Version, cancellationToken) is not { } invoice) return null;
            if (!InvoiceFlow.CanIssue(invoice.State)) throw new BillingValidationException([("state", "invalid")]); // a second issue finds it issued: one number only
            var lines = await db.InvoiceLines.Where(l => l.InvoiceId == id).ToListAsync(cancellationToken);
            var errors = new List<(string, string)>();
            if (lines.Count == 0) errors.Add(("lines", "required"));
            if (lines.Any(l => l.Suggested)) errors.Add(("lines", "unconfirmed")); // the desk confirms what the visit suggested
            BillingValidationException.ThrowIfAny(errors);
            // Again at issue: a taxable line may have been added after the discount. The cap was enforced on the edit that set it (an
            // administrator's larger discount stays issuable by the desk).
            await CheckDiscountAsync(invoice.DiscountMinor, lines, capped: false, cancellationToken);
            invoice.Recalculate(lines, rules.RoundOff);
            var now = clock.GetUtcNow();
            // The legal number in this transaction: a rollback gives it back, so issued numbers have no gaps (MDV-P4-001).
            var number = await numbering.IssueAsync(rules.CountryCode, DocumentType.Invoice, prefix, now, cancellationToken);
            (invoice.Number, invoice.IssuedAt) = (number.Value, now);
            invoice.State = InvoiceFlow.AfterPayment(invoice.TotalMinor, 0); // a ₹0 bill is paid when issued
            Touch(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.invoice.issue", invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task<InvoiceView?> CancelAsync(Guid id, CancelInvoice command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        var errors = new List<(string, string)>();
        if (command.Version is null) errors.Add(("version", "required"));
        if (reason is null) errors.Add(("reason", "required"));
        else if (reason.Length > Invoice.MaxReason) errors.Add(("reason", "too_long"));
        BillingValidationException.ThrowIfAny(errors);
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await LockAsync(id, command.Version, cancellationToken) is not { } invoice) return null;
            if (!InvoiceFlow.CanCancel(invoice.State, invoice.PaidMinor)) throw new BillingValidationException([("state", "invalid")]);
            // A link the patient may still pay is closed first ("Cancel link"), so no money arrives for a cancelled bill.
            if (await db.Payments.AnyAsync(p => p.InvoiceId == id && p.State == PaymentState.Pending, cancellationToken)) throw new BillingValidationException([("payment", "pending")]);
            (invoice.State, invoice.CancelReason, invoice.CancelledAt) = (InvoiceState.Cancelled, reason, clock.GetUtcNow()); // keeps its number; never deleted
            Touch(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.invoice.cancel", invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    // ---- Payments ----

    public async Task<InvoiceView?> RecordPaymentAsync(Guid id, RecordPayment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var enabled = (await settings.GetAsync(cancellationToken)).PaymentMethods;
        var reference = string.IsNullOrWhiteSpace(command.Reference) ? null : command.Reference.Trim();
        var errors = new List<(string, string)>();
        if (command.Method is not { } method || !Enum.IsDefined(method)) errors.Add(("method", "required"));
        else if (method == PaymentMethod.RazorpayLink) errors.Add(("method", "invalid")); // a link is created, never recorded by hand
        else if (!enabled.Contains(method)) errors.Add(("method", "disabled"));
        if (command.AmountMinor is not > 0) errors.Add(("amountMinor", "range"));
        if (command.Method is PaymentMethod.Card or PaymentMethod.UpiDirect)
        {
            if (reference is null) errors.Add(("reference", "required"));
            else if (reference.Length > Payment.MaxReference || !ReferenceChars().IsMatch(reference)) errors.Add(("reference", "format"));
            // Medvya never stores a card number: 13 or more digits in a row (spaces and dashes ignored) is refused as one.
            else if (CardNumber().IsMatch(reference.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal))) errors.Add(("reference", "card_number"));
        }
        BillingValidationException.ThrowIfAny(errors);

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await LockAsync(id, null, cancellationToken) is not { } invoice) return null;
            if (!InvoiceFlow.CanPay(invoice.State)) throw new BillingValidationException([("state", "invalid")]);
            // SAFETY: mutation check: removed this over-collection check -> BillingInvoiceTests.TwoPaymentsAtOnceCannotOverCollect failed (CK_Invoices_Paid then threw on the second save)
            if (command.AmountMinor > await AvailableAsync(invoice, cancellationToken)) throw new BillingValidationException([("amountMinor", "over_collected")]);
            var now = clock.GetUtcNow();
            var payment = new Payment
            {
                Id = Guid.CreateVersion7(), TenantId = invoice.TenantId, InvoiceId = id, Method = command.Method!.Value, AmountMinor = command.AmountMinor!.Value,
                State = PaymentState.Succeeded, Reference = command.Method == PaymentMethod.Cash ? null : reference, RecordedBy = user.UserId, RecordedAt = now, ConfirmedAt = now,
                CashSessionId = await OpenCashSessionAsync(invoice.FacilityId, cancellationToken),
            };
            db.Payments.Add(payment);
            Collect(invoice, payment.AmountMinor);
            Touch(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.payment.record", invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>What may still be collected: the balance less any link the patient may still pay.</summary>
    async Task<long> AvailableAsync(Invoice invoice, CancellationToken cancellationToken) =>
        invoice.TotalMinor - invoice.PaidMinor - await db.Payments.Where(p => p.InvoiceId == invoice.Id && p.State == PaymentState.Pending).SumAsync(p => p.AmountMinor, cancellationToken);

    static void Collect(Invoice invoice, long amountMinor)
    {
        invoice.PaidMinor += amountMinor;
        invoice.State = InvoiceFlow.AfterPayment(invoice.TotalMinor, invoice.PaidMinor);
    }

    public async Task<PaymentLinkView?> CreatePaymentLinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var s = await settings.GetAsync(cancellationToken);
        if (!s.PaymentMethods.Contains(PaymentMethod.RazorpayLink)) throw new BillingValidationException([("method", "disabled")]);

        // 1. The pending payment, committed and audited before the provider is called (no network call inside a transaction).
        Payment payment;
        Invoice invoice;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await LockAsync(id, null, cancellationToken) is not { } locked) return null;
            invoice = locked;
            if (!InvoiceFlow.CanPay(invoice.State)) throw new BillingValidationException([("state", "invalid")]);
            if (await db.Payments.AnyAsync(p => p.InvoiceId == id && p.State == PaymentState.Pending, cancellationToken)) throw new BillingValidationException([("payment", "pending")]);
            var balance = await AvailableAsync(invoice, cancellationToken);
            if (balance < MinLinkMinor) throw new BillingValidationException([("amountMinor", "too_small")]);
            payment = new Payment
            {
                Id = Guid.CreateVersion7(), TenantId = invoice.TenantId, InvoiceId = id, Method = PaymentMethod.RazorpayLink, AmountMinor = balance,
                State = PaymentState.Pending, RecordedBy = user.UserId, RecordedAt = clock.GetUtcNow(),
            };
            db.Payments.Add(payment);
            Touch(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.payment.link_create", invoice, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // 2. The provider call. The description and notes carry the bill number, the clinic's name and opaque ids only: never clinical data.
        var clinic = (await facilities.FindAsync(invoice.FacilityId, cancellationToken))?.TenantName ?? "";
        PaymentLink link;
        try
        {
            link = await links.CreateAsync(TenantId, new PaymentLinkRequest(
                new Money(payment.AmountMinor, Currency.From(invoice.Currency)), payment.Id.ToString("N"), $"Bill {invoice.Number}, {clinic}",
                clock.GetUtcNow().AddMinutes(Math.Max(s.PaymentLinkExpiryMinutes, BillingSettings.MinLinkExpiry)) + LinkExpiryMargin,
                new Dictionary<string, string> { ["tenant"] = TenantId.ToString(), ["invoice"] = id.ToString(), ["payment"] = payment.Id.ToString() }), cancellationToken);
        }
        catch (PaymentProviderException)
        {
            await SettleAsync(payment.Id, PaymentState.Failed, null, null, CancellationToken.None); // "Payment failed": Retry or Record cash
            throw;
        }

        // 3. The link on the payment, under the bill's lock (as SettleAsync), and not cancelled midway: the link exists now. If another desk failed the
        // payment meanwhile ("Check status" after the grace period), the link is closed and never shown, so no money can arrive for a failed payment.
        // If this step never runs, "Check status" fails the pending payment once the grace period is over.
        var attached = false;
        await using (var tx = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM billing."Invoices" WHERE "Id" = {id} FOR UPDATE""", CancellationToken.None);
            var tracked = await db.Payments.SingleAsync(p => p.Id == payment.Id, CancellationToken.None);
            // SAFETY: mutation check: removed this state check -> BillingInvoiceTests.ALinkIsNeverAttachedToAPaymentFailedMeanwhile failed (the URL went to a failed payment)
            if (tracked.State == PaymentState.Pending)
            {
                (tracked.ProviderLinkId, tracked.LinkUrl) = (link.Id, link.ShortUrl);
                await db.SaveChangesAsync(CancellationToken.None);
                await AuditAsync(tx, "billing.payment.link_ready", invoice, CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
                attached = true;
            }
        }
        if (!attached)
        {
            try { await links.CancelAsync(TenantId, link.Id, CancellationToken.None); }
            catch (PaymentProviderException) { } // best effort: unattached, the link reaches nobody (its URL is never shown)
            throw new BillingValidationException([("payment", "failed")]);
        }
        return new PaymentLinkView((await GetAsync(id, cancellationToken))!, payment.Id, link.ShortUrl);
    }

    public async Task<InvoiceView?> CheckPaymentAsync(Guid id, Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (await PendingLinkAsync(id, paymentId, cancellationToken) is not { } found) return null;
        var after = found.LinkId is { } linkId ? await ConfirmPaymentLinkAsync(TenantId, linkId, cancellationToken)
            : found.State == PaymentState.Pending && !StillCreating(found) ? await SettleAsync(paymentId, PaymentState.Failed, null, null, cancellationToken) // the link was never made
            : found.State;
        var view = await GetAsync(id, cancellationToken);
        // A check that changed nothing is still a look at the bill: one audit row of its own (a change wrote its own).
        if (after == PaymentState.Pending || after == found.State && found.State != PaymentState.Pending)
            await audit.WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "billing.payment.check", ResourceType = Resource, ResourceId = id,
                PatientId = view?.Patient.Id, Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = FacilityId,
            }, cancellationToken);
        return view;
    }

    public async Task<InvoiceView?> CancelPaymentLinkAsync(Guid id, Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (await PendingLinkAsync(id, paymentId, cancellationToken) is not { } found) return null;
        if (found.State != PaymentState.Pending) throw new BillingValidationException([("state", "invalid")]);
        if (found.LinkId is null && StillCreating(found)) throw new BillingValidationException([("payment", "creating")]);
        if (found.LinkId is { } linkId)
        {
            await links.CancelAsync(TenantId, linkId, cancellationToken);
            await ConfirmPaymentLinkAsync(TenantId, linkId, cancellationToken); // cancelled, or paid just before: the provider says which
        }
        else await SettleAsync(paymentId, PaymentState.Failed, null, null, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>A pending payment with no link yet, younger than the grace period: its link is probably being made right now (CreatePaymentLinkAsync step 2).</summary>
    bool StillCreating((PaymentState State, string? LinkId, DateTimeOffset RecordedAt) found) =>
        found.State == PaymentState.Pending && found.LinkId is null && found.RecordedAt > clock.GetUtcNow() - LinkCreationGrace;

    async Task<(PaymentState State, string? LinkId, DateTimeOffset RecordedAt)?> PendingLinkAsync(Guid id, Guid paymentId, CancellationToken cancellationToken)
    {
        var facilityId = FacilityId;
        var row = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId && p.InvoiceId == id && p.Method == PaymentMethod.RazorpayLink && db.Invoices.Any(i => i.Id == id && i.FacilityId == facilityId))
            .Select(p => new { p.State, p.ProviderLinkId, p.RecordedAt }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.State, row.ProviderLinkId, row.RecordedAt);
    }

    public async Task<PaymentState?> ConfirmPaymentLinkAsync(Guid tenantId, string linkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkId);
        if (tenantId != TenantId) throw new InvalidOperationException("The link's tenant is not the current tenant."); // keys and rows must be the same clinic's
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.ProviderLinkId == linkId, cancellationToken);
        if (payment is null) return null;
        if (!InvoiceFlow.CanSettle(payment.State)) return payment.State;

        // The provider's view, fetched outside any transaction; nothing a webhook said is trusted.
        var state = await links.FetchAsync(tenantId, linkId, cancellationToken);
        var currency = (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == payment.InvoiceId, cancellationToken)).Currency;
        // SAFETY: mutation check: accepted Paid without comparing the amount paid -> BillingInvoiceTests.ALinkIsPaidOnlyWhenTheProviderShowsTheFullAmountPaid failed
        PaymentState? to = state.Status switch
        {
            PaymentLinkStatus.Paid when state.AmountPaid.Minor == payment.AmountMinor && state.AmountPaid.Currency.Code == currency => PaymentState.Succeeded,
            PaymentLinkStatus.Expired or PaymentLinkStatus.Cancelled => PaymentState.Expired,
            _ => null,
        };
        if (state.Status == PaymentLinkStatus.Paid && to is null) LogAmountMismatch(log, payment.Id); // shortcut: left Pending for a person to reconcile; refunds come with MDV-P4-006
        if (to is null) return PaymentState.Pending;
        return await SettleAsync(payment.Id, to.Value, state.CapturedPaymentIds is [.., var last] ? last : null, payment.InvoiceId, cancellationToken);
    }

    /// <summary>Moves a pending payment to its final state under the bill's lock (a succeeded one counts towards the bill), audited.</summary>
    async Task<PaymentState> SettleAsync(Guid paymentId, PaymentState to, string? providerPaymentId, Guid? invoiceId, CancellationToken cancellationToken)
    {
        invoiceId ??= await db.Payments.AsNoTracking().Where(p => p.Id == paymentId).Select(p => p.InvoiceId).SingleAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM billing."Invoices" WHERE "Id" = {invoiceId.Value} FOR UPDATE""", cancellationToken);
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, cancellationToken);
        var payment = await db.Payments.SingleAsync(p => p.Id == paymentId, cancellationToken);
        if (!InvoiceFlow.CanSettle(payment.State)) return payment.State; // settled meanwhile (a webhook and "Check status" at once): once only
        (payment.State, payment.ConfirmedAt) = (to, clock.GetUtcNow());
        if (to == PaymentState.Succeeded)
        {
            payment.ProviderPaymentId = providerPaymentId;
            payment.CashSessionId = await OpenCashSessionAsync(invoice.FacilityId, cancellationToken);
            Collect(invoice, payment.AmountMinor);
        }
        invoice.ChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, $"billing.payment.{to.ToString().ToLowerInvariant()}", invoice, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return to;
    }

    // ---- Shared ----

    /// <summary>
    /// Locks this facility's bill for the rest of the transaction (row-level security applies) and loads it; null when there is none. With a version, a
    /// bill changed since the caller read it is refused (409).
    /// </summary>
    async Task<Invoice?> LockAsync(Guid id, uint? version, CancellationToken cancellationToken)
    {
        var facilityId = FacilityId;
        db.ChangeTracker.Clear(); // what an earlier transaction of this request loaded may be stale now; read it again under the lock
        // SAFETY: mutation check: dropped the FacilityId condition here and in the query below -> BillingInvoiceTests.AnotherLocationOfTheSameClinicCannotReachTheBill failed (the other location reached the bill)
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM billing."Invoices" WHERE "Id" = {id} AND "FacilityId" = {facilityId} FOR UPDATE""", cancellationToken);
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id && i.FacilityId == facilityId, cancellationToken);
        if (invoice is not null && version is not null && db.Entry(invoice).Property<uint>("xmin").CurrentValue != version) throw new DbUpdateConcurrencyException();
        return invoice;
    }

    /// <summary>
    /// The location's Open cash session for a payment that succeeds now (MDV-P4-012), share-locked until this transaction commits: a count locks the
    /// session for update, so it waits for this payment and sees it, or this payment waits for the count and finds no Open session. Null when none is open.
    /// </summary>
    // SAFETY: mutation check: dropped FOR SHARE here -> BillingInvoiceTests.APaymentCommittingDuringTheCountLandsInExactlyOneSession failed (the count missed the payment stamped with its session)
    async Task<Guid?> OpenCashSessionAsync(Guid facilityId, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM billing."CashSessions" WHERE "FacilityId" = {facilityId} AND "State" = 'Open' FOR SHARE""")
            .ToListAsync(cancellationToken)).Select(id => (Guid?)id).SingleOrDefault();

    void Touch(Invoice invoice) => (invoice.ChangedBy, invoice.ChangedAt) = (user.UserId, clock.GetUtcNow());

    /// <summary>The audit row, written last before the commit (AGENTS.md 3.1).</summary>
    Task AuditAsync(IDbContextTransaction tx, string action, Invoice invoice, CancellationToken cancellationToken) => audit.WriteAsync(new AuditEvent
    {
        Id = Guid.CreateVersion7(), TenantId = invoice.TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = invoice.Id,
        PatientId = invoice.PatientId, Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = invoice.FacilityId,
    }, tx.GetDbTransaction(), cancellationToken);

    async Task<BillingCountryRules> RulesAsync(CancellationToken cancellationToken) =>
        BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));

    async Task<TimeZoneInfo> ZoneAsync(CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(FacilityId, cancellationToken))?.TimeZone ?? throw new InvalidOperationException("No facility context."));

    static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment link {PaymentId} is paid with an amount other than the payment's; left pending for reconciliation.")]
    static partial void LogAmountMismatch(ILogger logger, Guid paymentId);

    [GeneratedRegex("^[A-Za-z0-9 /-]+$")] private static partial Regex ReferenceChars();
    [GeneratedRegex("[0-9]{13,}")] private static partial Regex CardNumber();
}
