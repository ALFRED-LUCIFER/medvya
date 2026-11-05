using Azure.Core;
using Host.Api.Middleware;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-032: email goes through SMTP (SendGrid) or Azure Communication Services, chosen by configuration; either fails closed.</summary>
public sealed class EmailProviderTests
{
    sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("no token in tests");
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new InvalidOperationException("no token in tests");
    }

    static IEmailSender Sender(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build();
        return new ServiceCollection().AddEmailSender(config, () => new NoCredential()).BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    static readonly EmailMessage Mail = new("someone@example.com", "Subject", "Body");

    [Fact]
    public void SmtpIsTheDefault() => Assert.IsType<SmtpEmailSender>(Sender());

    [Fact]
    public void AcsIsChosenByConfiguration() => Assert.IsType<AcsEmailSender>(Sender(("Email:Provider", "Acs")));

    [Fact]
    public void AnUnknownProviderIsRefusedAtStartup() =>
        Assert.Throws<InvalidOperationException>(() => Sender(("Email:Provider", "Mailgun")));

    [Fact]
    public async Task AcsWithoutEndpointOrSenderSendsNothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(("Email:Provider", "Acs"), ("Email:Acs:Endpoint", "https://x.communication.azure.com")).SendAsync(Mail));
        Assert.Contains("Email:Acs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmtpWithoutHostOrSenderSendsNothing() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sender(("Email:Smtp:Host", "smtp.example.com")).SendAsync(Mail));
}
