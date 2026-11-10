using Azure.Core;
using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Infrastructure;

public static class EmailSetup
{
    /// <summary>Email:Provider picks the sender: "Smtp" (default; SendGrid's relay in the cloud, smtp4dev locally) or "Acs" (Azure Communication Services).</summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration, Func<TokenCredential> credential)
    {
        services.Configure<SmtpOptions>(configuration.GetSection("Email:Smtp")).Configure<AcsEmailOptions>(configuration.GetSection("Email:Acs"));
        return (configuration["Email:Provider"] ?? "Smtp") switch
        {
            "Smtp" => services.AddSingleton<IEmailSender, SmtpEmailSender>(),
            "Acs" => services.AddSingleton<IEmailSender>(sp => new AcsEmailSender(sp.GetRequiredService<IOptions<AcsEmailOptions>>(), credential())),
            var other => throw new InvalidOperationException($"Email:Provider '{other}' is not supported (use Smtp or Acs)."),
        };
    }
}
