using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Scheduling.Infrastructure;

public static class SchedulingModule
{
    /// <summary>Needs the Tenancy module (facilities, memberships), Identity (doctor names), Patients (patient names) and Audit (write events).</summary>
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SchedulingOptions>();
        services.AddLogging();
        services.TryAddSingleton<IBoardNotifier, NoBoardNotifier>(); // the host replaces it with SignalR (Medvya.Scheduling.Api)
        return services
            .AddDbContext<SchedulingDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<SchedulingContext>()
            .AddScoped<DoctorDayTransaction>()
            .AddScoped<IScheduleService, ScheduleService>()
            .AddScoped<IBookingService, BookingService>()
            .AddScoped<IFrontDeskService, FrontDeskService>()
            .AddScoped<IQueueService, QueueService>();
    }
}

/// <summary>Without a real-time host (tests, tools) nothing is pushed; boards still refetch on their own.</summary>
internal sealed class NoBoardNotifier : IBoardNotifier
{
    public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
