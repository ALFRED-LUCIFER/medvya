using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Patients.Infrastructure;

public static class PatientsModule
{
    /// <summary>
    /// Needs the Audit module (every read and write is audited) and a column master key provider registered by the host
    /// (<see cref="ColumnEncryption"/>); without one, encrypted identifiers cannot be written or read.
    /// </summary>
    public static IServiceCollection AddPatientsModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        var encrypted = WithColumnEncryption(connectionString);
        return services
            .AddDbContext<PatientsDbContext>((sp, o) => o.UseSqlServer(encrypted).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IPatientRegistry, PatientRegistry>()
            .AddScoped<IPatientDirectory, PatientDirectory>();
    }

    /// <summary>Only this module's connections pay for Always Encrypted (parameter-encryption round trips).</summary>
    public static string WithColumnEncryption(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { ColumnEncryptionSetting = SqlConnectionColumnEncryptionSetting.Enabled }.ConnectionString;
}
