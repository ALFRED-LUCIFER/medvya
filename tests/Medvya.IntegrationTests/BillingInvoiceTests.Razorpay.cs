using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Razorpay;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Medvya.IntegrationTests;

/// <summary>Synthetic Razorpay test keys for two clinics, as Development reads them from user secrets (ADR-023 §8).</summary>
static class RazorpayTestKeys
{
    public static readonly Guid OtherTenant = Guid.NewGuid();
    public const string KeyId = "rzp_test_Synth0001", KeySecret = "synthetic-key-secret-0001", WebhookSecret = "synthetic-webhook-secret-0001";
    public const string OtherWebhookSecret = "synthetic-webhook-secret-0002";
    public static readonly string WebhookKey = new('a', 64), OtherWebhookKey = new('c', 64);

    public static Dictionary<string, string?> Settings(Guid tenant, Guid? other = null)
    {
        var s = new Dictionary<string, string?>
        {
            [$"Razorpay:Tenants:{tenant}:KeyId"] = KeyId, [$"Razorpay:Tenants:{tenant}:KeySecret"] = KeySecret,
            [$"Razorpay:Tenants:{tenant}:WebhookSecret"] = WebhookSecret, [$"Razorpay:Tenants:{tenant}:WebhookKey"] = WebhookKey,
        };
        if (other is { } o)
            (s[$"Razorpay:Tenants:{o}:KeyId"], s[$"Razorpay:Tenants:{o}:KeySecret"], s[$"Razorpay:Tenants:{o}:WebhookSecret"], s[$"Razorpay:Tenants:{o}:WebhookKey"]) =
                ("rzp_test_Synth0002", "synthetic-key-secret-0002", OtherWebhookSecret, OtherWebhookKey);
        return s;
    }

    /// <summary>The Development store with these keys, for the harness (which has no host configuration); no tenants = nobody connected.</summary>
    public static void Connect(IServiceCollection services, params Guid[] tenants) =>
        services.Replace(ServiceDescriptor.Singleton<IRazorpayCredentialStore>(new ConfigurationRazorpayCredentialStore(
            new ConfigurationBuilder().AddInMemoryCollection(tenants.SelectMany(t => Settings(t))).Build(), new HostingEnvironment { EnvironmentName = "Development" })));
}

/// <summary>
/// MDV-P4-007 part 2 on real PostgreSQL: the clinic's Razorpay webhook (signature before parsing, the key picks the clinic, replays change state once, the
/// payload is never trusted, a transient failure is retried by Razorpay) and Settings, Billing, Razorpay. Synthetic keys and data only.
/// </summary>
public sealed partial class BillingInvoiceTests
{
    const string LinkId = "plink_synthetic";

    static string Sign(string secret, string body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    /// <summary>What Razorpay sends for a paid link, with an amount and a tenant the server must ignore.</summary>
    static string Paid(string linkId = LinkId, long amountPaid = 1, Guid? tenant = null) => JsonSerializer.Serialize(new
    {
        entity = "event", @event = "payment_link.paid",
        payload = new { payment_link = new { entity = new { id = linkId, status = "paid", amount_paid = amountPaid, currency = "INR", notes = new { tenant = tenant ?? Guid.NewGuid() } } } },
    });

    HttpClient Webhooks(IDictionary<string, string?>? settings = null)
    {
        var client = Client(h.Reception, settings: settings);
        client.DefaultRequestHeaders.Remove("X-Test-User"); // Razorpay is never signed in
        return client;
    }

    static async Task<HttpStatusCode> DeliverAsync(HttpClient client, string body, string? signature, string eventId, string key = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/webhooks/razorpay/{(key.Length > 0 ? key : RazorpayTestKeys.WebhookKey)}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null) request.Headers.Add("X-Razorpay-Signature", signature);
        request.Headers.Add("x-razorpay-event-id", eventId);
        return (await client.SendAsync(request)).StatusCode;
    }

    async Task<(InvoiceView Bill, Guid PaymentId)> PendingLinkAsync(long amount = 40_000)
    {
        await MethodsAsync(PaymentMethod.Cash, PaymentMethod.RazorpayLink);
        var bill = await IssuedAsync(amount);
        return (bill, (await Desk().CreatePaymentLinkAsync(bill.Id))!.PaymentId);
    }

    async Task<PaymentState> StateAsync(InvoiceView bill, Guid paymentId) => (await Desk().GetAsync(bill.Id))!.Payments.Single(p => p.Id == paymentId).State;

    async Task<int> AuditCountAsync(string action, Guid? resource = null)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.CountAsync(e => e.Action == action && (resource == null || e.ResourceId == resource));
    }

    async Task<List<string>> ProviderEventsAsync(Guid tenant)
    {
        await using var billing = sql.Billing(tenant);
        return await billing.ProviderEvents.Select(e => e.EventId).ToListAsync();
    }

    /// <summary>The AC: a valid signed event settles the payment, and the same event delivered ten times changes state once.</summary>
    // SAFETY: mutation check: removed the ProviderEvents replay check in RazorpayAccount.HandleWebhookAsync -> this test failed (each replay fetched the link again)
    [Fact]
    public async Task AReplayedEventChangesStateOnce()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        using var client = Webhooks();
        var body = Paid();
        // Still unpaid at the provider: the first delivery fetches the link, the nine replays do nothing at all.
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_synthetic_0"));
        Assert.Equal([h.Tenant], links.Fetches); // one fetch, in the clinic's own account
        Assert.Equal(PaymentState.Pending, await StateAsync(bill, payment));

        links.State = new PaymentLinkState(LinkId, PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_synthetic_1"));
        Assert.Equal(PaymentState.Succeeded, await StateAsync(bill, payment));
        var paid = (await Desk().GetAsync(bill.Id))!;
        Assert.Equal((InvoiceState.Paid, 40_000L), (paid.State, paid.PaidMinor));
        Assert.Equal(2, links.Fetches.Count);
        Assert.Equal(1, await AuditCountAsync("billing.payment.succeeded", bill.Id));
        Assert.Equal(["evt_synthetic_0", "evt_synthetic_1"], (await ProviderEventsAsync(h.Tenant)).Order());
    }

    /// <summary>The payload says paid with some amount for some tenant; only the provider's answer to a fresh fetch counts.</summary>
    [Fact]
    public async Task ThePayloadIsNeverTrusted()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        using var client = Webhooks();
        var body = Paid(amountPaid: 40_000, tenant: h.Tenant); // the payload claims the full amount; the provider (fake) still says created
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_synthetic_2"));
        Assert.Equal(PaymentState.Pending, await StateAsync(bill, payment));

        // An event type that is not about payments is recorded and answered, and fetches nothing.
        var other = """{"event":"refund.created","payload":{}}""";
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, other, Sign(RazorpayTestKeys.WebhookSecret, other), "evt_synthetic_3"));
        Assert.Single(links.Fetches);
        Assert.Equal(["evt_synthetic_2", "evt_synthetic_3"], (await ProviderEventsAsync(h.Tenant)).Order());
    }

    /// <summary>The AC "forged webhook rejected": no signature, another secret's signature or a changed body is 401, audited, and changes nothing.</summary>
    // SAFETY: mutation check: skipped the signature check in RazorpayEndpoints.Webhook -> this test failed (the forged event settled the payment)
    [Fact]
    public async Task AForgedOrUnsignedWebhookIsRefusedAndChangesNothing()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        links.State = new PaymentLinkState(LinkId, PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]);
        using var client = Webhooks();
        var body = Paid();
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(client, body, null, "evt_forged"));
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(client, body, Sign(RazorpayTestKeys.OtherWebhookSecret, body), "evt_forged"));
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body + " "), "evt_forged"));
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(client, body, "not-hex", "evt_forged"));

        Assert.Equal(PaymentState.Pending, await StateAsync(bill, payment));
        Assert.Empty(links.Fetches);
        Assert.Empty(await ProviderEventsAsync(h.Tenant)); // a forged event id does not block the real one
        await using (var audit = sql.Audit(h.Tenant))
            Assert.Equal(Enumerable.Repeat(AuditOutcome.Denied, 4), await audit.Events.Where(e => e.Action == "billing.webhook.forged").Select(e => e.Outcome).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_forged"));
        Assert.Equal(PaymentState.Succeeded, await StateAsync(bill, payment));
    }

    /// <summary>Another clinic's URL and secret, with this clinic's link id (and this clinic named in the payload): nothing of this clinic is reached.</summary>
    // SAFETY: mutation check: entered the tenant named in the payload's notes instead of the webhook key's in RazorpayEndpoints.Webhook -> this test failed (settled)
    [Fact]
    public async Task AnotherClinicsKeyCannotSettleThisClinicsPayment()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        links.State = new PaymentLinkState(LinkId, PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]);
        using var client = Webhooks();
        var body = Paid(tenant: h.Tenant);
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.OtherWebhookSecret, body), "evt_other", RazorpayTestKeys.OtherWebhookKey));
        Assert.Equal(PaymentState.Pending, await StateAsync(bill, payment));
        Assert.Empty(links.Fetches); // the other clinic has no such payment, so nothing is fetched with anyone's keys
        Assert.Equal(["evt_other"], await ProviderEventsAsync(RazorpayTestKeys.OtherTenant));
        Assert.Empty(await ProviderEventsAsync(h.Tenant));
        // This clinic's key with the other clinic's secret is a forgery.
        Assert.Equal(HttpStatusCode.Unauthorized, await DeliverAsync(client, body, Sign(RazorpayTestKeys.OtherWebhookSecret, body), "evt_other"));
    }

    [Fact]
    public async Task AnUnknownKeyIs404AndTouchesNothing()
    {
        using var client = Webhooks();
        var body = Paid();
        Assert.Equal(HttpStatusCode.NotFound, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_x", new string('b', 64)));
        Assert.Equal(HttpStatusCode.NotFound, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_x", "not-a-key"));
        Assert.Equal(0, await AuditCountAsync("billing.webhook.forged"));
        Assert.Empty(await ProviderEventsAsync(h.Tenant));
    }

    /// <summary>Razorpay unreachable while confirming: 503 and the event is not recorded, so Razorpay's retry settles it. A huge or eventless body is refused.</summary>
    [Fact]
    public async Task ATransientFailureIsLeftForRazorpaysRetry()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        using var client = Webhooks();
        var body = Paid();
        links.Fail = PaymentProviderError.Unavailable;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_retry"));
        Assert.Empty(await ProviderEventsAsync(h.Tenant));
        (links.Fail, links.State) = (null, new PaymentLinkState(LinkId, PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]));
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_retry"));
        Assert.Equal(PaymentState.Succeeded, await StateAsync(bill, payment));

        var huge = "{\"event\":\"payment_link.paid\",\"pad\":\"" + new string('x', 70_000) + "\"}";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, await DeliverAsync(client, huge, Sign(RazorpayTestKeys.WebhookSecret, huge), "evt_huge"));
        // The same without a Content-Length (chunked): the limit is on the bytes read, never on the caller's header.
        using (var chunked = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/webhooks/razorpay/{RazorpayTestKeys.WebhookKey}", UriKind.Relative)) { Content = new Chunked(Encoding.UTF8.GetBytes(huge)) })
        {
            chunked.Headers.Add("X-Razorpay-Signature", Sign(RazorpayTestKeys.WebhookSecret, huge));
            chunked.Headers.Add("x-razorpay-event-id", "evt_huge_chunked");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(chunked)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), new string('e', 65)));
    }

    sealed class Chunked(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    /// <summary>Wrong or revoked keys: retrying cannot help, so the webhook is answered (Razorpay keeps the clinic's webhook on), not recorded, and logged.</summary>
    [Fact]
    public async Task RefusedKeysAreAnsweredAndLeftPending()
    {
        var (bill, payment) = await PendingLinkAsync(40_000);
        var logs = new List<string>();
        var client = Client(h.Reception, s => s.AddSingleton<ILoggerProvider>(new CaptureLogs(logs)));
        client.DefaultRequestHeaders.Remove("X-Test-User");
        var body = Paid();
        foreach (var error in new[] { PaymentProviderError.Unauthorized, PaymentProviderError.Rejected, PaymentProviderError.NotConnected })
        {
            links.Fail = error;
            Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_refused"));
        }
        Assert.Equal(PaymentState.Pending, await StateAsync(bill, payment));
        Assert.Empty(await ProviderEventsAsync(h.Tenant));
        Assert.Contains(logs, l => l.Contains("could not confirm its payment link: Unauthorized", StringComparison.Ordinal));
        (links.Fail, links.State) = (null, new PaymentLinkState(LinkId, PaymentLinkStatus.Paid, new Money(40_000, Currency.Inr), ["pay_synthetic"]));
        Assert.Equal(HttpStatusCode.OK, await DeliverAsync(client, body, Sign(RazorpayTestKeys.WebhookSecret, body), "evt_refused")); // keys fixed: the next delivery settles
        Assert.Equal(PaymentState.Succeeded, await StateAsync(bill, payment));
    }

    /// <summary>The same event delivered twice at once: the second save meets the first's row and answers "replay", no error.</summary>
    [Fact]
    public async Task TwoDeliveriesAtOnceRecordTheEventOnce()
    {
        await PendingLinkAsync(40_000);
        links.DuringFetch = async () =>
        {
            links.DuringFetch = null;
            await using var other = sql.Billing(h.Tenant);
            other.ProviderEvents.Add(new ProviderEvent { TenantId = h.Tenant, Provider = "razorpay", EventId = "evt_twice", Type = "payment_link.paid", ReceivedAt = h.Clock.GetUtcNow() });
            await other.SaveChangesAsync();
        };
        var account = h.Get<IRazorpayAccount>(h.Admin, s => { RazorpayTestKeys.Connect(s, h.Tenant); s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(links)); });
        Assert.False(await account.HandleWebhookAsync(new RazorpayWebhookEvent("evt_twice", "payment_link.paid", LinkId)));
        Assert.Equal(["evt_twice"], await ProviderEventsAsync(h.Tenant));
    }

    [Fact]
    public async Task TheWebhookIsRateLimitedPerAddress()
    {
        using var client = Webhooks(new Dictionary<string, string?> { ["RateLimiting:PaymentWebhooksPerMinute"] = "3" });
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) codes.Add(await DeliverAsync(client, "{}", null, "evt", new string('b', 64)));
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], codes);
    }

    // SAFETY: mutation check: removed migrationBuilder.Tenant("billing", "ProviderEvents") -> this test failed (the IgnoreQueryFilters read saw the other tenant's row)
    [Fact]
    public async Task ProviderEventsAreIsolatedByRowLevelSecurity()
    {
        await using (var mine = sql.Billing(h.Tenant))
        {
            mine.ProviderEvents.Add(new ProviderEvent { TenantId = h.Tenant, Provider = "razorpay", EventId = "evt_rls", Type = "payment_link.paid", ReceivedAt = h.Clock.GetUtcNow() });
            await mine.SaveChangesAsync();
        }
        await using var other = sql.Billing(Guid.NewGuid());
        Assert.Empty(await other.ProviderEvents.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        other.ProviderEvents.Add(new ProviderEvent { TenantId = h.Tenant, Provider = "razorpay", EventId = "evt_rls_forged", Type = "payment_link.paid", ReceivedAt = h.Clock.GetUtcNow() });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    /// <summary>No webhook body, signature or secret is ever logged, whatever happens to the webhook (raw log records, before any scrubbing).</summary>
    [Fact]
    public async Task NoWebhookBodySignatureOrSecretIsLogged()
    {
        await PendingLinkAsync(40_000);
        var logs = new List<string>();
        var client = Client(h.Reception, s => s.AddSingleton<ILoggerProvider>(new CaptureLogs(logs)));
        client.DefaultRequestHeaders.Remove("X-Test-User");
        var body = Paid(linkId: "plink_marker_body");
        var signature = Sign(RazorpayTestKeys.WebhookSecret, body);
        links.Fail = PaymentProviderError.Unavailable;
        await DeliverAsync(client, body, signature, "evt_log_1", RazorpayTestKeys.WebhookKey);
        await DeliverAsync(client, body, Sign("wrong", body), "evt_log_2");
        links.Fail = null;
        await DeliverAsync(client, body, signature, "evt_log_3");
        await DeliverAsync(client, "{ not json", Sign(RazorpayTestKeys.WebhookSecret, "{ not json"), "evt_log_4");
        Assert.NotEmpty(logs); // the capture works
        var all = string.Join('\n', logs);
        foreach (var secret in new[] { "plink_marker_body", "amount_paid", signature, Sign("wrong", body), RazorpayTestKeys.WebhookSecret, RazorpayTestKeys.KeySecret })
            Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
    }

    sealed class CaptureLogs(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(sink);
        public void Dispose() { }
        sealed class Logger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(';', pairs.Select(p => $"{p.Key}={p.Value}")) : "";
                lock (sink) sink.Add($"{formatter(state, exception)}|{values}|{exception}");
            }
        }
    }

    // ---- Settings, Billing, Razorpay ----

    /// <summary>The admin connects test keys; the answer and later reads show the mode, the key's last four and the webhook path, never a secret.</summary>
    [Fact]
    public async Task TheAdminConnectsRazorpayAndSecretsNeverComeBack()
    {
        await h.StepsAsync(nurse: false, billing: true);
        using var admin = Client(h.Admin, s => s.Replace(ServiceDescriptor.Singleton<IRazorpayCredentialStore>(
            new ConfigurationRazorpayCredentialStore(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = "Development" }))));
        var before = await admin.GetFromJsonAsync<JsonElement>(new Uri("/v1/settings/billing/razorpay", UriKind.Relative));
        Assert.False(before.GetProperty("connected").GetBoolean());

        var invalid = await admin.PutAsJsonAsync("/v1/settings/billing/razorpay", new { keyId = "key_123", keySecret = " ", webhookSecret = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(("format", "required", "required"), (errors.GetProperty("keyId")[0].GetString(), errors.GetProperty("keySecret")[0].GetString(), errors.GetProperty("webhookSecret")[0].GetString()));

        var saved = await admin.PutAsJsonAsync("/v1/settings/billing/razorpay", new { keyId = "rzp_test_Synth9876", keySecret = "synthetic-new-secret", webhookSecret = "synthetic-new-hook" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var text = await saved.Content.ReadAsStringAsync();
        var read = await admin.GetStringAsync(new Uri("/v1/settings/billing/razorpay", UriKind.Relative));
        foreach (var payload in new[] { text, read })
        {
            Assert.DoesNotContain("synthetic-new", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("Synth9876", payload, StringComparison.Ordinal); // only the last four
        }
        var status = JsonDocument.Parse(read).RootElement;
        Assert.Equal((true, "Test", "9876"), (status.GetProperty("connected").GetBoolean(), status.GetProperty("mode").GetString(), status.GetProperty("keyIdEnding").GetString()));
        Assert.Matches("^/v1/webhooks/razorpay/[0-9a-f]{64}$", status.GetProperty("webhookPath").GetString());

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "billing.razorpay.connect").ToListAsync();
        Assert.Equal([AuditOutcome.Rejected, AuditOutcome.Allowed], rows.OrderBy(r => r.OccurredAt).ThenBy(r => r.Id).Select(r => r.Outcome));
        Assert.All(rows, r => Assert.DoesNotContain("synthetic", r.Detail ?? "", StringComparison.Ordinal));

        // The front desk and a doctor have no practice configuration; with billing off the screen does not exist.
        foreach (var role in new[] { Role.Receptionist, Role.HospitalDoctor })
        {
            using var refused = Client(h.ByRole[role]);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.GetAsync(new Uri("/v1/settings/billing/razorpay", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PutAsJsonAsync("/v1/settings/billing/razorpay", new { keyId = "rzp_test_Synth1", keySecret = "s", webhookSecret = "w" })).StatusCode);
        }
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Billing, false, "practice", "Billing (synthetic test)");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri("/v1/settings/billing/razorpay", UriKind.Relative))).StatusCode);
    }

    /// <summary>Turning on Razorpay links needs a connected account (checked on the server).</summary>
    // SAFETY: mutation check: removed the connected-account check in BillingSettingsService.SaveAsync -> this test failed (saved without an account)
    [Fact]
    public async Task RazorpayLinksNeedAConnectedAccount()
    {
        var command = new SaveBillingSettings
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash, PaymentMethod.RazorpayLink], MaxDiscountPercent = 10,
            PaymentLinkExpiryMinutes = 30, Version = 0,
        };
        Assert.Equal("paymentMethods:razorpay_not_connected", await Refused(h.Get<IBillingSettings>(h.Admin, s => RazorpayTestKeys.Connect(s)).SaveAsync(command)));
        // No secret store can be built here (as on a host without Razorpay:KeyVaultUri outside Development): not connected, not a server error.
        Assert.Equal("paymentMethods:razorpay_not_connected", await Refused(h.Get<IBillingSettings>(h.Admin).SaveAsync(command)));
        Assert.Equal([PaymentMethod.Cash, PaymentMethod.RazorpayLink], (await h.Get<IBillingSettings>(h.Admin, s => RazorpayTestKeys.Connect(s, h.Tenant)).SaveAsync(command)).PaymentMethods);
    }
}
