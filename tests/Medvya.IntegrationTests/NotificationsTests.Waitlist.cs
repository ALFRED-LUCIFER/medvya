using Medvya.Notifications.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

// MDV-P1-027: the offer message.
public sealed partial class NotificationsTests
{
    [Fact]
    public async Task AWaitlistOfferMessagesThePatientTheSlotTimeAndNothingClinical()
    {
        smsGateway.Sent.Clear();
        dlt["WaitlistOffer.hi"] = "1107000000000000002";
        Rebuild(denyAll: false);
        var entry = await InScope(sp => sp.GetRequiredService<IWaitlistService>().AddAsync(new AddToWaitlist { FacilityId = h.Facility, PatientId = h.Patient, ReasonCategory = ReasonCategory.ChronicReview }));
        await InScope(sp => sp.GetRequiredService<IWaitlistService>().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = Tuesday(10) }));
        await RunJobAsync();

        Assert.All(await RowsAsync(entry.Id), r => Assert.Equal((NotificationKind.WaitlistOffer, NotificationStatus.Sent, Tuesday(10)), (r.Kind, r.Status, r.AppointmentStart)));
        var sms = Assert.Single(smsGateway.Sent);
        Assert.Equal("1107000000000000002", sms.TemplateId); // a registered DLT template
        foreach (var body in email.Sent.Select(m => m.Subject + m.Body).Append(sms.Text))
        {
            Assert.Contains("Adyar", body);
            foreach (var secret in new[] { "Meera", "000001", "chest", "Rao", "example.test" }) Assert.DoesNotContain(secret, body);
        }

        // A slot that has started before the message is due is never announced.
        await InScope(sp => sp.GetRequiredService<IWaitlistService>().DeclineAsync(entry.Id));
        await InScope(sp => sp.GetRequiredService<IWaitlistService>().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = Tuesday(11) }));
        h.Clock.Advance(TimeSpan.FromDays(2));
        email.Sent.Clear();
        await RunJobAsync();
        Assert.Empty(email.Sent);
        Assert.Contains(await RowsAsync(entry.Id), r => r.Status == NotificationStatus.Suppressed && r.FailureCode == "visit_started");
    }
}
