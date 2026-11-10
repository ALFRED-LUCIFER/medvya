using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Prescribing.Infrastructure;

public static class PrescribingModule
{
    /// <summary>Needs Tenancy (role, clinical access, the tenant's country), Audit, Encounters (the consultation), Patients and Identity (names on the slip).</summary>
    public static IServiceCollection AddPrescribingModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        // The QR verify link opens the clinic app, whose origin invitation emails already use (configured, never the request's Host header).
        services.AddOptions<PrescribingOptions>().Configure<IServiceProvider>((o, sp) =>
            o.VerifyBaseUrl ??= sp.GetService<IConfiguration>() is { } c ? c["Prescribing:VerifyBaseUrl"] ?? c["Invitations:LinkBaseUrl"] : null);
        return services
            .AddDbContext<PrescribingDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IFormulary, FormularyService>()
            .AddScoped<IOrders, OrderService>()
            .AddScoped<IPrescriptions, PrescriptionService>()
            .AddScoped<IPrescriptionVerification, PrescriptionVerification>()
            .AddScoped<IReferrals, ReferralService>()
            .AddScoped<Medvya.Encounters.Application.ITimelineSource, OrderTimelineSource>()
            .AddScoped<Medvya.Patients.Application.IPatientMergeParticipant, OrderMergeParticipant>();
    }
}
