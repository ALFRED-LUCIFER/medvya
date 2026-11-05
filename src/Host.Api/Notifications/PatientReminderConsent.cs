using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;

namespace Host.Api.Notifications;

/// <summary>
/// The patient's reminder consent for a channel (MDV-P1-019), asked through the Patients contract before every send. Lives in the
/// host because Notifications and Patients never reference each other. No receipt for the channel means no message.
/// </summary>
public sealed class PatientReminderConsent(IPatientConsents consents) : IReminderConsent
{
    public Task<bool> AllowsAsync(Guid patientId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        consents.IsGrantedAsync(patientId, ConsentType.Reminders, channel == NotificationChannel.Email ? ConsentChannel.Email : ConsentChannel.Sms, cancellationToken);
}
