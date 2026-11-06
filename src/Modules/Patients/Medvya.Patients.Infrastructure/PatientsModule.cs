using Medvya.BuildingBlocks.Infrastructure;
using Medvya.BuildingBlocks.Security;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

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
            .AddDbContext<PatientsDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IPatientRegistry, PatientRegistry>()
            .AddScoped<IPatientMerges, PatientMerges>()
            .AddScoped<IPatientConsents, PatientConsents>()
            .AddScoped<IPatientDirectory, PatientDirectory>()
            .AddScoped<IPatientContacts, PatientContacts>()
            .AddNationalIdColumns(connectionString);
    }

    /// <summary>
    /// National-ID encryption for the Patients tables (MDV-P0-035): Key Vault from NationalIdProtection:KeyVaultKeyId, or in Development this
    /// machine's key; wrapped data keys in patients."DataKeys". Also used by the seeder and the tests, so every writer shares one setup.
    /// </summary>
    public static IServiceCollection AddNationalIdColumns(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton<IWrappedKeyStore>(new PatientsDataKeyStore(connectionString));
        services.TryAddSingleton<NationalIdColumns>();
        return services.AddNationalIdProtection();
    }

    /// <summary>The same setup outside a host (the seeder, tests): configuration from environment variables, as the API would read them.</summary>
    public static NationalIdColumns NationalIdColumnsFor(string connectionString, string environmentName) =>
        new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environmentName })
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddEnvironmentVariables().Build())
            .AddNationalIdColumns(connectionString)
            .BuildServiceProvider().GetRequiredService<NationalIdColumns>();
}
