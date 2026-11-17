using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Medvya.Billing.Application;
using Medvya.Billing.Infrastructure;
using Medvya.Billing.Infrastructure.Razorpay;
using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P4-007 Razorpay adapter and credential stores against a fake HTTP handler and a fake Key Vault: no network, no database.</summary>
public class RazorpayPaymentLinksTests
{
    // Synthetic test-mode values only.
    const string KeyId = "rzp_test_Synth00001234", KeySecret = "keysecret-synthetic-test-only", WebhookSecret = "whsec-synthetic-test-only";
    static readonly Guid Tenant = Guid.Parse("7d1c6b3e-0000-4000-8000-00000000a001");
    static readonly string WebhookKey = new('a', 64);
    const string LinkJson = """{"id":"plink_Synth0001","short_url":"https://rzp.io/i/Synth01","status":"created","amount_paid":0,"currency":"INR","payments":null}""";

    sealed class FakeHandler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        readonly Queue<(HttpStatusCode, string)> _answers = new(answers);
        public List<(HttpMethod Method, string Path, string? Auth, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = _answers.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    static ConfigurationRazorpayCredentialStore DevStore(bool connected = true) => new(
        new ConfigurationBuilder().AddInMemoryCollection(connected
            ? new Dictionary<string, string?>
            {
                [$"Razorpay:Tenants:{Tenant}:KeyId"] = KeyId,
                [$"Razorpay:Tenants:{Tenant}:KeySecret"] = KeySecret,
                [$"Razorpay:Tenants:{Tenant}:WebhookSecret"] = WebhookSecret,
                [$"Razorpay:Tenants:{Tenant}:WebhookKey"] = WebhookKey,
            }
            : []).Build(),
        new HostingEnvironment { EnvironmentName = Environments.Development });

    static (RazorpayPaymentLinks Links, FakeHandler Handler, CapturingLogger<RazorpayPaymentLinks> Log) Adapter(bool connected = true, params (HttpStatusCode, string)[] answers)
    {
        var handler = new FakeHandler(answers);
        var log = new CapturingLogger<RazorpayPaymentLinks>();
        return (new RazorpayPaymentLinks(new HttpClient(handler) { BaseAddress = RazorpayPaymentLinks.BaseAddress }, DevStore(connected), log), handler, log);
    }

    static PaymentLinkRequest Request(DateTimeOffset expires) => new(new Money(123_450, Currency.Inr), "a1b2c3d4-attempt-1", "Bill INV/26-27/00342, Sunrise Clinic", expires,
        new Dictionary<string, string> { ["tenant"] = Tenant.ToString(), ["invoice"] = "inv-1", ["payment"] = "pay-1" });

    [Fact]
    public async Task CreateSendsPaiseTheReferenceNoNotificationTheExpiryAndNotesWithBasicAuth()
    {
        var (links, handler, _) = Adapter(answers: (HttpStatusCode.OK, LinkJson));
        var expires = new DateTimeOffset(2026, 11, 16, 10, 30, 0, TimeSpan.Zero);

        var link = await links.CreateAsync(Tenant, Request(expires), CancellationToken.None);

        Assert.Equal(new PaymentLink("plink_Synth0001", "https://rzp.io/i/Synth01", PaymentLinkStatus.Created), link);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "/v1/payment_links"), (sent.Method, sent.Path));
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")), sent.Auth);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        Assert.Equal(123_450, body.GetProperty("amount").GetInt64());
        Assert.Equal("INR", body.GetProperty("currency").GetString());
        Assert.Equal("a1b2c3d4-attempt-1", body.GetProperty("reference_id").GetString());
        Assert.Equal("Bill INV/26-27/00342, Sunrise Clinic", body.GetProperty("description").GetString());
        Assert.Equal(expires.ToUnixTimeSeconds(), body.GetProperty("expire_by").GetInt64());
        Assert.False(body.GetProperty("notify").GetProperty("sms").GetBoolean());
        Assert.False(body.GetProperty("notify").GetProperty("email").GetBoolean());
        Assert.Equal("inv-1", body.GetProperty("notes").GetProperty("invoice").GetString()); // note keys are sent as given
        Assert.False(body.TryGetProperty("customer", out _)); // Razorpay never gets the patient's name or number from us
    }

    [Fact]
    public async Task FetchReturnsTheStatusTheAmountPaidAndOnlyCapturedPayments()
    {
        const string paid = """{"id":"plink_Synth0001","short_url":"https://rzp.io/i/Synth01","status":"paid","amount_paid":123450,"currency":"INR","payments":[{"payment_id":"pay_Failed01","status":"failed"},{"payment_id":"pay_Synth0001","status":"captured"}]}""";
        var (links, handler, _) = Adapter(answers: (HttpStatusCode.OK, paid));

        var state = await links.FetchAsync(Tenant, "plink_Synth0001", CancellationToken.None);

        Assert.Equal(PaymentLinkStatus.Paid, state.Status);
        Assert.Equal(new Money(123_450, Currency.Inr), state.AmountPaid);
        Assert.Equal(["pay_Synth0001"], state.CapturedPaymentIds);
        Assert.Equal((HttpMethod.Get, "/v1/payment_links/plink_Synth0001"), (handler.Requests[0].Method, handler.Requests[0].Path));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PaymentProviderError.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest, PaymentProviderError.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, PaymentProviderError.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, PaymentProviderError.Unavailable)]
    public async Task ErrorsMapToTypedErrorsAndACreateIsNeverRetried(HttpStatusCode status, PaymentProviderError expected)
    {
        var (links, handler, _) = Adapter(answers: (status, """{"error":{"code":"BAD_REQUEST_ERROR","description":"echo a1b2c3d4-attempt-1"}}"""));

        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => links.CreateAsync(Tenant, Request(DateTimeOffset.UtcNow.AddHours(1)), CancellationToken.None));

        Assert.Equal((expected, (int)status), (e.Error, e.Status));
        Assert.DoesNotContain("attempt-1", e.Message); // the provider's text never reaches our exception
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AFetchIsRetriedOnceAfterAProviderFault()
    {
        var (links, handler, _) = Adapter(answers: [(HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.OK, LinkJson)]);
        Assert.Equal(PaymentLinkStatus.Created, (await links.FetchAsync(Tenant, "plink_Synth0001", CancellationToken.None)).Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AClinicWithoutKeysIsNotConnectedAndNothingIsSent()
    {
        var (links, handler, _) = Adapter(connected: false);
        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => links.FetchAsync(Tenant, "plink_Synth0001", CancellationToken.None));
        Assert.Equal(PaymentProviderError.NotConnected, e.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancelReturnsCancelled()
    {
        var (links, handler, _) = Adapter(answers: (HttpStatusCode.OK, LinkJson.Replace("\"created\"", "\"cancelled\"", StringComparison.Ordinal)));
        Assert.Equal(PaymentLinkStatus.Cancelled, await links.CancelAsync(Tenant, "plink_Synth0001", CancellationToken.None));
        Assert.Equal((HttpMethod.Post, "/v1/payment_links/plink_Synth0001/cancel"), (handler.Requests[0].Method, handler.Requests[0].Path));
    }

    [Fact]
    public async Task ACancelRefusedBecauseTheLinkWasPaidReportsPaid()
    {
        var (links, handler, _) = Adapter(answers: [(HttpStatusCode.BadRequest, """{"error":{"code":"BAD_REQUEST_ERROR"}}"""), (HttpStatusCode.OK, LinkJson.Replace("\"created\"", "\"paid\"", StringComparison.Ordinal))]);
        Assert.Equal(PaymentLinkStatus.Paid, await links.CancelAsync(Tenant, "plink_Synth0001", CancellationToken.None));
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task LogsCarryNoKeySecretRequestTextOrProviderText()
    {
        var (links, _, log) = Adapter(answers: [(HttpStatusCode.BadRequest, """{"error":{"description":"Sunrise Clinic"}}"""), (HttpStatusCode.Unauthorized, "{}"), (HttpStatusCode.OK, LinkJson)]);
        await Assert.ThrowsAsync<PaymentProviderException>(() => links.CreateAsync(Tenant, Request(DateTimeOffset.UtcNow.AddHours(1)), CancellationToken.None));
        await Assert.ThrowsAsync<PaymentProviderException>(() => links.FetchAsync(Tenant, "plink_Synth0001", CancellationToken.None));
        await links.FetchAsync(Tenant, "plink_Synth0001", CancellationToken.None);

        Assert.NotEmpty(log.Entries);
        var all = string.Join('\n', log.Entries.Select(e => e.Message));
        foreach (var leak in new[] { KeySecret, WebhookSecret, Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}")), "Sunrise Clinic", "INV/26-27", "attempt-1" })
            Assert.DoesNotContain(leak, all);
    }

    [Fact]
    public void CredentialsNeverPrintTheirSecrets()
    {
        var text = new RazorpayCredentials(KeyId, KeySecret, WebhookSecret, WebhookKey).ToString();
        Assert.DoesNotContain(KeySecret, text);
        Assert.DoesNotContain(WebhookSecret, text);
        Assert.Equal("Razorpay Test key …1234", text);
        Assert.Equal(RazorpayMode.Live, new RazorpayCredentials("rzp_live_Synth1", "s", "w", WebhookKey).Mode);
    }

    // A Key Vault stand-in: the SDK's clients are mockable by design (virtual members, protected constructor).
    sealed class FakeVault : SecretClient
    {
        public Dictionary<string, string> Secrets { get; } = [];
        public int Reads { get; private set; }

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version, SecretContentType? outContentType, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Secrets.TryGetValue(name, out var v)
                ? Task.FromResult(Response.FromValue(new KeyVaultSecret(name, v), null!))
                : throw new RequestFailedException(404, "SecretNotFound");
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            Secrets[name] = value;
            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), null!));
        }
    }

    [Fact]
    public async Task KeyVaultStoreKeepsTheWebhookKeyFindsTheTenantByItAndCachesReads()
    {
        var (vault, clock) = (new FakeVault(), new ManualClock(DateTimeOffset.UnixEpoch));
        var store = new KeyVaultRazorpayCredentialStore(vault, clock);
        Assert.Null(await store.GetAsync(Tenant, CancellationToken.None));

        var first = await store.SaveAsync(Tenant, KeyId, KeySecret, WebhookSecret, CancellationToken.None);
        Assert.True(RazorpayCredentials.IsWebhookKey(first.WebhookKey));
        Assert.Equal(Tenant.ToString(), vault.Secrets[$"razorpay-hook-{first.WebhookKey}"]);
        Assert.Contains(KeySecret, vault.Secrets[$"razorpay-{Tenant}"], StringComparison.Ordinal);
        var again = await store.SaveAsync(Tenant, KeyId, "rotated-synthetic", WebhookSecret, CancellationToken.None);
        Assert.Equal(first.WebhookKey, again.WebhookKey); // the URL configured in Razorpay stays valid

        var fresh = new KeyVaultRazorpayCredentialStore(vault, clock); // another instance: reads Key Vault once, then its cache
        var reads = vault.Reads;
        for (var i = 0; i < 3; i++)
        {
            var found = await fresh.FindByWebhookKeyAsync(first.WebhookKey, CancellationToken.None);
            Assert.Equal((Tenant, "rotated-synthetic"), (found!.Value.TenantId, found.Value.Credentials.KeySecret));
        }
        Assert.Equal(reads + 2, vault.Reads);
        clock.Advance(TimeSpan.FromMinutes(6));
        await fresh.FindByWebhookKeyAsync(first.WebhookKey, CancellationToken.None);
        Assert.Equal(reads + 4, vault.Reads);

        Assert.Null(await fresh.FindByWebhookKeyAsync(new string('b', 64), CancellationToken.None));
        var beforeJunk = vault.Reads;
        Assert.Null(await fresh.FindByWebhookKeyAsync("../razorpay-" + Tenant, CancellationToken.None)); // malformed: never reaches Key Vault
        Assert.Equal(beforeJunk, vault.Reads);
    }

    [Fact]
    public async Task DevelopmentStoreReadsUserSecretsAndRefusesOutsideDevelopment()
    {
        var found = await DevStore().FindByWebhookKeyAsync(WebhookKey, CancellationToken.None);
        Assert.Equal((Tenant, KeySecret), (found!.Value.TenantId, found.Value.Credentials.KeySecret));
        Assert.Null(await DevStore().FindByWebhookKeyAsync(new string('b', 64), CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => new ConfigurationRazorpayCredentialStore(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = Environments.Staging }));
    }

    [Fact]
    public void TheModuleResolvesTheAdapterWithTheDevelopmentStore()
    {
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Development })
            .AddBillingModule("Host=localhost;Database=unused")
            .BuildServiceProvider();
        Assert.IsType<RazorpayPaymentLinks>(sp.GetRequiredService<IPaymentLinks>());
        Assert.IsType<ConfigurationRazorpayCredentialStore>(sp.GetRequiredService<IRazorpayCredentialStore>());
    }
}
