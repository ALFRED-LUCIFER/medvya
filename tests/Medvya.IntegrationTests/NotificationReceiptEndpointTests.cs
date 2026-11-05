using System.Net;
using System.Security.Cryptography;
using System.Text;
using Host.Api.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Medvya.IntegrationTests;

/// <summary>The delivery receipt webhook is public, so its signature is the only credential (MDV-P1-033). Synthetic data only.</summary>
public class NotificationReceiptEndpointTests
{
    static string Sign(string secret, string body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void OnlyTheExactSignatureOfTheBodyIsAccepted()
    {
        var body = """{"ref":"a.b","state":"delivered"}""";
        var bytes = Encoding.UTF8.GetBytes(body);
        Assert.True(NotificationEndpoints.SignatureValid("s3cret", bytes, Sign("s3cret", body)));
        Assert.True(NotificationEndpoints.SignatureValid("s3cret", bytes, Sign("s3cret", body).ToUpperInvariant()));
        Assert.False(NotificationEndpoints.SignatureValid("s3cret", bytes, Sign("other", body)));
        Assert.False(NotificationEndpoints.SignatureValid("s3cret", bytes, Sign("s3cret", body + " ")));
        Assert.False(NotificationEndpoints.SignatureValid("s3cret", bytes, null));
        Assert.False(NotificationEndpoints.SignatureValid("s3cret", bytes, ""));
    }

    [Fact]
    public void WithoutAConfiguredSecretNothingIsAccepted()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        Assert.False(NotificationEndpoints.SignatureValid(null, body, Sign("", "{}")));
        Assert.False(NotificationEndpoints.SignatureValid("", body, Sign("", "{}")));
    }

    [Fact]
    public async Task ARequestWithoutAValidSignatureIsRefusedBeforeAnythingIsRead()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var unsigned = new StringContent("""{"ref":"x","state":"delivered"}""", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/v1/notifications/receipts", unsigned)).StatusCode);
    }

    [Fact]
    public async Task StaffEndpointsNeedSignIn()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/v1/appointments/{Guid.NewGuid()}/notifications?facilityId={Guid.NewGuid()}");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect or HttpStatusCode.Found, response.StatusCode.ToString());
    }
}
