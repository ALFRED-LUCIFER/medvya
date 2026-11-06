using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Notifications.Application;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Notifications.Infrastructure;

public static class NotificationsModule
{
    /// <summary>
    /// Needs Scheduling (events), Patients (contacts), Tenancy (facilities) and an IEmailSender. Consent is denied unless a module
    /// registers an <see cref="IReminderConsent"/> (MDV-P1-019): the last registration wins, so register it after this call.
    /// </summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString, bool smsSink)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.TryAddSingleton<IReminderConsent, DenyAllReminderConsent>();
        // smsSink: the host asks for the local sink (SMS to the mail sink) in Development; otherwise it fails closed until a provider is chosen (Q-03).
        if (smsSink) services.TryAddScoped<ISmsGateway, SinkSmsGateway>();
        else services.TryAddScoped<ISmsGateway, UnconfiguredSmsGateway>();
        return services
            .AddDbContext<NotificationsDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IAppointmentSubscriber, AppointmentNotifier>()
            .AddScoped<INotificationSender, NotificationSender>()
            .AddScoped<IReceiptProcessor, ReceiptProcessor>()
            .AddScoped<INotificationQueries, NotificationQueries>()
            .AddScoped<IStaffNotifications, StaffNotifications>()
            .AddScoped<INotificationPreferences, NotificationPreferences>()
            .AddScoped<IComplianceReminders, ComplianceReminders>();
    }
}
