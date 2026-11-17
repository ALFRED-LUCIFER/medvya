using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-004 on real PostgreSQL: bills priced from the catalogue, the draft → issued → partly paid → paid lifecycle, discounts, India round-off, cancel,
/// split payments with the over-collection guard, Razorpay links settled only on the provider's word (a fake provider), tenant isolation in both layers,
/// and the endpoints' capability, flag and payload rules.
/// </summary>
public sealed class BillingInvoiceTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly FakeLinks links = new();
    readonly List<WebApplicationFactory<Program>> hosts = [];

    public Task InitializeAsync() => h.SeedAsync();

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        foreach (var host in hosts) await host.DisposeAsync();
    }

    IInvoices Desk(Guid? user = null) => h.Get<IInvoices>(user ?? h.Reception, s => s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links)));
    ICatalogue Catalogue() => h.Get<ICatalogue>(h.Admin);

    async Task<ServiceView> PricedAsync(string name, long amount, TaxTreatment tax = TaxTreatment.Exempt, int? rate = null, ConsultationKind? kind = null)
    {
        var service = await Catalogue().CreateAsync(new SaveService
        {
            Name = name, Category = kind is null ? ServiceCategory.Procedure : ServiceCategory.Consultation, TaxTreatment = tax, GstRateBasisPoints = rate, ConsultationKind = kind, Sac = "999312",
        });
        await Catalogue().AddPriceAsync(service.Id, new AddPrice { AmountMinor = amount });
        return service;
    }

    async Task<InvoiceView> DraftAsync(long amount, int quantity = 1)
    {
        var service = await PricedAsync($"Dressing {Guid.NewGuid():N} (synthetic)", amount);
        var draft = (await Desk().CreateAsync(new NewInvoice { PatientId = h.Patient }))!;
        return (await Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = service.Id, Quantity = quantity, Version = draft.Version }))!;
    }

    async Task<InvoiceView> IssuedAsync(long amount)
    {
        var draft = await DraftAsync(amount);
        return (await Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = draft.Version }))!;
    }

    Task<BillingSettingsView> MethodsAsync(params PaymentMethod[] methods) => h.Get<IBillingSettings>(h.Admin).SaveAsync(new SaveBillingSettings
    {
        InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = methods, DiscountReasons = ["Senior citizen"], MaxDiscountPercent = 10,
        PaymentLinkExpiryMinutes = 30, Version = 0,
    });

    static async Task<string> Refused(Task task) => string.Join(",", (await Assert.ThrowsAsync<BillingValidationException>(() => task)).Errors.Select(e => $"{e.Field}:{e.Code}"));

    async Task<Guid> DoctorMembershipAsync()
    {
        await using var db = sql.Context(h.Tenant);
        return await db.Memberships.Where(m => m.UserId == h.Doctor && m.FacilityId == h.Facility).Select(m => m.Id).SingleAsync();
    }

    /// <summary>The visit at the board's Billing step.</summary>
    async Task<AppointmentView> VisitAtBillingAsync(int hour, VisitType? type = null)
    {
        var visit = await h.BookAsync(hour);
        if (type is { } t)
        {
            await using var db = sql.Scheduling(h.Tenant);
            await db.Appointments.Where(a => a.Id == visit.Id).ExecuteUpdateAsync(a => a.SetProperty(x => x.VisitType, t));
        }
        var desk = h.FrontDesk(h.Reception);
        await desk.CheckInAsync(visit.Id, new CheckIn());
        foreach (var state in new[] { AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing })
            await desk.MoveAsync(visit.Id, new MoveVisit { State = state });
        return visit;
    }

    // ---- Lines, suggestions and price versions ----

    /// <summary>
    /// The board's Billing step opens the visit's draft with the visit's consultation at the doctor's own price, marked suggested; the desk confirms it; the issued
    /// bill keeps the price version it was priced from when the doctor's fee changes later (the MDV-P4-002 acceptance test).
    /// </summary>
    [Fact]
    public async Task TheVisitsDraftSuggestsItsConsultationAtTheDoctorsPriceAndTheIssuedBillKeepsThatVersion()
    {
        await h.DoctorTemplateAsync();
        await h.StepsAsync(nurse: false, billing: true);
        var followUp = await PricedAsync("Consultation (follow-up, synthetic)", 30_000, kind: ConsultationKind.FollowUp);
        var newVisit = await PricedAsync("Consultation (new visit, synthetic)", 50_000, kind: ConsultationKind.New);
        var doctor = await DoctorMembershipAsync();
        await Catalogue().AddPriceAsync(followUp.Id, new AddPrice { DoctorId = doctor, AmountMinor = 40_000 }); // the doctor's own fee beats the clinic default

        Assert.Equal("visit:not_attended", await Refused(Desk().ForVisitAsync((await h.BookAsync(11)).Id))); // booked, not arrived: nothing to bill yet
        var visit = await VisitAtBillingAsync(9);
        var draft = (await Desk().ForVisitAsync(visit.Id))!;
        var line = Assert.Single(draft.Lines);
        Assert.Equal((followUp.Id, 40_000L, true, "Consultation (follow-up, synthetic)", "999312"), (line.ServiceId, line.UnitMinor, line.Suggested, line.Description, line.Sac));
        Assert.Equal((InvoiceState.Draft, (Guid?)visit.Id, (Guid?)doctor), (draft.State, draft.AppointmentId, draft.DoctorId));
        Assert.Equal(draft.Id, (await Desk().ForVisitAsync(visit.Id))!.Id); // opened again: the same draft, not a second one

        Assert.Equal("lines:unconfirmed", await Refused(Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = draft.Version })));
        var confirmed = (await Desk().UpdateLineAsync(draft.Id, line.Id, new UpdateLine { Quantity = 1, Version = draft.Version }))!;
        var issued = (await Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = confirmed.Version }))!;
        Assert.Equal((InvoiceState.Issued, "INV/26-27/00001", 40_000L), (issued.State, issued.Number, issued.TotalMinor));

        await Catalogue().AddPriceAsync(followUp.Id, new AddPrice { DoctorId = doctor, AmountMinor = 45_000 });
        var later = (await Desk().GetAsync(draft.Id))!;
        Assert.Equal((line.PriceVersionId, 40_000L, 40_000L), (later.Lines[0].PriceVersionId, later.Lines[0].UnitMinor, later.TotalMinor));

        // A new-consultation visit suggests the new-visit service (the doctor has no own price for it: the clinic default).
        var second = await VisitAtBillingAsync(10, VisitType.NewConsultation);
        var other = Assert.Single((await Desk().ForVisitAsync(second.Id))!.Lines);
        Assert.Equal((newVisit.Id, 50_000L), (other.ServiceId, other.UnitMinor));
    }

    [Fact]
    public async Task ADraftIsEditableAndAnIssuedBillIsNot()
    {
        var ecg = await PricedAsync("ECG (synthetic)", 25_000);
        var draft = await DraftAsync(10_000, quantity: 2);
        Assert.Equal((20_000L, 20_000L), (draft.SubtotalMinor, draft.TotalMinor));
        Assert.Equal("quantity:range", await Refused(Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = ecg.Id, Quantity = 0, Version = draft.Version })));
        var unpriced = await Catalogue().CreateAsync(new SaveService { Name = "Unpriced (synthetic)", Category = ServiceCategory.Other });
        Assert.Equal("serviceId:no_price", await Refused(Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = unpriced.Id, Quantity = 1, Version = draft.Version })));

        var added = (await Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = ecg.Id, Quantity = 1, Version = draft.Version }))!;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = ecg.Id, Quantity = 1, Version = draft.Version })); // stale
        var three = (await Desk().UpdateLineAsync(draft.Id, added.Lines[1].Id, new UpdateLine { Quantity = 3, Version = added.Version }))!;
        Assert.Equal(95_000, three.SubtotalMinor);
        var removed = (await Desk().RemoveLineAsync(draft.Id, three.Lines[0].Id, new InvoiceVersion { Version = three.Version }))!;
        Assert.Equal(75_000, Assert.Single(removed.Lines).AmountMinor);
        var discounted = (await Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 5_000, Reason = "Senior citizen", Version = removed.Version }))!;
        Assert.Equal((5_000L, 70_000L), (discounted.DiscountMinor, discounted.TotalMinor));

        var issued = (await Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = discounted.Version }))!;
        foreach (var change in new Func<Task>[]
        {
            () => Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = ecg.Id, Quantity = 1, Version = issued.Version }),
            () => Desk().UpdateLineAsync(draft.Id, issued.Lines[0].Id, new UpdateLine { Quantity = 1, Version = issued.Version }),
            () => Desk().RemoveLineAsync(draft.Id, issued.Lines[0].Id, new InvoiceVersion { Version = issued.Version }),
            () => Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 0, Version = issued.Version }),
            () => Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = issued.Version }),
        })
            Assert.Equal("state:invalid", await Refused(change()));
        Assert.Equal((70_000L, 1), ((await Desk().GetAsync(draft.Id))!.TotalMinor, (await Desk().GetAsync(draft.Id))!.Lines.Count));
    }

    [Fact]
    public async Task ADiscountNeedsAReasonAndAboveTheCapPracticeConfiguration()
    {
        await MethodsAsync(PaymentMethod.Cash); // MaxDiscountPercent 10
        var draft = await DraftAsync(100_000);
        Assert.Equal("reason:required", await Refused(Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 5_000, Reason = " ", Version = draft.Version })));
        Assert.Equal("discountMinor:range", await Refused(Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 100_001, Reason = "Staff family", Version = draft.Version })));
        Assert.Equal("discountMinor:over_limit", await Refused(Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 10_001, Reason = "Staff family", Version = draft.Version })));
        var desk = (await Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 10_000, Reason = "Senior citizen", Version = draft.Version }))!;
        Assert.Equal(("Senior citizen", 90_000L), (desk.DiscountReason, desk.TotalMinor));
        Assert.False(desk.Options.MayExceedMaxDiscount);
        var admin = (await Desk(h.Admin).SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 50_000, Reason = "Hardship (owner approved)", Version = desk.Version }))!;
        Assert.Equal(50_000, admin.TotalMinor);
        Assert.True(admin.Options.MayExceedMaxDiscount);
        var cleared = (await Desk().SetDiscountAsync(draft.Id, new SetDiscount { AmountMinor = 0, Version = admin.Version }))!;
        Assert.Equal((0L, (string?)null), (cleared.DiscountMinor, cleared.DiscountReason));
    }

    // ---- Totals ----

    [Fact]
    public void IndiaRoundsTheBillToTheRupeeAndTheUaeDoesNot()
    {
        Assert.Equal(50, BillingCountryRules.India.RoundOff(123_450)); // ₹1,234.50 → +0.50
        Assert.Equal(-49, BillingCountryRules.India.RoundOff(123_449)); // ₹1,234.49 → −0.49
        Assert.Equal(0, BillingCountryRules.India.RoundOff(123_400));
        Assert.Equal(0, BillingCountryRules.Uae.RoundOff(123_449));
    }

    /// <summary>Lines and tax exact to the paisa; GST per taxable line, half up; the round-off line always present, even when 0.</summary>
    [Fact]
    public async Task TotalsAreExactInPaiseWithASeparateRoundOff()
    {
        var exact = await DraftAsync(123_450);
        Assert.Equal((123_450L, 0L, 50L, 123_500L), (exact.SubtotalMinor, exact.TaxMinor, exact.RoundOffMinor, exact.TotalMinor));

        var taxable = await PricedAsync("Cosmetic peel (synthetic)", 10_003, TaxTreatment.Taxable, 1800);
        var mixed = (await Desk().AddLineAsync(exact.Id, new AddLine { ServiceId = taxable.Id, Quantity = 1, Version = exact.Version }))!;
        var line = mixed.Lines.Single(l => l.ServiceId == taxable.Id);
        Assert.Equal((1_801L, TaxTreatment.Taxable, (int?)1800), (line.TaxMinor, line.TaxTreatment, line.GstRateBasisPoints)); // 100.03 × 18 % = 18.0054 → 18.01
        Assert.Equal((133_453L, 1_801L, 46L, 135_300L), (mixed.SubtotalMinor, mixed.TaxMinor, mixed.RoundOffMinor, mixed.TotalMinor)); // ₹1,352.54 payable → +0.46

        var whole = await DraftAsync(50_000);
        Assert.Equal((0L, 50_000L), (whole.RoundOffMinor, whole.TotalMinor));
    }

    // ---- Issue and cancel ----

    /// <summary>Two desks issue the same draft at once: one legal number is taken, the series has no gap.</summary>
    [Fact]
    public async Task IssuingTwiceAtOnceTakesOneNumber()
    {
        var draft = await DraftAsync(50_000);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return (await Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = draft.Version }))!.Number; }
            catch (Exception e) when (e is BillingValidationException or DbUpdateConcurrencyException) { return null; }
        }));
        Assert.Equal("INV/26-27/00001", Assert.Single(outcomes, n => n is not null));
        Assert.Equal("INV/26-27/00002", (await IssuedAsync(10_000)).Number);
    }

    [Fact]
    public async Task ADraftOrAnUnpaidIssuedBillIsCancelledWithAReasonAndKeepsItsNumber()
    {
        var empty = (await Desk().CreateAsync(new NewInvoice { PatientId = h.Patient }))!;
        Assert.Equal("lines:required", await Refused(Desk().IssueAsync(empty.Id, new InvoiceVersion { Version = empty.Version })));
        var draft = await DraftAsync(10_000);
        Assert.Equal("reason:required", await Refused(Desk().CancelAsync(draft.Id, new CancelInvoice { Reason = "", Version = draft.Version })));
        Assert.Equal(InvoiceState.Cancelled, (await Desk().CancelAsync(draft.Id, new CancelInvoice { Reason = "Opened by mistake", Version = draft.Version }))!.State);

        var issued = await IssuedAsync(10_000);
        var cancelled = (await Desk().CancelAsync(issued.Id, new CancelInvoice { Reason = "Patient left before paying", Version = issued.Version }))!;
        Assert.Equal((InvoiceState.Cancelled, issued.Number, "Patient left before paying"), (cancelled.State, cancelled.Number, cancelled.CancelReason));

        var part = await IssuedAsync(10_000);
        var paid = (await Desk().RecordPaymentAsync(part.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 1_000 }))!;
        Assert.Equal("state:invalid", await Refused(Desk().CancelAsync(part.Id, new CancelInvoice { Reason = "Too late", Version = paid.Version })));

        // Never deleted: the application's database role has no DELETE right on bills or payments (roles.sql).
        foreach (var table in new[] { "Invoices", "Payments" })
        {
            await using var conn = await sql.OpenAs("app_user", h.Tenant);
            await using var cmd = new NpgsqlCommand($"DELETE FROM billing.\"{table}\"", conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
        }
    }

    // ---- Split payments ----

    [Fact]
    public async Task ABillIsPaidInPartsByCashCardAndUpiAndNeverOverCollected()
    {
        var bill = await IssuedAsync(50_000);
        Assert.Equal(50_000, bill.BalanceMinor);
        var cash = (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 20_000, Reference = "ignored" }))!;
        Assert.Equal((InvoiceState.PartlyPaid, 20_000L, 30_000L, (string?)null), (cash.State, cash.PaidMinor, cash.BalanceMinor, cash.Payments[0].Reference));

        Assert.Equal("reference:card_number", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 10_000, Reference = "4111 1111 1111 1111" })));
        Assert.Equal("reference:required", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 10_000 })));
        var card = (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 10_000, Reference = "TID 0042 APPR 7781" }))!;
        Assert.Equal((InvoiceState.PartlyPaid, 20_000L), (card.State, card.BalanceMinor));

        Assert.Equal("amountMinor:over_collected", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.UpiDirect, AmountMinor = 20_001, Reference = "630912345678" })));
        Assert.Equal("method:invalid", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.RazorpayLink, AmountMinor = 1 })));
        var upi = (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.UpiDirect, AmountMinor = 20_000, Reference = "630912345678" }))!;
        Assert.Equal((InvoiceState.Paid, 50_000L, 0L, 3), (upi.State, upi.PaidMinor, upi.BalanceMinor, upi.Payments.Count));
        Assert.Equal("state:invalid", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 1 })));

        Assert.Equal("state:invalid", await Refused(Desk().RecordPaymentAsync((await DraftAsync(1_000)).Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 1 }))); // a draft takes no money
        await MethodsAsync(PaymentMethod.Cash);
        Assert.Equal("method:disabled", await Refused(Desk().RecordPaymentAsync((await IssuedAsync(1_000)).Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 1, Reference = "TID 1" })));
    }

    /// <summary>Two desks take cash for the same bill at the same moment: the bill's lock lets one through and refuses the other.</summary>
    // SAFETY: mutation check: removed the over-collection check in Invoices.RecordPaymentAsync -> this test failed (CK_Invoices_Paid threw on the second save instead of a refusal)
    [Fact]
    public async Task TwoPaymentsAtOnceCannotOverCollect()
    {
        var bill = await IssuedAsync(50_000);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 30_000 }); return true; }
            catch (BillingValidationException) { return false; }
        }));
        Assert.Single(outcomes, ok => ok);
        var after = (await Desk().GetAsync(bill.Id))!;
        Assert.Equal((30_000L, 1), (after.PaidMinor, after.Payments.Count));
    }

    // ---- Razorpay links ----

    /// <summary>
    /// A link for the balance: a pending payment with no clinical data sent to the provider; the bill is paid only when the provider shows the link paid
    /// with exactly the payment's amount, and only once.
    /// </summary>
    // SAFETY: mutation check: accepted any Paid status without comparing the amount -> this test failed (a link paid 1.00 settled the 400.00 payment)
    [Fact]
    public async Task ALinkIsPaidOnlyWhenTheProviderShowsTheFullAmountPaid()
    {
        var bill = await IssuedAsync(50_000);
        Assert.Equal("method:disabled", await Refused(Desk().CreatePaymentLinkAsync(bill.Id))); // off until the clinic turns it on
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var part = (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 10_000 }))!;

        var created = (await Desk().CreatePaymentLinkAsync(bill.Id))!;
        Assert.Equal("https://rzp.io/i/synthetic", created.ShortUrl);
        var request = links.Created!;
        Assert.Equal((40_000L, "INR"), (request.Amount.Minor, request.Amount.Currency.Code));
        Assert.Equal($"Bill {bill.Number}, Iyer Family Clinic (synthetic)", request.Description);
        Assert.Equal(["invoice", "payment", "tenant"], request.Notes.Keys.Order());
        Assert.True(request.ExpiresAt >= h.Clock.GetUtcNow().AddMinutes(30));
        Assert.True(request.ReferenceId.Length <= 40);
        var pending = created.Invoice.Payments.Single(p => p.Method == PaymentMethod.RazorpayLink);
        Assert.Equal((PaymentState.Pending, 40_000L, "https://rzp.io/i/synthetic"), (pending.State, pending.AmountMinor, pending.LinkUrl));
        Assert.Equal("payment:pending", await Refused(Desk().CreatePaymentLinkAsync(bill.Id))); // one pending link at a time
        Assert.Equal("amountMinor:over_collected", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 1 }))); // the link may still be paid

        // Created, or paid with another amount: still pending.
        links.State = new PaymentLinkState("plink_synthetic", PaymentLinkStatus.Created, Money.Zero(Currency.Inr), []);
        Assert.Equal(PaymentState.Pending, (await Desk().CheckPaymentAsync(bill.Id, pending.Id))!.Payments.Single(p => p.Id == pending.Id).State);
        links.State = new PaymentLinkState("plink_synthetic", PaymentLinkStatus.Paid, new Money(100, Currency.Inr), ["pay_synthetic"]);
        Assert.Equal(PaymentState.Pending, await Desk().ConfirmPaymentLinkAsync(h.Tenant, "plink_synthetic"));

        links.State = new PaymentLinkState("plink_synthetic", PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]);
        var paid = (await Desk().CheckPaymentAsync(bill.Id, pending.Id))!;
        Assert.Equal((InvoiceState.Paid, 50_000L), (paid.State, paid.PaidMinor));
        Assert.Equal((PaymentState.Succeeded, (string?)null), (paid.Payments.Single(p => p.Id == pending.Id).State, paid.Payments.Single(p => p.Id == pending.Id).LinkUrl));
        Assert.Equal(PaymentState.Succeeded, await Desk().ConfirmPaymentLinkAsync(h.Tenant, "plink_synthetic")); // a webhook replay: once only
        Assert.Equal(50_000, (await Desk().GetAsync(bill.Id))!.PaidMinor);
        Assert.Null(await Desk().ConfirmPaymentLinkAsync(h.Tenant, "plink_unknown"));
        await using var billing = sql.Billing(h.Tenant);
        Assert.Equal("pay_synthetic", (await billing.Payments.SingleAsync(p => p.Id == pending.Id)).ProviderPaymentId);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(["billing.payment.link_create", "billing.payment.link_ready", "billing.payment.succeeded"],
            await audit.Events.Where(e => e.ResourceId == bill.Id && e.Action.StartsWith("billing.payment.") && e.Action != "billing.payment.record" && e.Action != "billing.payment.check")
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => e.Action).ToListAsync());
    }

    [Fact]
    public async Task AnExpiredOrCancelledLinkLeavesTheBalanceForAnotherPayment()
    {
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var bill = await IssuedAsync(20_000);
        var first = (await Desk().CreatePaymentLinkAsync(bill.Id))!;
        links.State = new PaymentLinkState("plink_synthetic", PaymentLinkStatus.Expired, Money.Zero(Currency.Inr), []);
        var expired = (await Desk().CheckPaymentAsync(bill.Id, first.PaymentId))!;
        Assert.Equal((PaymentState.Expired, InvoiceState.Issued, 20_000L), (expired.Payments.Single().State, expired.State, expired.BalanceMinor));

        links.State = null;
        var second = (await Desk().CreatePaymentLinkAsync(bill.Id))!;
        var cancelled = (await Desk().CancelPaymentLinkAsync(bill.Id, second.PaymentId))!;
        Assert.Equal(PaymentState.Expired, cancelled.Payments.Single(p => p.Id == second.PaymentId).State);
        Assert.Equal(InvoiceState.Paid, (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 20_000 }))!.State);
    }

    /// <summary>A clinic that has not connected Razorpay: a clear error, and the attempt shows as a failed payment the desk can retry or replace with cash.</summary>
    [Fact]
    public async Task ALinkForAClinicWithoutRazorpayFailsClearly()
    {
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var bill = await IssuedAsync(20_000);
        links.Fail = PaymentProviderError.NotConnected;
        Assert.Equal(PaymentProviderError.NotConnected, (await Assert.ThrowsAsync<PaymentProviderException>(() => Desk().CreatePaymentLinkAsync(bill.Id))).Error);
        var after = (await Desk().GetAsync(bill.Id))!;
        Assert.Equal((PaymentState.Failed, 20_000L), (after.Payments.Single().State, after.BalanceMinor));
        Assert.Equal(InvoiceState.Paid, (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 20_000 }))!.State);
    }

    // ---- Tenant isolation ----

    // SAFETY: mutation check: removed migrationBuilder.Tenant("billing", "Payments") -> this test failed (the IgnoreQueryFilters and raw-SQL reads saw tenant A's payment)
    [Fact]
    public async Task AnotherTenantSeesNoneOfTheBillsAndCannotWriteIntoThem()
    {
        var bill = await IssuedAsync(10_000);
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 5_000 });

        var other = Guid.NewGuid();
        await using var inOther = sql.Billing(other);
        Assert.Empty(await inOther.Invoices.ToListAsync()); // layer 1: the EF filter
        Assert.Empty(await inOther.Invoices.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        Assert.Empty(await inOther.InvoiceLines.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await inOther.Payments.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await inOther.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM billing."Payments" """).ToListAsync());
        inOther.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = h.Tenant, InvoiceId = bill.Id, Method = PaymentMethod.Cash, AmountMinor = 1, State = PaymentState.Succeeded, RecordedAt = DateTimeOffset.UtcNow });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => inOther.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(insert.InnerException).SqlState);

        var foreign = h.Get<IInvoices>(h.Reception, s =>
        {
            s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(other)));
            s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links));
        });
        Assert.Null(await foreign.GetAsync(bill.Id));
        Assert.Null(await foreign.RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 1 }));
        Assert.Empty(await foreign.SearchAsync(new InvoiceSearch()));
        Assert.Equal(5_000, (await Desk().GetAsync(bill.Id))!.PaidMinor);
    }

    // ---- Endpoints ----

    /// <summary>The receptionist bills; a nurse and a doctor without "Invoices and payments" are refused; the payloads carry no clinical text.</summary>
    [Fact]
    public async Task TheFrontDeskBillsAndRolesWithoutInvoicesAreRefused()
    {
        await h.DoctorTemplateAsync();
        await h.StepsAsync(nurse: false, billing: true);
        await PricedAsync("Consultation (follow-up, synthetic)", 30_000, kind: ConsultationKind.FollowUp);
        var visit = await VisitAtBillingAsync(9);

        using var desk = Client(h.Reception);
        var opened = await desk.PostAsync(new Uri($"/v1/billing/visits/{visit.Id}/invoice", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var body = await opened.Content.ReadAsStringAsync();
        var bill = JsonDocument.Parse(body).RootElement;
        var id = bill.GetProperty("id").GetGuid();
        var detail = await desk.GetStringAsync(new Uri($"/v1/billing/invoices/{id}", UriKind.Relative));
        var list = await desk.PostAsJsonAsync("/v1/billing/invoices/search", new { state = "Draft" });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        // Billing never carries the visit's reason, notes or diagnoses.
        foreach (var payload in new[] { body, detail, await list.Content.ReadAsStringAsync() })
        {
            Assert.DoesNotContain("chest tightness", payload, StringComparison.OrdinalIgnoreCase);
            foreach (var word in new[] { "diagnos", "reasonText", "\"note", "allerg", "icd" }) Assert.DoesNotContain(word, payload, StringComparison.OrdinalIgnoreCase);
        }
        var line = bill.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var version = bill.GetProperty("version").GetUInt32();
        var confirmed = await desk.PutAsJsonAsync($"/v1/billing/invoices/{id}/lines/{line}", new { quantity = 1, version });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        version = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await desk.PostAsJsonAsync($"/v1/billing/invoices/{id}/issue", new { version })).StatusCode);
        var stale = await desk.PostAsJsonAsync($"/v1/billing/invoices/{id}/cancel", new { reason = "Synthetic", version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var cardNumber = await desk.PostAsJsonAsync($"/v1/billing/invoices/{id}/payments", new { method = "Card", amountMinor = 100, reference = "4111111111111111" });
        Assert.Equal(HttpStatusCode.BadRequest, cardNumber.StatusCode);
        Assert.DoesNotContain("4111", await cardNumber.Content.ReadAsStringAsync(), StringComparison.Ordinal); // the refusal names the field, never the value
        Assert.Equal(HttpStatusCode.OK, (await desk.PostAsJsonAsync($"/v1/billing/invoices/{id}/payments", new { method = "Cash", amountMinor = 30_000 })).StatusCode);

        foreach (var role in new[] { Role.Nurse, Role.HospitalDoctor })
        {
            using var refused = Client(h.ByRole[role]);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.GetAsync(new Uri($"/v1/billing/invoices/{id}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PostAsJsonAsync($"/v1/billing/invoices/{id}/payments", new { method = "Cash", amountMinor = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PostAsJsonAsync("/v1/billing/invoices/search", new { })).StatusCode);
        }
        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains(await audit.Events.Where(e => e.Action == "billing.payment.record").Select(e => e.Outcome).ToListAsync(), o => o == Medvya.Audit.Domain.AuditOutcome.Denied);
        Assert.Contains(await audit.Events.Where(e => e.Action == "billing.invoice.issue" && e.ResourceId == id).Select(e => e.PatientId).ToListAsync(), p => p == h.Patient);
    }

    [Fact]
    public async Task AProviderThatIsNotConnectedIsAClearConflictAndBillingOffIs404()
    {
        await h.StepsAsync(nurse: false, billing: true);
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var bill = await IssuedAsync(10_000);
        links.Fail = PaymentProviderError.NotConnected;
        using var desk = Client(h.Reception);
        var link = await desk.PostAsync(new Uri($"/v1/billing/invoices/{bill.Id}/payment-link", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Equal("provider_not_connected", (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Billing, false, "practice", "Billing (synthetic test)");
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync(new Uri($"/v1/billing/invoices/{bill.Id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desk.PostAsJsonAsync($"/v1/billing/invoices/{bill.Id}/payments", new { method = "Cash", amountMinor = 1 })).StatusCode);
    }

    /// <summary>A provider double: what was asked for, and the link state it reports.</summary>
    sealed class FakeLinks : IPaymentLinks
    {
        public PaymentLinkRequest? Created { get; private set; }
        public PaymentLinkState? State { get; set; }
        public PaymentProviderError? Fail { get; set; }
        int made;

        public Task<PaymentLink> CreateAsync(Guid tenantId, PaymentLinkRequest request, CancellationToken ct)
        {
            if (Fail is { } error) throw new PaymentProviderException(error);
            Created = request;
            var id = made++ == 0 ? "plink_synthetic" : $"plink_synthetic{made}"; // a new link per attempt
            return Task.FromResult(new PaymentLink(id, "https://rzp.io/i/synthetic", PaymentLinkStatus.Created));
        }

        public Task<PaymentLinkState> FetchAsync(Guid tenantId, string linkId, CancellationToken ct) =>
            Task.FromResult(State ?? new PaymentLinkState(linkId, PaymentLinkStatus.Created, Money.Zero(Currency.Inr), []));

        public Task<PaymentLinkStatus> CancelAsync(Guid tenantId, string linkId, CancellationToken ct)
        {
            State = new PaymentLinkState(linkId, PaymentLinkStatus.Cancelled, Money.Zero(Currency.Inr), []);
            return Task.FromResult(PaymentLinkStatus.Cancelled);
        }
    }

    sealed class NoMail : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid user)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Features:InternalOrganisations:0", h.Tenant.ToString()) // billing is at the Internal release stage
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility)))
            .ConfigureTestServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail()));
                s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links));
            }));
        hosts.Add(host);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }
}
