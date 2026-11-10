using System.Security.Cryptography;
using Fido2NetLib;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Platform.Infrastructure;

/// <summary>Console settings (configuration section "Platform").</summary>
public sealed class PlatformOptions
{
    /// <summary>
    /// The first owner, configured because nobody exists yet to invite them: their company sign-on issuer and subject (the Entra
    /// object id). Used only while the team is empty; they then register the very first key without a link.
    /// </summary>
    public BootstrapOwner? BootstrapOwner { get; set; }

    /// <summary>The console's WebAuthn relying party: its host name (RpId) and the exact origins the browser may report.</summary>
    public string RpId { get; set; } = "localhost";
    public string RpName { get; set; } = "Medvya Platform";
    public string[] Origins { get; set; } = [];
}

public sealed class BootstrapOwner
{
    public string? Issuer { get; set; }
    public string? Subject { get; set; }
    public string DisplayName { get; set; } = "Platform owner";
    public string Email { get; set; } = "owner@medvya.example";
}

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, string connectionString, PlatformOptions options)
    {
        if (options.Origins.Length == 0) throw new InvalidOperationException("Platform:Origins must list the console's origin(s) for WebAuthn.");
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<PlatformDbContext>(o => o.UseNpgsql(PostgresDataSources.For(connectionString)))
            .AddSingleton(options)
            .AddSingleton<IFido2>(new Fido2(new Fido2Configuration
            {
                RPID = options.RpId,
                RPName = options.RpName,
                Origins = new HashSet<string>(options.Origins, StringComparer.Ordinal),
            }))
            .AddScoped<IPlatformTeam, PlatformTeam>()
            .AddScoped<IPlatformAuditLog, PlatformAuditLog>()
            .AddScoped<IConsoleActions, ConsoleActions>()
            .AddScoped<IPlatformSignIn, PlatformSignIn>();
    }
}

static class PlatformAudit
{
    public static PlatformAuditEntry Entry(IPlatformCaller caller, DateTimeOffset now, string action, string resourceType, Guid? resourceId,
        string? reason = null, Guid? actor = null, Guid? subjectTenantId = null) => new()
    {
        SubjectTenantId = subjectTenantId,
        ActorMemberId = actor ?? caller.MemberId,
        Action = action,
        ResourceType = resourceType,
        ResourceId = resourceId,
        Reason = reason,
        OccurredAt = now,
        ClientIp = caller.ClientIp,
        CorrelationId = caller.CorrelationId,
    };

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public static (KeyLink Link, string Token) NewLink(Guid memberId, KeyLinkPurpose purpose, Guid issuedBy, DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new KeyLink { MemberId = memberId, Purpose = purpose, TokenHash = Hash(token), IssuedBy = issuedBy, IssuedAt = now, ExpiresAt = now + KeyLink.Lifetime }, token);
    }
}
