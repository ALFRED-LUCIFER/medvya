using Medvya.Billing.Application;
using Azure.Security.KeyVault.Secrets;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.Billing.Infrastructure.Razorpay;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Medvya.Billing.Infrastructure;

/// <summary>Billing (P4): the services catalogue, doctor fees and billing settings (MDV-P4-002), legal numbering, and bills with their payments (MDV-P4-004).</summary>
public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<BillingDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IDocumentNumbering, DocumentNumbering>()
            .AddScoped<ICatalogue, Catalogue>()
            .AddScoped<IDoctorFees, DoctorFees>()
            .AddScoped<IBillingSettings, BillingSettingsService>()
            .AddScoped<IInvoices, Invoices>()
            .AddScoped<IRazorpayAccount, RazorpayAccount>()
            // Lazy: the credential store is built on first use (a payment link), never at start-up, so a host without Razorpay:KeyVaultUri still starts.
            .AddRazorpay();
    }

    /// <summary>
    /// Razorpay payment links (MDV-P4-007, ADR-023): each clinic's keys from the payments Key Vault (Razorpay:KeyVaultUri), or in
    /// Development from configuration; anywhere else without the vault, resolving the store fails closed.
    /// </summary>
    static IServiceCollection AddRazorpay(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRazorpayCredentialStore>(sp =>
        {
            var vault = sp.GetRequiredService<IConfiguration>()["Razorpay:KeyVaultUri"];
            return string.IsNullOrEmpty(vault)
                ? new ConfigurationRazorpayCredentialStore(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>())
                : new KeyVaultRazorpayCredentialStore(new SecretClient(new Uri(vault), AzureCredentials.Shared), sp.GetRequiredService<TimeProvider>());
        });
        // Razorpay asks for an answer within seconds; a webhook handler that waits longer than this has already lost.
        services.AddHttpClient<IPaymentLinks, RazorpayPaymentLinks>(c => (c.BaseAddress, c.Timeout) = (RazorpayPaymentLinks.BaseAddress, TimeSpan.FromSeconds(10)));
        return services;
    }
}
