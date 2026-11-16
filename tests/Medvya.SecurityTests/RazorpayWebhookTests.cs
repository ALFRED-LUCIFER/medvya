using System.Security.Cryptography;
using System.Text;
using Medvya.Billing.Application;

namespace Medvya.SecurityTests;

/// <summary>MDV-P4-007: a webhook counts only when its signature matches the clinic's webhook secret (ADR-023). Synthetic values only.</summary>
public class RazorpayWebhookTests
{
    const string Secret = "whsec-synthetic-test-only";
    static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"entity":"event","account_id":"acc_Synthetic01","event":"payment_link.paid","contains":["payment_link","payment"],"payload":{"payment_link":{"entity":{"id":"plink_Synth0001","status":"paid","amount_paid":120000}},"payment":{"entity":{"id":"pay_Synth0001","status":"captured"}}},"created_at":1794787200}""");

    static string Sign(byte[] body, string secret) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    [Fact]
    public void AValidSignatureIsAccepted()
    {
        Assert.True(RazorpayWebhook.IsSignatureValid(Body, Sign(Body, Secret), Secret));
        Assert.True(RazorpayWebhook.IsSignatureValid(Body, Sign(Body, Secret).ToUpperInvariant(), Secret)); // hex case does not matter
    }

    [Fact]
    public void ATamperedBodyIsRefused()
    {
        var tampered = (byte[])Body.Clone();
        tampered[^3] ^= 1;
        Assert.False(RazorpayWebhook.IsSignatureValid(tampered, Sign(Body, Secret), Secret));
    }

    [Fact]
    public void ASignatureMadeWithAnotherSecretIsRefused() =>
        Assert.False(RazorpayWebhook.IsSignatureValid(Body, Sign(Body, "another-clinics-secret"), Secret));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex-at-all")]
    public void AMissingOrMalformedSignatureIsRefused(string? header) => Assert.False(RazorpayWebhook.IsSignatureValid(Body, header, Secret));

    [Fact]
    public void ASignatureOfAnotherLengthIsRefused()
    {
        var good = Sign(Body, Secret);
        Assert.False(RazorpayWebhook.IsSignatureValid(Body, good[..32], Secret)); // a prefix of the right value
        Assert.False(RazorpayWebhook.IsSignatureValid(Body, good + "00", Secret));
    }

    [Fact]
    public void AnEmptySecretNeverVerifies() => Assert.False(RazorpayWebhook.IsSignatureValid(Body, Sign(Body, ""), ""));

    [Fact]
    public void AVerifiedBodyGivesTheEventAndTheLinkToFetch()
    {
        var e = RazorpayWebhook.Parse(Body, "evt_Synth0001");
        Assert.Equal(new RazorpayWebhookEvent("evt_Synth0001", "payment_link.paid", "plink_Synth0001"), e);
    }

    [Fact]
    public void AnEventWithoutAPaymentLinkHasNoLinkId()
    {
        var failed = """{"event":"payment.failed","payload":{"payment":{"entity":{"id":"pay_Synth0002","status":"failed"}}}}"""u8;
        Assert.Equal(new RazorpayWebhookEvent("evt_Synth0002", "payment.failed", null), RazorpayWebhook.Parse(failed, "evt_Synth0002"));
    }

    [Theory]
    [InlineData("""{"event":"payment_link.paid"}""", null)] // no event id header: cannot be de-duplicated
    [InlineData("""{"payload":{}}""", "evt_1")]
    [InlineData("""{"event":42}""", "evt_1")]
    [InlineData("""[]""", "evt_1")]
    [InlineData("""{not json""", "evt_1")]
    public void AnUnusableBodyParsesToNothing(string body, string? eventId) => Assert.Null(RazorpayWebhook.Parse(Encoding.UTF8.GetBytes(body), eventId));
}
