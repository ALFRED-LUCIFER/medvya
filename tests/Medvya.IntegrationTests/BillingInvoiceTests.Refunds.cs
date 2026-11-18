using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-006 on real PostgreSQL: a refund on a paid bill issues a credit note from its own series against the bill, which itself never changes; cash
/// refunds come out of the drawer's expected cash; a card or UPI refund goes back the way it was paid; never more given back than was paid (the service
/// and the database); refunds above the clinic's threshold need practice configuration; credit notes are append-only and tenant-isolated in both layers.
/// </summary>
public sealed partial class BillingInvoiceTests
{
    /// <summary>A ₹500 bill paid ₹200 cash and ₹300 card, in the open drawer.</summary>
    async Task<InvoiceView> PaidSplitAsync()
    {
        var bill = await IssuedAsync(50_000);
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 20_000 });
        return (await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 30_000, Reference = "TID 0042" }))!;
    }

    /// <summary>
    /// A refund issues a credit note numbered from the credit note series against the bill; the bill's lines and totals stay as issued; it is Refunded once
    /// all it was paid is given back. Cash refunds come out of the drawer (expected cash), card refunds do not, and the count closes with no variance.
    /// </summary>
    [Fact]
    public async Task ARefundIssuesACreditNoteAndCashRefundsComeOutOfTheDrawer()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 100_000 });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var bill = await PaidSplitAsync();
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        var partly = (await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 10_000, Reason = "Dressing not done (synthetic)" }))!;
        Assert.Equal((InvoiceState.Paid, 10_000L, 50_000L, 0L), (partly.State, partly.RefundedMinor, partly.PaidMinor, partly.BalanceMinor));
        var note = Assert.Single(partly.CreditNotes);
        Assert.Matches(@"^CN/\d{2}-\d{2}/00001$", note.Number);
        Assert.Equal((10_000L, PaymentMethod.Cash, (string?)null, "Dressing not done (synthetic)"), (note.AmountMinor, note.Method, note.Reference, note.Reason));
        Assert.Equal((bill.Number, bill.TotalMinor, bill.Lines.Count), (partly.Number, partly.TotalMinor, partly.Lines.Count)); // the bill itself is unchanged

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Card, AmountMinor = 30_000, Reference = "TID 0042 RFND 1", Reason = "Synthetic" });
        var refunded = (await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 10_000, Reason = "Synthetic" }))!;
        Assert.Equal((InvoiceState.Refunded, 50_000L), (refunded.State, refunded.RefundedMinor));
        Assert.Equal(["CN", "CN", "CN"], refunded.CreditNotes.Select(n => n.Number[..2]));
        Assert.Equal(3, refunded.CreditNotes.Select(n => n.Number).Distinct().Count());
        Assert.Equal("state:invalid", await Refused(Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 100, Reason = "Synthetic" })));
        Assert.Equal(50_000L, Assert.Single(await Desk().SearchAsync(new InvoiceSearch { State = InvoiceState.Refunded })).RefundedMinor); // the bills list shows it

        // ₹1,000 float + ₹200 cash taken − ₹200 cash given back = ₹1,000 in the drawer; the ₹300 card refund went back on the terminal.
        var day = await Drawer().DayAsync();
        Assert.Equal(100_000L, day.Current!.ExpectedMinor);
        Assert.Equal([(PaymentMethod.Cash, 1, 20_000L, 2, 20_000L), (PaymentMethod.Card, 1, 30_000L, 1, 30_000L)],
            day.Current.Totals.Select(t => (t.Method, t.Payments, t.AmountMinor, t.Refunds, t.RefundsMinor)));
        var closed = (await Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(2), Version = day.Current.Version }))!;
        Assert.Equal((CashSessionState.Closed, 0L), (closed.State, closed.VarianceMinor!.Value));
        Assert.NotNull(await Drawer().ReportAsync(session.Id)); // the shift report lists the refunds

        await using var audit = sql.Audit(h.Tenant);
        var details = await audit.Events.Where(e => e.ResourceId == bill.Id && e.Action == "billing.refund" && e.PatientId == h.Patient).OrderBy(e => e.Id).Select(e => e.Detail).ToListAsync();
        Assert.Equal(3, details.Count);
        Assert.Equal($"creditNote:{note.Id};amountMinor:10000;method:Cash", details[0]); // which refund, never the reason
        Assert.DoesNotContain(details, d => d!.Contains("Dressing", StringComparison.Ordinal));
    }

    /// <summary>
    /// Never more given back than was paid, a card or UPI refund at most what was paid that way, only on a paid bill, with a reason and never a card number.
    /// The database refuses an over-refund even when the service's check is skipped.
    /// </summary>
    // SAFETY: mutation check: CK_Invoices_Refunded made "true" in the migration -> this test failed (the owner's UPDATE past the paid total succeeded)
    [Fact]
    public async Task ARefundIsAtMostWhatWasPaidAndACardRefundAtMostWhatTheCardPaid()
    {
        var bill = await PaidSplitAsync();
        RecordRefund Refund(PaymentMethod method, long amount, string? reference = null, string? reason = "Synthetic") =>
            new() { Method = method, AmountMinor = amount, Reference = reference, Reason = reason };

        Assert.Equal("amountMinor:over_refunded", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Cash, 50_100))));
        Assert.Equal("amountMinor:over_method", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Card, 30_100, "TID 1")))); // the card paid ₹300
        Assert.Equal("amountMinor:over_method", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.UpiDirect, 100, "UTR1")))); // nothing was paid by UPI
        Assert.Equal("reason:required", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Cash, 100, reason: " "))));
        Assert.Equal("reference:required", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Card, 100))));
        Assert.Equal("reference:card_number", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Card, 100, "4111 1111 1111 1111"))));
        Assert.Equal("method:invalid", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.RazorpayLink, 100))));
        Assert.Equal("amountMinor:range", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Cash, 0))));
        var unpaid = await IssuedAsync(10_000);
        Assert.Equal("state:invalid", await Refused(Desk().RefundAsync(unpaid.Id, Refund(PaymentMethod.Cash, 100))));
        Assert.Null(await Desk().RefundAsync(Guid.NewGuid(), Refund(PaymentMethod.Cash, 100)));

        // Cash may give back money paid by card (the patient takes cash); the card then has only what is left of the bill.
        await Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Cash, 40_000));
        Assert.Equal("amountMinor:over_refunded", await Refused(Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Card, 20_000, "TID 2"))));
        Assert.Equal(InvoiceState.Refunded, (await Desk().RefundAsync(bill.Id, Refund(PaymentMethod.Card, 10_000, "TID 3")))!.State);

        await using var owner = sql.BillingAsAdmin(h.Tenant);
        var e = await Assert.ThrowsAsync<PostgresException>(() => owner.Database.ExecuteSqlRawAsync(
            """UPDATE billing."Invoices" SET "RefundedMinor" = "PaidMinor" + 1 WHERE "Id" = @id""", new NpgsqlParameter("id", bill.Id)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, e.SqlState);
    }

    /// <summary>
    /// A refund above the clinic's threshold (₹1,000 by default) is refused to the desk and made by the practice admin; the threshold is a billing setting
    /// (0: every refund needs the admin), and a settings save that leaves it out keeps it.
    /// </summary>
    [Fact]
    public async Task ARefundAboveTheThresholdNeedsPracticeConfiguration()
    {
        var bill = await IssuedAsync(200_000);
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 200_000 });
        var big = new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 150_000, Reason = "Procedure cancelled (synthetic)" };

        Assert.Equal(100_000L, (await Desk().GetAsync(bill.Id))!.Options.RefundApprovalAboveMinor);
        Assert.Equal("amountMinor:approval_required", await Refused(Desk().RefundAsync(bill.Id, big)));
        Assert.Equal(10_000L, (await Desk().RefundAsync(bill.Id, big with { AmountMinor = 10_000 }))!.RefundedMinor); // below it: the desk alone
        // The threshold is on all the bill gives back: ₹100 then ₹950 is ₹1,050, past ₹1,000, so the second needs the admin too.
        Assert.Equal("amountMinor:approval_required", await Refused(Desk().RefundAsync(bill.Id, big with { AmountMinor = 95_000 })));
        Assert.Equal(160_000L, (await Desk(h.Admin).RefundAsync(bill.Id, big))!.RefundedMinor);

        var settings = h.Get<IBillingSettings>(h.Admin);
        var saved = await settings.SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash], MaxDiscountPercent = 10, PaymentLinkExpiryMinutes = 30,
            RefundApprovalAboveMinor = 0, Version = (await settings.GetAsync()).Version,
        });
        Assert.Equal(0L, saved.RefundApprovalAboveMinor);
        Assert.Equal("amountMinor:approval_required", await Refused(Desk().RefundAsync(bill.Id, big with { AmountMinor = 100 })));
        var kept = await settings.SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash], MaxDiscountPercent = 10, PaymentLinkExpiryMinutes = 30,
            Version = saved.Version,
        });
        Assert.Equal(0L, kept.RefundApprovalAboveMinor);
        Assert.Equal("refundApprovalAboveMinor:range", await Refused(settings.SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash], MaxDiscountPercent = 10, PaymentLinkExpiryMinutes = 30,
            RefundApprovalAboveMinor = -1, Version = kept.Version,
        })));
    }

    /// <summary>A credit note is never changed or deleted, by any role; another tenant sees none and cannot write one into this clinic.</summary>
    // SAFETY: mutation check: dropped TR_CreditNotes_AppendOnly from the migration -> this test failed (the owner's UPDATE of the credit note succeeded)
    [Fact]
    public async Task CreditNotesAreAppendOnlyAndTenantIsolated()
    {
        var bill = await PaidSplitAsync();
        var note = Assert.Single((await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 5_000, Reason = "Synthetic" }))!.CreditNotes);
        await using (var owner = sql.BillingAsAdmin(h.Tenant))
            foreach (var text in new[] { """UPDATE billing."CreditNotes" SET "AmountMinor" = 1 WHERE "Id" = @id""", """DELETE FROM billing."CreditNotes" WHERE "Id" = @id""" })
            {
                var e = await Assert.ThrowsAsync<PostgresException>(() => owner.Database.ExecuteSqlRawAsync(text, new NpgsqlParameter("id", note.Id)));
                Assert.Equal(PostgresErrorCodes.RestrictViolation, e.SqlState);
            }

        var other = Guid.NewGuid();
        await using var inOther = sql.Billing(other);
        Assert.Empty(await inOther.CreditNotes.ToListAsync()); // layer 1: the EF filter
        Assert.Empty(await inOther.CreditNotes.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        inOther.CreditNotes.Add(new CreditNote
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, InvoiceId = bill.Id, Number = "CN/X/1", Currency = "INR", AmountMinor = 1,
            Method = PaymentMethod.Cash, Reason = "Synthetic", CreatedAt = DateTimeOffset.UtcNow,
        });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => inOther.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(insert.InnerException).SqlState);
        var foreign = h.Get<IInvoices>(h.Reception, s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links));
            s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(other)));
        });
        Assert.Null(await foreign.RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 100, Reason = "Synthetic" }));
    }

    /// <summary>The desk refunds over HTTP; roles without "Invoices and payments" are refused; the old print route never prints a credit note.</summary>
    [Fact]
    public async Task TheFrontDeskRefundsOverHttpAndOtherRolesAreRefused()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var bill = await PaidSplitAsync();
        using var desk = Client(h.Reception);
        var refunded = await desk.PostAsJsonAsync($"/v1/billing/invoices/{bill.Id}/refunds", new { method = "Cash", amountMinor = 5_000, reason = "Synthetic" });
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        var view = await refunded.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5_000, view.GetProperty("refundedMinor").GetInt64());
        Assert.Equal(1, view.GetProperty("creditNotes").GetArrayLength());
        var invalid = await desk.PostAsJsonAsync($"/v1/billing/invoices/{bill.Id}/refunds", new { method = "Cash", amountMinor = 999_999, reason = "Synthetic" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desk.PostAsync(new Uri($"/v1/billing/invoices/{bill.Id}/documents/creditnote/a5", UriKind.Relative), null)).StatusCode);

        foreach (var role in new[] { Role.Nurse, Role.HospitalDoctor })
        {
            using var refused = Client(h.ByRole[role]);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await refused.PostAsJsonAsync($"/v1/billing/invoices/{bill.Id}/refunds", new { method = "Cash", amountMinor = 100, reason = "Synthetic" })).StatusCode);
        }
        Assert.Equal(5_000L, (await Desk().GetAsync(bill.Id))!.RefundedMinor);
    }

    /// <summary>
    /// A deposit on a partly paid bill goes back when the procedure is called off: the bill stays PartlyPaid and takes no more payments, and once everything
    /// paid is given back it can be cancelled (it keeps its number and its credit notes).
    /// </summary>
    [Fact]
    public async Task ADepositGoesBackAndTheBillIsThenCancelled()
    {
        var bill = await IssuedAsync(100_000);
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 50_000 });
        var cancel = new CancelInvoice { Reason = "Procedure called off (synthetic)" };

        var part = (await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 20_000, Reason = "Synthetic" }))!;
        Assert.Equal((InvoiceState.PartlyPaid, 20_000L), (part.State, part.RefundedMinor));
        Assert.Equal("state:invalid", await Refused(Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 100 }))); // a refund ends collecting
        Assert.Equal("state:invalid", await Refused(Desk().CancelAsync(bill.Id, cancel with { Version = part.Version }))); // ₹300 still kept
        var back = (await Desk().RefundAsync(bill.Id, new RecordRefund { Method = PaymentMethod.Cash, AmountMinor = 30_000, Reason = "Synthetic" }))!;
        Assert.Equal((InvoiceState.PartlyPaid, 50_000L), (back.State, back.RefundedMinor));
        var cancelled = (await Desk().CancelAsync(bill.Id, cancel with { Version = back.Version }))!;
        Assert.Equal((InvoiceState.Cancelled, bill.Number, 2, 0L), (cancelled.State, cancelled.Number, cancelled.CreditNotes.Count, cancelled.BalanceMinor));
    }

    /// <summary>A day counted before refunds existed has no refund fields in its stored totals: it still reads (as no refunds) and closes.</summary>
    [Fact]
    public async Task ADayCountedBeforeRefundsExistedStillReadsAndCloses()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 10_000 });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await PaidAsync(20_000, PaymentMethod.Cash);
        var counted = (await Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(0, 2), Note = "Synthetic short", Version = session.Version }))!;
        Assert.Equal(CashSessionState.AwaitingConfirmation, counted.State);
        await using (var owner = sql.BillingAsAdmin(h.Tenant)) // the totals JSON as the previous release wrote it
            await owner.Database.ExecuteSqlRawAsync("""UPDATE billing."CashSessions" SET "Totals" = @totals::jsonb WHERE "Id" = @id""",
                new NpgsqlParameter("totals", """[{"Method":"Cash","Payments":1,"AmountMinor":20000}]"""), new NpgsqlParameter("id", session.Id));

        var old = Assert.Single((await Drawer(h.Admin).DayAsync()).Awaiting);
        Assert.Equal((PaymentMethod.Cash, 20_000L, 0, 0L, 30_000L), (old.Totals[0].Method, old.Totals[0].AmountMinor, old.Totals[0].Refunds, old.Totals[0].RefundsMinor, old.ExpectedMinor));
        Assert.Equal(CashSessionState.Closed, (await Drawer(h.Admin).ConfirmAsync(session.Id, new ConfirmCashSession { Version = old.Version }))!.State);
        Assert.NotNull(await Drawer().ReportAsync(session.Id));
    }
}
