using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.BuildingBlocks.Security;

/// <summary>The client's address and protocol behind a proxy (MDV-P0-007); shared by the API and the platform console host. Call UseForwardedHeaders first.</summary>
public static class ClientAddress
{
    /// <summary>
    /// The client address for the per-IP limits and the audit trail. A forged one would dodge limits and falsify the trail, so
    /// X-Forwarded-For is believed only where we know a proxy is in front:
    /// - On App Service (it sets WEBSITE_INSTANCE_ID; a client cannot): every request arrives through the platform front end, whose
    ///   addresses are not fixed, and the app cannot be reached around it. Only the last entry is read (ForwardLimit 1), the one the
    ///   front end added; anything a client wrote into the header itself sits further left and is ignored.
    /// - Elsewhere: only from proxies listed in ForwardedHeaders:KnownProxies; none listed (Development) means the header is ignored.
    /// </summary>
    public static IServiceCollection AddClientAddress(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            var appService = !string.IsNullOrEmpty(configuration["WEBSITE_INSTANCE_ID"]);
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear(); // both lists empty = every peer is believed, which only App Service's own front end can be
            foreach (var proxy in proxies) o.KnownProxies.Add(IPAddress.Parse(proxy));
            o.ForwardLimit = 1;
            // The protocol is always believed: App Service ends TLS in front of the app, so without it sign-in sends Entra an http redirect
            // address that is not registered. A forged protocol can only change generated links.
            o.ForwardedHeaders = appService || proxies.Length > 0 ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.XForwardedProto;
        });
}
