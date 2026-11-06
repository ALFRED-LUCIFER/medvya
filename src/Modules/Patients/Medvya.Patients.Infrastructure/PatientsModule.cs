using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Patients.Infrastructure;

public static class PatientsModule
{
    /// <summary>
    /// Needs the Audit module (every read and write is audited).
    /// </summary>
    public static IServiceCollection AddPatientsModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<PatientsDbContext>((sp, o) => o.UseNpgsql(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IPatientRegistry, PatientRegistry>()
            .AddScoped<IPatientMerges, PatientMerges>()
            .AddScoped<IPatientConsents, PatientConsents>()
            .AddScoped<IPatientDirectory, PatientDirectory>()
            .AddScoped<IPatientContacts, PatientContacts>();
    }
}
