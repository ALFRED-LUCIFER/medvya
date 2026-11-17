using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-012 on real PostgreSQL: the cash session's expected cash and takings by method, the second-person rule for a variance, one open session per
/// location under concurrency, closed sessions immutable in the database with corrections as adjustments, tenant and location isolation, and the endpoints.
/// </summary>
public sealed partial class BillingInvoiceTests
{
    ICashSessions Drawer(Guid? user = null) => h.Get<ICashSessions>(user ?? h.Reception);

    /// <summary>₹500 × n plus ₹100 × m, as the desk types them.</summary>
    static List<CashCount> Notes(int fiveHundreds, int hundreds = 0) =>
        [new() { ValueMinor = 50_000, Count = fiveHundreds }, new() { ValueMinor = 10_000, Count = hundreds }];

    async Task PaidAsync(long amount, PaymentMethod method, string? reference = null)
    {
        var bill = await IssuedAsync(amount);
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = method, AmountMinor = amount, Reference = reference });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ExpectedCashIsTheFloatPlusCashReceiptsMinusCashRefunds()
    {
        Assert.Equal(160_000, CashSession.Expected(100_000, 60_000));
        Assert.Equal(150_000, CashSession.Expected(100_000, 60_000, 10_000));
        Assert.Equal([50_000L, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100], BillingCountryRules.India.CashDenominationsMinor);
    }

    /// <summary>
    /// The session's takings are this location's succeeded payments from its opening to its count, by method; expected cash is the float plus the cash;
    /// a count that matches closes the day with no confirmation.
    /// </summary>
    [Fact]
    public async Task ACountThatMatchesClosesTheDayWithTheTakingsByMethod()
    {
        await PaidAsync(30_000, PaymentMethod.Cash); // before the session opened: not this session's
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 100_000 });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await PaidAsync(40_000, PaymentMethod.Cash);
        await PaidAsync(20_000, PaymentMethod.Cash);
        await PaidAsync(30_000, PaymentMethod.Card, "TID-0001");
        await PaidAsync(25_000, PaymentMethod.UpiDirect, "UTR000000001");

        var day = await Drawer().DayAsync();
        Assert.Equal((session.Id, 160_000L), (day.Current!.Id, day.Current.ExpectedMinor)); // live while open
        var closed = (await Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(3, 1), Version = day.Current.Version }))!;
        Assert.Equal((CashSessionState.Closed, 160_000L, 160_000L, 0L), (closed.State, closed.ExpectedMinor, closed.CountedMinor!.Value, closed.VarianceMinor!.Value));
        Assert.Equal([(PaymentMethod.Cash, 2, 60_000L), (PaymentMethod.Card, 1, 30_000L), (PaymentMethod.UpiDirect, 1, 25_000L)],
            closed.Totals.Select(t => (t.Method, t.Payments, t.AmountMinor)));
        Assert.Null(closed.ConfirmedByName);
        Assert.Equal(9, closed.Counts.Count); // every denomination, in order

        await PaidAsync(10_000, PaymentMethod.Cash); // after the close: the next session's, never this one's
        var after = await Drawer().DayAsync();
        Assert.Null(after.Current);
        Assert.Equal((160_000L, 2), (after.Recent[0].ExpectedMinor, after.Recent[0].Totals[0].Payments));
    }

    /// <summary>A short drawer needs a note, then a second, different staff user's confirmation; the counter can never confirm their own variance.</summary>
    // SAFETY: mutation check: dropped "user != CountedBy" from CashSession.MayConfirm -> this test failed (the receptionist confirmed their own short drawer)
    // SAFETY: mutation check: removed the note rule in CashSessions.CountAsync -> this test failed (the short count was accepted with no note)
    [Fact]
    public async Task AVarianceNeedsANoteAndASecondDifferentUser()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 50_000 });
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await PaidAsync(70_000, PaymentMethod.Cash); // expected ₹1,200

        Assert.Equal("note:required", await Refused(Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(2), Version = session.Version })));
        Assert.Equal("counts:invalid", await Refused(Drawer().CountAsync(session.Id, new CountCashSession { Counts = [new() { ValueMinor = 7_000, Count = 1 }], Version = session.Version })));
        var counted = (await Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(2), Note = "₹200 change given from personal cash", Version = session.Version }))!;
        Assert.Equal((CashSessionState.AwaitingConfirmation, -20_000L), (counted.State, counted.VarianceMinor!.Value));
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("confirm:same_user", await Refused(Drawer().ConfirmAsync(session.Id, new ConfirmCashSession { Version = counted.Version })));
        Assert.False(counted.MayConfirm);
        Assert.Equal(CashSessionState.AwaitingConfirmation, Assert.Single((await Drawer().DayAsync()).Awaiting).State);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Drawer(h.Admin).ConfirmAsync(session.Id, new ConfirmCashSession { Version = session.Version })); // not the count they saw

        var view = Assert.Single((await Drawer(h.Admin).DayAsync()).Awaiting);
        Assert.True(view.MayConfirm);
        var closed = (await Drawer(h.Admin).ConfirmAsync(session.Id, new ConfirmCashSession { Version = view.Version }))!;
        Assert.Equal((CashSessionState.Closed, "₹200 change given from personal cash"), (closed.State, closed.Note));
        Assert.NotNull(closed.ClosedAt);
        await using var audit = sql.Audit(h.Tenant);
        var actions = await audit.Events.Where(e => e.ResourceId == session.Id).OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.ActorUserId }).ToListAsync();
        Assert.Equal(["billing.cash.open", "billing.cash.count", "billing.cash.close"], actions.Select(a => a.Action));
        Assert.Equal(h.Admin, actions[^1].ActorUserId);
        Assert.NotNull(await Drawer().ReportAsync(session.Id)); // the shift report of the closed session
    }

    /// <summary>A closed session never changes, not through the service and not in the database for the app's role; a correction is an adjustment.</summary>
    // SAFETY: mutation check: dropped TR_CashSessions_ClosedImmutable from the migration -> this test failed (the UPDATE of the closed row succeeded)
    [Fact]
    public async Task AClosedSessionIsImmutableAndCorrectedByAnAdjustment()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 50_000 });
        var closed = (await Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(1), Version = session.Version }))!;
        Assert.Equal(CashSessionState.Closed, closed.State);

        Assert.Equal("state:invalid", await Refused(Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(2), Note = "Recount", Version = closed.Version })));
        Assert.Equal("state:invalid", await Refused(Drawer(h.Admin).ConfirmAsync(session.Id, new ConfirmCashSession { Version = closed.Version })));
        // The app's role, and even the database owner: the trigger refuses for every role.
        await using (var app = sql.Billing(h.Tenant))
        await using (var owner = sql.BillingAsAdmin(h.Tenant))
        {
            foreach (var (db, sqlText) in new[] { (app, """UPDATE billing."CashSessions" SET "CountedMinor" = 1 WHERE "Id" = @id"""),
                (owner, """UPDATE billing."CashSessions" SET "VarianceMinor" = 0 WHERE "Id" = @id"""), (owner, """DELETE FROM billing."CashSessions" WHERE "Id" = @id""") })
            {
                var e = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sqlText, new NpgsqlParameter("id", session.Id)));
                Assert.Equal(PostgresErrorCodes.RestrictViolation, e.SqlState);
            }
        }

        Assert.Equal("reason:required", await Refused(Drawer().AdjustAsync(session.Id, new AddCashAdjustment { AmountMinor = -10_000 })));
        Assert.Equal("amountMinor:range", await Refused(Drawer().AdjustAsync(session.Id, new AddCashAdjustment { AmountMinor = 0, Reason = "Synthetic" })));
        var adjusted = (await Drawer(h.Admin).AdjustAsync(session.Id, new AddCashAdjustment { AmountMinor = -10_000, Reason = "Note found to be counterfeit next morning" }))!;
        var adjustment = Assert.Single(adjusted.Adjustments);
        Assert.Equal((-10_000L, closed.CountedMinor, closed.VarianceMinor, closed.Version), (adjustment.AmountMinor, adjusted.CountedMinor, adjusted.VarianceMinor, adjusted.Version));
        await using (var db = sql.BillingAsAdmin(h.Tenant))
        {
            var e = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""UPDATE billing."CashAdjustments" SET "AmountMinor" = 1 WHERE "Id" = @id""", new NpgsqlParameter("id", adjustment.Id)));
            Assert.Equal(PostgresErrorCodes.RestrictViolation, e.SqlState);
        }
        var opened = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 50_000 }); // the next shift
        Assert.Equal("state:invalid", await Refused(Drawer().AdjustAsync(opened.Id, new AddCashAdjustment { AmountMinor = 100, Reason = "Synthetic" }))); // open: recount instead
    }

    /// <summary>
    /// While a short drawer waits for its second person, the desk opens the next one and keeps taking cash: each payment counts in the session that was
    /// Open when it succeeded, never in none. Still at most one Open session.
    /// </summary>
    // SAFETY: mutation check: unique index filter back to "State" <> 'Closed' -> this test failed (session:already_open while the first awaited confirmation)
    // SAFETY: mutation check: dropped the CashSessionId stamp in Invoices.RecordPaymentAsync -> this test failed (the next session's cash was 0)
    // SAFETY: mutation check: dropped the CashSessionId stamp in Invoices.SettleAsync -> this test failed (the paid link was in no session)
    [Fact]
    public async Task CashTakenWhileADrawerAwaitsConfirmationCountsInTheNextOpenSession()
    {
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var first = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 50_000 });
        await PaidAsync(70_000, PaymentMethod.Cash);
        var linked = await IssuedAsync(25_000);
        var link = (await Desk().CreatePaymentLinkAsync(linked.Id))!; // sent during the first session, paid during the next
        var awaiting = (await Drawer().CountAsync(first.Id, new CountCashSession { Counts = Notes(2), Note = "₹200 short (synthetic)", Version = first.Version }))!;
        Assert.Equal(CashSessionState.AwaitingConfirmation, awaiting.State);

        var next = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 20_000 });
        Assert.Equal("session:already_open", await Refused(Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 0 })));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await PaidAsync(30_000, PaymentMethod.Cash);
        links.State = new PaymentLinkState("plink_synthetic", PaymentLinkStatus.Paid, new Money(25_000, Currency.Inr), ["pay_synthetic"]);
        await Desk().CheckPaymentAsync(linked.Id, link.PaymentId);
        var day = await Drawer().DayAsync();
        Assert.Equal((next.Id, first.Id), (day.Current!.Id, Assert.Single(day.Awaiting).Id));
        Assert.Equal(50_000L, day.Current.ExpectedMinor);

        var confirmed = (await Drawer(h.Admin).ConfirmAsync(first.Id, new ConfirmCashSession { Version = awaiting.Version }))!;
        Assert.Equal((CashSessionState.Closed, 70_000L), (confirmed.State, Assert.Single(confirmed.Totals).AmountMinor));
        var closed = (await Drawer().CountAsync(next.Id, new CountCashSession { Counts = Notes(1), Version = day.Current.Version }))!;
        Assert.Equal((CashSessionState.Closed, 0L), (closed.State, closed.VarianceMinor!.Value));
        Assert.Equal([(PaymentMethod.Cash, 30_000L), (PaymentMethod.RazorpayLink, 25_000L)], closed.Totals.Select(x => (x.Method, x.AmountMinor)));
    }

    /// <summary>
    /// A payment that has just read the Open session, its transaction still open, when the count starts: the count waits for it (the payment holds the
    /// session FOR SHARE), so the payment is stamped with that session and in its totals; exactly one session has it.
    /// </summary>
    // SAFETY: mutation check: dropped FOR SHARE in Invoices.OpenCashSessionAsync -> this test failed (the count closed with no takings; the payment then committed stamped with it)
    [Fact]
    public async Task APaymentCommittingDuringTheCountLandsInExactlyOneSession()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 0 });
        var bill = await IssuedAsync(40_000);
        var gate = new StampGate();
        var desk = h.Get<IInvoices>(h.Reception, s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links));
            s.ConfigureDbContext<Medvya.Billing.Infrastructure.Persistence.BillingDbContext>(o => o.AddInterceptors(gate));
        });
        var paying = desk.RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 40_000 });
        await gate.Reached.Task; // the payment has read the Open session; nothing saved or committed yet
        var counting = Drawer().CountAsync(session.Id, new CountCashSession { Counts = Notes(0), Note = "Counted while a payment was being taken (synthetic)", Version = session.Version });
        await Task.WhenAny(counting, Task.Delay(TimeSpan.FromSeconds(1)));
        gate.Release.SetResult();
        await paying;
        var counted = (await counting)!;

        await using var db = sql.Billing(h.Tenant);
        var payment = await db.Payments.SingleAsync(p => p.InvoiceId == bill.Id);
        Assert.Equal((Guid?)session.Id, payment.CashSessionId);
        Assert.Equal(40_000L, Assert.Single(counted.Totals).AmountMinor);
        Assert.Equal(-40_000L, counted.VarianceMinor!.Value);
    }

    /// <summary>Holds a payment's transaction open right after it reads the location's Open cash session, until the test releases it.</summary>
    sealed class StampGate : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"CashSessions\" WHERE \"FacilityId\"", StringComparison.Ordinal) && Reached.TrySetResult()) await Release.Task;
            return result;
        }
    }

    // SAFETY: mutation check: dropped the unique index on CashSessions (TenantId, FacilityId) where Open -> this test failed (both desks opened a session)
    [Fact]
    public async Task OneLocationHasOneOpenSessionEvenWhenTwoDesksOpenAtOnce()
    {
        var second = (await h.AddReceptionistsAsync(1))[0];
        var outcomes = await Task.WhenAll(new[] { h.Reception, second }.Select(async u =>
        {
            try { await Drawer(u).OpenAsync(new OpenCashSession { OpeningFloatMinor = 10_000 }); return "opened"; }
            catch (BillingValidationException e) { return $"{e.Errors[0].Field}:{e.Errors[0].Code}"; }
        }));
        Assert.Equal(["opened", "session:already_open"], outcomes.Order());
        Assert.Equal("openingFloatMinor:range", await Refused(Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = -1 })));

        // Another location of the same clinic has its own drawer, and cannot reach this one.
        var elsewhere = h.Get<ICashSessions>(h.Elsewhere, s => s.Replace(ServiceDescriptor.Singleton<ICurrentFacility>(new PatientRegistryTests.FixedFacility(h.OtherFacility))));
        var here = (await Drawer().DayAsync()).Current!;
        Assert.Null((await elsewhere.DayAsync()).Current);
        Assert.Null(await elsewhere.CountAsync(here.Id, new CountCashSession { Counts = Notes(0, 1), Version = here.Version }));
        Assert.Equal(CashSessionState.Open, (await elsewhere.OpenAsync(new OpenCashSession { OpeningFloatMinor = 0 })).State);
    }

    [Fact]
    public async Task AnotherTenantSeesNoSessionsAndCannotWriteIntoThem()
    {
        var session = await Drawer().OpenAsync(new OpenCashSession { OpeningFloatMinor = 10_000 });
        var other = Guid.NewGuid();
        await using var inOther = sql.Billing(other);
        Assert.Empty(await inOther.CashSessions.ToListAsync()); // layer 1: the EF filter
        Assert.Empty(await inOther.CashSessions.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        Assert.Equal(0, await inOther.Database.ExecuteSqlRawAsync("""UPDATE billing."CashSessions" SET "OpeningFloatMinor" = 0 WHERE "Id" = @id""", new NpgsqlParameter("id", session.Id)));
        inOther.CashAdjustments.Add(new CashAdjustment { Id = Guid.NewGuid(), TenantId = h.Tenant, SessionId = session.Id, AmountMinor = 1, Reason = "Synthetic", CreatedAt = DateTimeOffset.UtcNow });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => inOther.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(insert.InnerException).SqlState);

        var foreign = h.Get<ICashSessions>(h.Reception, s => s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(other))));
        Assert.Null(await foreign.ConfirmAsync(session.Id, new ConfirmCashSession { Version = session.Version }));
        Assert.Null(await foreign.ReportAsync(session.Id));
        Assert.Equal(10_000, (await Drawer().DayAsync()).Current!.OpeningFloatMinor);
    }

    /// <summary>The receptionist runs the day close over HTTP and gets the shift report; roles without "Invoices and payments" are refused; billing off is 404.</summary>
    [Fact]
    public async Task OnlyTheFrontDeskClosesTheDayAndBillingOffIs404()
    {
        await h.StepsAsync(nurse: false, billing: true);
        using var desk = Client(h.Reception);
        var opened = await desk.PostAsJsonAsync("/v1/billing/cash-sessions", new { openingFloatMinor = 20_000 });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var session = await opened.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await desk.PostAsync(new Uri($"/v1/billing/cash-sessions/{id}/report/pdf", UriKind.Relative), null)).StatusCode); // not closed yet
        var counted = await desk.PostAsJsonAsync($"/v1/billing/cash-sessions/{id}/count",
            new { counts = new[] { new { valueMinor = 10_000, count = 2 } }, version = session.GetProperty("version").GetUInt32() });
        Assert.Equal("Closed", (await counted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        var day = await desk.GetFromJsonAsync<JsonElement>(new Uri("/v1/billing/cash-sessions", UriKind.Relative));
        Assert.Equal(9, day.GetProperty("denominations").GetArrayLength());

        var report = await desk.PostAsync(new Uri($"/v1/billing/cash-sessions/{id}/report/pdf", UriKind.Relative), null);
        Assert.Equal((HttpStatusCode.OK, "application/pdf"), (report.StatusCode, report.Content.Headers.ContentType?.MediaType));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(await report.Content.ReadAsByteArrayAsync())[..5], StringComparison.Ordinal);

        foreach (var role in new[] { Role.Nurse, Role.HospitalDoctor })
        {
            using var refused = Client(h.ByRole[role]);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.GetAsync(new Uri("/v1/billing/cash-sessions", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PostAsJsonAsync("/v1/billing/cash-sessions", new { openingFloatMinor = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PostAsync(new Uri($"/v1/billing/cash-sessions/{id}/report/pdf", UriKind.Relative), null)).StatusCode);
        }
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Billing, false, "practice", "Billing (synthetic test)");
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync(new Uri("/v1/billing/cash-sessions", UriKind.Relative))).StatusCode);
    }
}
