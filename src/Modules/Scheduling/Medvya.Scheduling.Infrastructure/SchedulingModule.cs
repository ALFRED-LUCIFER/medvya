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
            .AddDbContext<SchedulingDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<SchedulingContext>()
            .AddScoped<DoctorDayTransaction>()
            .AddScoped<Medvya.Patients.Application.IPatientMergeParticipant, PatientMergeParticipant>()
            .AddScoped<Medvya.Tenancy.Application.IStaffOffboardingParticipant, DoctorOffboardingParticipant>()
            .AddScoped<IScheduleService, ScheduleService>()
            .AddScoped<IScheduleExceptionService, ScheduleExceptionService>()
            .AddScoped<IBookingService, BookingService>()
            .AddScoped<IFrontDeskService, FrontDeskService>()
            .AddScoped<IQueueService, QueueService>()
            .AddScoped<IOutboxDispatcher, OutboxDispatcher>();
    }
}

/// <summary>Without a real-time host (tests, tools) nothing is pushed; boards still refetch on their own.</summary>
internal sealed class NoBoardNotifier : IBoardNotifier
{
    public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
