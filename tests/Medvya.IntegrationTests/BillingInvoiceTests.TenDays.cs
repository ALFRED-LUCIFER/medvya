using Medvya.Billing.Application;
using Medvya.Billing.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-012 exit gate 1 rehearsed (MDV-P4-033): ten desk days on real PostgreSQL with synthetic bills, split payments and refunds. The test keeps its own
/// ledger of the cash that must be in the drawer and checks Medvya's expected cash against it every day; every close has zero unexplained variance.
/// </summary>
public sealed partial class BillingInvoiceTests
{
    /// <summary>
    /// Each day opens a ₹1,000 float, bills 6–12 walk-ins (₹300 to ₹1,200; cash, card, UPI or part cash part card), refunds some (cash or back to the
    /// card), and counts the drawer note by note. Day 6 is ₹100 short: a note and a second person explain it. The difference between the test's ledger and
    /// Medvya's expected cash is 0 every day, and no close leaves a variance without a note and a confirmation.
    /// </summary>
    // SAFETY: mutation check: CashSession.Expected without the cash refunds -> this test failed (Medvya's expected cash no longer matched the ledger)
    [Fact]
    public async Task TenDaysOfDeskClosesHaveZeroUnexplainedVariance()
    {
        var random = new Random(20261118); // fixed: the same ten days every run
        var service = await PricedAsync("Consultation (synthetic)", 10_000); // ₹100 a unit: a bill is 3 to 12 units
        long[] prices = [30_000, 50_000, 80_000, 120_000];
        var denominations = BillingCountryRules.India.CashDenominationsMinor;
        var rows = new List<string> { "day | bills | cash in | cash back | card/UPI | ledger | Medvya expected | difference | counted | variance | explained" };

        for (var day = 1; day <= 10; day++)
        {
            var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 100_000 });
            long ledger = 100_000, cashIn = 0, cashBack = 0, other = 0;
            var bills = random.Next(6, 13);
            for (var b = 0; b < bills; b++)
            {
                h.Clock.Advance(TimeSpan.FromMinutes(20));
                var price = prices[random.Next(prices.Length)];
                var draft = (await Desk().CreateAsync(new NewInvoice { PatientId = h.Patient }))!;
                draft = (await Desk().AddLineAsync(draft.Id, new AddLine { ServiceId = service.Id, Quantity = (int)(price / 10_000), Version = draft.Version }))!;
                var bill = (await Desk().IssueAsync(draft.Id, new InvoiceVersion { Version = draft.Version }))!;
                var cash = random.Next(4) switch { 0 => price, 1 => 0L, 2 => 0L, _ => price / 2 }; // all cash, card, UPI, or half cash half card
                var upi = cash == 0 && random.Next(2) == 0;
                if (cash > 0) await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = cash });
                if (price - cash > 0)
                    await Desk().RecordPaymentAsync(bill.Id, new RecordPayment
                    {
                        Method = upi ? PaymentMethod.UpiDirect : PaymentMethod.Card, AmountMinor = price - cash, Reference = upi ? $"UTR{day:00}{b:00}0001" : $"TID {day}{b}",
                    });
                (ledger, cashIn, other) = (ledger + cash, cashIn + cash, other + price - cash);

                if (random.Next(5) == 0) // about one bill in five gets money back, under the desk's ₹1,000 threshold
                {
                    var back = Math.Min(price, 50_000) / 2;
                    var byCard = cash < back;
                    await Desk().RefundAsync(bill.Id, new RecordRefund
                    {
                        Method = byCard ? (upi ? PaymentMethod.UpiDirect : PaymentMethod.Card) : PaymentMethod.Cash, AmountMinor = back,
                        Reference = byCard ? $"RFND {day}{b}" : null, Reason = "Service not given (synthetic)",
                    });
                    if (byCard) other -= back; else (ledger, cashBack) = (ledger - back, cashBack + back);
                }
            }

            var live = (await Drawer().DayAsync()).Current!;
            Assert.Equal(ledger, live.ExpectedMinor); // the difference the gate is about: Medvya and the drawer's own ledger agree
            var counted = day == 6 ? ledger - 10_000 : ledger;
            List<CashCount> notes = [];
            var left = counted;
            foreach (var d in denominations) { notes.Add(new CashCount { ValueMinor = d, Count = (int)(left / d) }); left %= d; }
            h.Clock.Advance(TimeSpan.FromMinutes(30));
            var closed = (await Drawer().CountAsync(session.Id, new CountCashSession
            {
                Counts = notes, Note = counted == ledger ? null : "₹100 note given as change twice (synthetic)", Version = live.Version,
            }))!;
            if (closed.State == CashSessionState.AwaitingConfirmation)
                closed = (await Drawer(h.Admin).ConfirmAsync(session.Id, new ConfirmCashSession { Version = closed.Version }))!;

            Assert.Equal(CashSessionState.Closed, closed.State);
            var explained = closed.VarianceMinor == 0 || closed is { Note: not null, ConfirmedByName: not null };
            Assert.True(explained, $"day {day}: unexplained variance {closed.VarianceMinor}");
            rows.Add($"{day} | {bills} | {cashIn / 100} | {cashBack / 100} | {other / 100} | {ledger / 100} | {closed.ExpectedMinor / 100} | {(closed.ExpectedMinor - ledger) / 100} | "
                + $"{closed.CountedMinor / 100} | {closed.VarianceMinor / 100} | {(closed.VarianceMinor == 0 ? "-" : "note + second person")}");
            h.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(20 * bills + 30)); // the next morning
        }
        foreach (var row in rows) output.WriteLine(row);
        Assert.Equal(10, (await Drawer().DayAsync()).Recent.Count);
    }
}
