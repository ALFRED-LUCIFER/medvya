using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Medvya.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-032/033 on real SQL Server: confirmations and 24 h / 2 h reminders from the outbox, consent checked at send time, change
/// handling that cancels and never doubles, retries with backoff, DLT-registered SMS only, delivery receipts, tenant isolation.
/// Synthetic data only.
/// </summary>
public sealed partial class NotificationsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly FakeEmail email = new();
    readonly FakeSms smsGateway = new();
    readonly Consent consent = new();
    ServiceProvider? provider;
    Guid actingUser;
    readonly Dictionary<string, string> dlt = [];

    sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("smtp down for meera.iyer@example.test");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    sealed class FakeSms : ISmsGateway
    {
        public List<SmsMessage> Sent { get; } = [];
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    sealed class Consent : IReminderConsent
    {
        public bool Allow { get; set; } = true;
        public Task<bool> AllowsAsync(Guid patientId, NotificationChannel channel, CancellationToken cancellationToken = default) => Task.FromResult(Allow);
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    sealed class Terms : Medvya.Tenancy.Application.ITermsAcceptance
    {
        public bool Accepted { get; set; } = true;
        public Task<bool> IsSatisfiedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Accepted);
        public Task<Medvya.Tenancy.Application.TermsStatus> StatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AcceptAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    readonly Terms terms = new();

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using var patients = sql.Patients(h.Tenant);
        foreach (var p in await patients.Patients.ToListAsync()) { p.Email = "meera.iyer@example.test"; p.PreferredLanguage = "hi"; }
        await patients.SaveChangesAsync();
        actingUser = h.Reception;
        foreach (var group in new[] { "Confirmation", "Rescheduled", "Cancelled", "Reminder" })
            foreach (var lang in new[] { "en", "hi", "ar" }) dlt[$"{group}.{lang}"] = $"1107{group.Length:D2}{lang.Length}0000001";
        Rebuild(denyAll: false);
    }

    public async Task DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        await h.CloseAsync();
    }

    /// <summary>The module as the host builds it, with fakes in front of the providers. With <paramref name="denyAll"/> the module's own consent default applies.</summary>
    void Rebuild(bool denyAll, Guid? inTenant = null, Guid? user = null, bool realConsent = false)
    {
        provider?.Dispose();
        var services = new ServiceCollection()
            .AddSingleton<ICurrentTenant>(new PostgresFixture.FixedTenant(inTenant ?? h.Tenant))
            .AddSingleton<ICurrentUser>(new FixedUser(user ?? actingUser))
            .AddSingleton<TimeProvider>(h.Clock)
            .AddSingleton<ICurrentFacility>(new PatientRegistryTests.FixedFacility(h.Facility))
            .AddSingleton<Medvya.Tenancy.Application.ILogoStore>(h.Logos)
            .AddSingleton<IEmailSender>(email)
            .AddSingleton<ISmsGateway>(smsGateway)
            .AddSingleton(h.NationalIds); // the fixture's national-ID setup (this harness has no host configuration)
        if (!denyAll && !realConsent) services.AddSingleton<IReminderConsent>(consent);
        services.AddAuditModule(h.ConnectionString).AddTenancyModule(h.ConnectionString).AddIdentityModule(h.ConnectionString)
            .AddPatientsModule(h.ConnectionString).AddSchedulingModule(h.ConnectionString).AddNotificationsModule(h.ConnectionString, smsSink: false);
        if (realConsent) services.AddScoped<IReminderConsent, Medvya.Notifications.Api.PatientReminderConsent>(); // as the host registers it
        services.AddSingleton<Medvya.Tenancy.Application.ITermsAcceptance>(terms); // the real one is tested in TermsAcceptanceTests
        services.Configure<NotificationOptions>(o =>
        {
            o.Sms.DefaultSender = "ADYCLN";
            foreach (var (k, v) in dlt) o.Sms.DltTemplateIds[k] = v;
        });
        provider = services.BuildServiceProvider();
    }

    async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>What the host's job does for one tenant: publish the outbox, then send what is due.</summary>
    async Task RunJobAsync()
    {
        await InScope(async sp =>
        {
            await sp.GetRequiredService<IOutboxDispatcher>().DispatchAsync();
            await sp.GetRequiredService<INotificationSender>().SendDueAsync();
            return 0;
        });
    }

    async Task<List<Notification>> RowsAsync(Guid appointmentId)
    {
        await using var db = sql.Notifications(h.Tenant);
        return (await db.Notifications.Where(n => n.AppointmentId == appointmentId).ToListAsync()).OrderBy(n => n.Kind).ThenBy(n => n.Channel).ToList();
    }

    static DateTimeOffset Tuesday(int hour) => At(hour).AddDays(1);

    Task<AppointmentView> BookTuesdayAsync(int hour = 10) => h.BookAsync(0, start: Tuesday(hour));

    [Fact]
    public async Task BookingQueuesAConfirmationAndBothRemindersAndSendsTheConfirmationAtOnce()
    {
        var visit = await BookTuesdayAsync();
        await RunJobAsync();

        var rows = await RowsAsync(visit.Id);
        Assert.Equal(6, rows.Count); // confirmation + 24 h + 2 h, on email and SMS
        Assert.All(rows.Where(r => r.Kind == NotificationKind.Confirmation), r => Assert.Equal(NotificationStatus.Sent, r.Status));
        Assert.Equal(Tuesday(10).AddHours(-24), rows.First(r => r.Kind == NotificationKind.Reminder24h).SendAt);
        Assert.Equal(Tuesday(10).AddHours(-2), rows.First(r => r.Kind == NotificationKind.Reminder2h).SendAt);
        Assert.All(rows.Where(r => r.Kind != NotificationKind.Confirmation), r => Assert.Equal(NotificationStatus.Queued, r.Status));
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task RemindersGoOutAtTheirTimeAndNeverEarly()
    {
        await BookTuesdayAsync();
        await RunJobAsync();
        email.Sent.Clear();

        h.Clock.Advance(TimeSpan.FromHours(1)); // Monday 09:00 IST: the 24 h reminder is at Monday 10:00
        await RunJobAsync();
        Assert.Empty(email.Sent);

        h.Clock.Advance(TimeSpan.FromHours(2)); // Monday 11:00
        await RunJobAsync();
        Assert.Single(email.Sent);
        Assert.Equal("Adyar (synthetic): अपॉइंटमेंट की याद", email.Sent[0].Subject); // the patient's language
    }

    [Fact]
    public async Task MessagesCarryTheClinicDateAndTimeAndNothingElse()
    {
        smsGateway.Sent.Clear();
        dlt["Confirmation.hi"] = "1107000000000000001";
        Rebuild(denyAll: false);
        var visit = await BookTuesdayAsync();
        await RunJobAsync();

        var text = Assert.Single(email.Sent).Body;
        Assert.Contains("Adyar (synthetic)", text);
        Assert.Contains("बजे", text);
        var sms = Assert.Single(smsGateway.Sent);
        Assert.Equal("1107000000000000001", sms.TemplateId);
        Assert.Equal("ADYCLN", sms.Sender);
        foreach (var body in email.Sent.Select(m => m.Subject + m.Body).Append(sms.Text))
        {
            Assert.DoesNotContain("Meera", body); // patient name
            Assert.DoesNotContain("000001", body); // MRN
            Assert.DoesNotContain("chest", body); // the visit reason
            Assert.DoesNotContain("Rao", body); // doctor
            Assert.DoesNotContain("example.test", body); // address
        }
        Assert.NotEmpty(await RowsAsync(visit.Id));
    }

    [Fact]
    public async Task NothingIsSentWithoutConsentAndWithdrawingStopsQueuedReminders()
    {
        consent.Allow = false;
        var visit = await BookTuesdayAsync();
        await RunJobAsync();

        Assert.Empty(email.Sent);
        Assert.Empty(smsGateway.Sent);
        Assert.All((await RowsAsync(visit.Id)).Where(r => r.Kind == NotificationKind.Confirmation), r => Assert.Equal((NotificationStatus.Suppressed, "no_consent"), (r.Status, r.FailureCode)));

        // Consent given, then withdrawn before a reminder is due: the check happens at send time.
        consent.Allow = true;
        var second = await h.BookAsync(0, patient: h.SecondPatient, start: Tuesday(11));
        await RunJobAsync();
        Assert.Single(email.Sent);
        consent.Allow = false;
        email.Sent.Clear();
        h.Clock.Advance(TimeSpan.FromHours(4)); // Monday 12:00: the 24 h reminder of the 11:00 visit was due at 11:00
        await RunJobAsync();
        Assert.Empty(email.Sent);
        Assert.Contains(await RowsAsync(second.Id), r => r.Kind == NotificationKind.Reminder24h && r.Status == NotificationStatus.Suppressed);
    }

    /// <summary>MDV-P1-019 with the host's real consent: a reminder needs a grant for its channel; a withdrawal stops it. Mutation check: make the adapter return true and the first assertions fail.</summary>
    [Fact]
    public async Task ARemindersGoesOnlyToAChannelThePatientConsentedTo()
    {
        Rebuild(denyAll: false, realConsent: true);
        async Task Decide(ConsentChannel channel, bool granted)
        {
            await using var db = sql.Patients(h.Tenant);
            db.ConsentReceipts.Add(new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Type = ConsentType.Reminders, Channel = channel,
                PolicyVersion = ConsentPolicy.CurrentVersion, Jurisdiction = "IN", Granted = granted, DecidedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }

        await BookTuesdayAsync();
        await RunJobAsync();
        Assert.Empty(email.Sent); // no receipt: nothing goes out
        Assert.Empty(smsGateway.Sent);

        await Decide(ConsentChannel.Email, true);
        await BookTuesdayAsync(11);
        await RunJobAsync();
        Assert.Single(email.Sent); // email consented...
        Assert.Empty(smsGateway.Sent); // ...SMS was not

        await Decide(ConsentChannel.Email, false);
        email.Sent.Clear();
        await BookTuesdayAsync(17);
        await RunJobAsync();
        Assert.Empty(email.Sent); // withdrawn: the newest receipt wins
    }

    [Fact]
    public async Task WithoutAConsentModuleNothingIsSent()
    {
        Rebuild(denyAll: true); // the module's default
        await BookTuesdayAsync();
        await RunJobAsync();
        Assert.Empty(email.Sent);
        Assert.Empty(smsGateway.Sent);
    }

    [Fact]
    public async Task ReschedulingCancelsPendingRemindersAndSendsTheRightMessageOnce()
    {
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        email.Sent.Clear();

        await h.Booking(h.Reception).RescheduleAsync(visit.Id, new RescheduleAppointment { CalendarId = h.Doctor, Start = Tuesday(18) });
        await RunJobAsync();
        await RunJobAsync(); // a second run publishes nothing new

        var rows = await RowsAsync(visit.Id);
        Assert.Equal(4, rows.Count(r => r.Status == NotificationStatus.Cancelled && r.FailureCode == "visit_changed")); // old 24 h and 2 h, both channels
        var moved = Assert.Single(email.Sent);
        Assert.Equal("Adyar (synthetic): अपॉइंटमेंट बदली गई", moved.Subject);
        Assert.Equal(Tuesday(18).AddHours(-2), rows.Single(r => r.Kind == NotificationKind.Reminder2h && r.Status == NotificationStatus.Queued && r.Channel == NotificationChannel.Email).SendAt);

        // The same event published again changes nothing (at-least-once delivery).
        await using (var db = sql.Scheduling(h.Tenant))
            await db.Outbox.Where(e => e.AggregateId == visit.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.PublishedAt, (DateTimeOffset?)null));
        await RunJobAsync();
        Assert.Equal(rows.Count, (await RowsAsync(visit.Id)).Count);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task CancellingCancelsRemindersAndTellsThePatientOnce()
    {
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        email.Sent.Clear();

        await h.Booking(h.Reception).CancelAsync(visit.Id, new CancelAppointment { Reason = CancelReason.PatientRequest });
        await RunJobAsync();
        await RunJobAsync();

        var rows = await RowsAsync(visit.Id);
        Assert.All(rows.Where(r => r.Kind is NotificationKind.Reminder24h or NotificationKind.Reminder2h), r => Assert.Equal(NotificationStatus.Cancelled, r.Status));
        Assert.Equal("Adyar (synthetic): अपॉइंटमेंट रद्द", Assert.Single(email.Sent).Subject);
    }

    [Fact]
    public async Task CancellingThisAndFollowingTellsThePatientOnceAndClearsEveryReminder()
    {
        var series = await h.Booking(h.Reception).BookSeriesAsync(new BookSeries
        {
            FacilityId = h.Facility, CalendarId = h.Doctor, PatientId = h.Patient, ReasonCategory = ReasonCategory.General, Channel = BookingChannel.Phone,
            Starts = [Tuesday(10), Tuesday(10).AddDays(7), Tuesday(10).AddDays(14)],
        });
        var ids = series.Occurrences.Select(o => o.Appointment!.Id).ToList();
        await RunJobAsync();
        email.Sent.Clear();

        await h.Booking(h.Reception).CancelFollowingAsync(ids[0], new CancelAppointment { Reason = CancelReason.PatientRequest });
        await RunJobAsync();

        Assert.Equal("Adyar (synthetic): अपॉइंटमेंट रद्द", Assert.Single(email.Sent).Subject); // one message for the whole series
        foreach (var id in ids)
            Assert.All((await RowsAsync(id)).Where(r => r.Kind is NotificationKind.Reminder24h or NotificationKind.Reminder2h), r => Assert.Equal(NotificationStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task LeaveCancelsTheQueuedRemindersOfEveryMovedVisitAndMessagesOnlyTheOffered()
    {
        // #231: a visit moved with "Manual" kept its 24 h and 2 h reminders, so the patient was reminded of a visit with no doctor.
        var offered = await BookTuesdayAsync(10);
        var manual = await BookTuesdayAsync(11);
        await RunJobAsync();
        email.Sent.Clear();

        await h.Adjustments(h.Admin).SaveAsync(new SaveAdjustment
        {
            FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.Leave, FromDate = Monday.AddDays(1), ToDate = Monday.AddDays(1), Reason = "Conference",
            Actions = [new(offered.Id, RebookAction.Offer), new(manual.Id, RebookAction.Manual)],
        });
        await RunJobAsync();

        foreach (var id in new[] { offered.Id, manual.Id })
            Assert.DoesNotContain(await RowsAsync(id), r => r.Status == NotificationStatus.Queued);
        Assert.DoesNotContain(await RowsAsync(manual.Id), r => r.Kind == NotificationKind.NeedsRebooking);
        Assert.Contains(await RowsAsync(offered.Id), r => r.Kind == NotificationKind.NeedsRebooking);
        Assert.Single(email.Sent); // the offered patient's rebooking message only
    }

    [Fact]
    public async Task AWalkInGetsNoMessages()
    {
        h.Clock.Advance(TimeSpan.FromHours(1.5)); // 09:30, inside Dr. Rao's morning
        var visit = await h.Booking(h.Reception).WalkInAsync(new WalkIn { FacilityId = h.Facility, PatientId = h.Patient, VisitType = VisitType.FollowUp, ReasonCategory = ReasonCategory.General, CalendarId = h.Doctor });
        await RunJobAsync();
        Assert.Empty(await RowsAsync(visit.Id));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task FailedSendsRetryWithBackoffThenShowAsFailedWithoutTheAddress()
    {
        email.Fail = true;
        var visit = await BookTuesdayAsync();
        async Task<Notification> Confirmation() => (await RowsAsync(visit.Id)).Single(r => r.Kind == NotificationKind.Confirmation && r.Channel == NotificationChannel.Email);

        await RunJobAsync();
        var row = await Confirmation();
        Assert.Equal((NotificationStatus.Queued, 1), (row.Status, row.Attempts));
        Assert.Equal(h.Clock.GetUtcNow().AddMinutes(1), row.NextAttemptAt);

        await RunJobAsync(); // too early: backoff holds it
        Assert.Equal(1, (await Confirmation()).Attempts);

        foreach (var wait in new[] { 1, 5, 30 })
        {
            h.Clock.Advance(TimeSpan.FromMinutes(wait));
            await RunJobAsync();
        }
        row = await Confirmation();
        Assert.Equal((NotificationStatus.Failed, 4), (row.Status, row.Attempts)); // MaxAttempts
        Assert.Equal("provider_invalidoperationexception", row.FailureCode);
        Assert.DoesNotContain("example.test", row.FailureCode); // the exception text held the address

        var report = await InScope(sp => sp.GetRequiredService<INotificationQueries>().DeliveryAsync(h.Facility, h.Clock.GetUtcNow().AddDays(-1)));
        Assert.Contains(report.Failures, f => f.Id == row.Id && f.Status == NotificationStatus.Failed);
        await using var db = sql.Notifications(h.Tenant);
        Assert.Equal(4, await db.Attempts.CountAsync(a => a.NotificationId == row.Id));
    }

    [Fact]
    public async Task SmsNeedsItsRegisteredDltTemplateIdInIndiaAndIsOtherwiseFailedAtOnce()
    {
        dlt.Clear();
        Rebuild(denyAll: false); // no template ids registered
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        var sms = (await RowsAsync(visit.Id)).Single(r => r.Kind == NotificationKind.Confirmation && r.Channel == NotificationChannel.Sms);
        Assert.Equal((NotificationStatus.Failed, "dlt_template_id_missing"), (sms.Status, sms.FailureCode));
        Assert.Empty(smsGateway.Sent);
    }

    [Fact]
    public void OnlyRegisteredTemplatesAreSendable()
    {
        foreach (var lang in new[] { "en", "hi", "ar" })
            foreach (var kind in Enum.GetValues<NotificationKind>())
            {
                var m = MessageCatalog.Render(kind, lang, new string('C', 80), new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(5.5)));
                Assert.Equal(m.TemplateKey, MessageCatalog.RegisteredTemplate(m.Body));
            }
        Assert.Null(MessageCatalog.RegisteredTemplate("Your appointment at X is confirmed for 20 Oct 2026 at 10:00 AM. Buy cheap pills now. Call 9999999999 for a deal today!!"));
        Assert.Null(MessageCatalog.RegisteredTemplate("Your appointment at  is confirmed for  at ."));
        Assert.Equal("en", MessageCatalog.Language("ml-IN")); // unsupported languages fall back
    }

    [Fact]
    public void RemindersAvoidQuietHoursAndNeverFallAfterTheVisit()
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset? At2h(DateTimeOffset start) => NotificationRules.ReminderTime(start, TimeSpan.FromHours(2), now, ist, new(21, 0), new(8, 0));
        var ten = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(5.5));
        Assert.Equal(ten.AddHours(-2), At2h(ten));
        var early = new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.FromHours(5.5)); // 06:30 is quiet: moves to 08:00
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.FromHours(5.5)), At2h(early));
        Assert.Null(At2h(new DateTimeOffset(2026, 10, 20, 7, 30, 0, TimeSpan.FromHours(5.5)))); // would be after the visit starts
        var late = new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.FromHours(5.5)); // 21:30 is quiet, 08:00 next day is after the visit
        Assert.Null(At2h(late));
        Assert.Null(NotificationRules.ReminderTime(now.AddHours(1), TimeSpan.FromHours(2), now, ist, new(21, 0), new(8, 0))); // booked inside the window
    }

    [Fact]
    public async Task ABookingIsPreparingUntilTheJobPublishesItThenItsMessagesShow()
    {
        var visit = await BookTuesdayAsync();
        Task<bool> Pending(Guid facility) => InScope(sp => sp.GetRequiredService<IOutboxDispatcher>().PendingAsync(facility, visit.Id));
        Assert.True(await Pending(h.Facility));
        Assert.False(await Pending(h.OtherFacility)); // another location's view never learns about it
        Assert.Empty(await InScope(sp => sp.GetRequiredService<INotificationQueries>().ForAppointmentAsync(h.Facility, visit.Id)));
        await RunJobAsync();
        Assert.False(await Pending(h.Facility));
        Assert.NotEmpty(await InScope(sp => sp.GetRequiredService<INotificationQueries>().ForAppointmentAsync(h.Facility, visit.Id)));
    }

    async Task<Notification> SentRowAsync()
    {
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        return (await RowsAsync(visit.Id)).First(r => r.Kind == NotificationKind.Confirmation && r.Channel == NotificationChannel.Email);
    }

    [Fact]
    public async Task ReceiptsMoveASentMessageOnceAndNeverUndoAFinalState()
    {
        var row = await SentRowAsync();
        Assert.Equal(NotificationStatus.Sent, row.Status);
        var reference = row.ProviderRef!;
        Task<bool> Apply(NotificationStatus state, string? reason = null) =>
            InScope(sp => sp.GetRequiredService<IReceiptProcessor>().ApplyAsync(new DeliveryReceipt(reference, state, reason)));

        Assert.True(await Apply(NotificationStatus.Delivered));
        Assert.False(await Apply(NotificationStatus.Delivered)); // duplicate
        Assert.False(await Apply(NotificationStatus.Bounced, "late")); // out of order
        Assert.False(await Apply(NotificationStatus.Queued)); // not a receipt state

        await using var db = sql.Notifications(h.Tenant);
        Assert.Equal(NotificationStatus.Delivered, (await db.Notifications.SingleAsync(n => n.Id == row.Id)).Status);
        Assert.Equal(2, await db.Attempts.CountAsync(a => a.NotificationId == row.Id)); // sent, delivered
    }

    [Fact]
    public async Task ABounceIsStoredWithItsReasonCodeAndShownToReception()
    {
        var row = await SentRowAsync();
        await InScope(sp => sp.GetRequiredService<IReceiptProcessor>().ApplyAsync(new DeliveryReceipt(row.ProviderRef!, NotificationStatus.Bounced, "mailbox full<script>")));
        var views = await InScope(sp => sp.GetRequiredService<INotificationQueries>().ForAppointmentAsync(h.Facility, row.AppointmentId));
        var view = views.Single(v => v.Id == row.Id);
        Assert.Equal((NotificationStatus.Bounced, "mailboxfullscript"), (view.Status, view.FailureCode)); // a code, never free text
    }

    [Fact]
    public async Task AnotherTenantCannotApplyReceiptsOrReadMessages()
    {
        var row = await SentRowAsync();
        var other = Guid.NewGuid();
        Rebuild(denyAll: false, inTenant: other);
        Assert.False(await InScope(sp => sp.GetRequiredService<IReceiptProcessor>().ApplyAsync(new DeliveryReceipt(row.ProviderRef!, NotificationStatus.Delivered, null))));
        await using var db = sql.Notifications(other);
        Assert.Empty(await db.Notifications.ToListAsync());
        Rebuild(denyAll: false);
    }

    [Fact]
    public async Task OnlyStaffWhoMayViewTheCalendarAtTheFacilitySeeDelivery()
    {
        var row = await SentRowAsync();
        foreach (var user in new[] { h.Reception, h.Nurse, h.Admin, h.Doctor })
        {
            Rebuild(denyAll: false, user: user);
            Assert.NotEmpty(await InScope(sp => sp.GetRequiredService<INotificationQueries>().ForAppointmentAsync(h.Facility, row.AppointmentId)));
        }
        foreach (var user in new[] { h.PortalUser, h.Elsewhere, Guid.NewGuid() })
        {
            Rebuild(denyAll: false, user: user);
            await Assert.ThrowsAsync<NotificationAccessDeniedException>(() => InScope(sp => sp.GetRequiredService<INotificationQueries>().ForAppointmentAsync(h.Facility, row.AppointmentId)));
        }
    }

    // MDV-P1-034: the notification centre and the board's failed-message marks.

    /// <summary>A booking whose email confirmation fails for good (the provider is down), as the desk would see it.</summary>
    async Task<Notification> FailedConfirmationAsync()
    {
        email.Fail = true;
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        foreach (var wait in new[] { 1, 5, 30 }) { h.Clock.Advance(TimeSpan.FromMinutes(wait)); await RunJobAsync(); }
        return (await RowsAsync(visit.Id)).Single(r => r.Kind == NotificationKind.Confirmation && r.Channel == NotificationChannel.Email);
    }

    Task<T> As<T>(Guid user, Func<IServiceProvider, Task<T>> action) { Rebuild(denyAll: false, user: user); return InScope(action); }

    [Fact]
    public async Task AFailedMessageReachesTheDeskOnceAndIsReadPerPerson()
    {
        var failed = await FailedConfirmationAsync();
        await RunJobAsync(); // another run changes nothing
        Task<StaffNotificationList> List(Guid user) => As(user, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility));

        var desk = await List(h.Reception);
        var item = Assert.Single(desk.Items);
        Assert.Equal((StaffNotificationKind.MessageFailed, failed.AppointmentId, false, 1), (item.Kind, item.RefId, item.Read, desk.Unread));
        Assert.Equal(1, (await List(h.Nurse)).Unread); // views the calendar, so sees it too

        // One person's read state is theirs only, and a new scope (another device) still has it.
        Assert.True(await As(h.Reception, async sp => { var n = sp.GetRequiredService<IStaffNotifications>(); return await n.MarkReadAsync(h.Facility, item.Id) && await n.MarkReadAsync(h.Facility, item.Id); })); // twice is harmless
        Assert.Equal(0, (await List(h.Reception)).Unread);
        Assert.True((await List(h.Reception)).Items[0].Read);
        Assert.Equal(1, (await List(h.Nurse)).Unread);
        await As(h.Nurse, async sp => { await sp.GetRequiredService<IStaffNotifications>().MarkAllReadAsync(h.Facility); return 0; });
        Assert.Equal(0, (await List(h.Nurse)).Unread);
    }

    /// <summary>Mutation check: drop the capability check in StaffNotifications.WhoAsync and the denied cases below fail.</summary>
    [Fact]
    public async Task OnlyStaffWhoMayViewTheDeskSeeTheirFacilitysNotifications()
    {
        await FailedConfirmationAsync();
        var id = (await As(h.Reception, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items.Single().Id;
        foreach (var user in new[] { h.PortalUser, h.Elsewhere, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<NotificationAccessDeniedException>(() => As(user, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility)));
            await Assert.ThrowsAsync<NotificationAccessDeniedException>(() => As(user, sp => sp.GetRequiredService<IStaffNotifications>().MarkReadAsync(h.Facility, id)));
            await Assert.ThrowsAsync<NotificationAccessDeniedException>(() => As(user, sp => sp.GetRequiredService<INotificationQueries>().FailedVisitsAsync(h.Facility)));
        }
        // Hospital IT administers the practice but has no desk access: it sees owner reminders only, never a message failure.
        Assert.Empty((await As(h.HospitalIt, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items);
        Assert.False(await As(h.HospitalIt, sp => sp.GetRequiredService<IStaffNotifications>().MarkReadAsync(h.Facility, id)));
        await Assert.ThrowsAsync<NotificationAccessDeniedException>(() => As(h.HospitalIt, sp => sp.GetRequiredService<INotificationQueries>().FailedVisitsAsync(h.Facility)));
        // Another tenant sees nothing at all (EF filter and row-level security).
        var other = Guid.NewGuid();
        await using var db = sql.Notifications(other);
        Assert.Empty(await db.StaffNotifications.ToListAsync());
        Assert.Empty(await db.StaffNotificationReads.ToListAsync());
    }

    [Fact]
    public async Task TheItemHoldsOnlyIdsAKindAndATime()
    {
        await FailedConfirmationAsync();
        await using var db = sql.Notifications(h.Tenant);
        var row = await db.StaffNotifications.SingleAsync();
        Assert.StartsWith("failed:", row.DedupeKey);
        Assert.Equal(typeof(string), typeof(StaffNotification).GetProperty(nameof(StaffNotification.DedupeKey))!.PropertyType);
        Assert.DoesNotContain(typeof(StaffNotification).GetProperties(), p => p.PropertyType == typeof(string) && p.Name != nameof(StaffNotification.DedupeKey)); // no free text field
    }

    [Fact]
    public async Task ABounceReceiptAlsoRaisesOneItemAndMarksTheVisitOnTheBoard()
    {
        var row = await SentRowAsync();
        Assert.Empty(await As(h.Reception, sp => sp.GetRequiredService<INotificationQueries>().FailedVisitsAsync(h.Facility)));
        for (var i = 0; i < 2; i++)
            await InScope(sp => sp.GetRequiredService<IReceiptProcessor>().ApplyAsync(new DeliveryReceipt(row.ProviderRef!, NotificationStatus.Bounced, "mailbox_full")));
        Assert.Equal(row.AppointmentId, Assert.Single(await As(h.Reception, sp => sp.GetRequiredService<INotificationQueries>().FailedVisitsAsync(h.Facility))));
        Assert.Single((await As(h.Reception, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items);
    }

    [Fact]
    public async Task ASuppressedMessageIsNotAFailure()
    {
        consent.Allow = false;
        await BookTuesdayAsync();
        await RunJobAsync();
        Assert.Empty((await As(h.Reception, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items);
    }

    // MDV-P1-006: the practice's notification preferences.

    Task<NotificationPreferencesView> Prefs() => InScope(sp => sp.GetRequiredService<INotificationPreferences>().GetAsync());

    Task<int> Save(Func<NotificationPreferencesView, NotificationPreferencesView> change) =>
        InScope(async sp => { var p = sp.GetRequiredService<INotificationPreferences>(); await p.SaveAsync(change(await p.GetAsync())); return 0; });

    [Fact]
    public async Task EmailsOpenWithThePracticesSenderNameWhichIsValidatedAndKeptOutOfTheAudit()
    {
        // #213, owner decision 7 Nov: until DLT, the clinic is named at the start of every email subject (and as the From name where possible).
        await Save(p => p with { SenderName = "  Iyer Family Clinic  " });
        Assert.Equal("Iyer Family Clinic", (await InScope(sp => sp.GetRequiredService<INotificationPreferences>().GetAsync())).SenderName);
        await BookTuesdayAsync();
        await RunJobAsync();
        var sent = Assert.Single(email.Sent);
        Assert.StartsWith("Iyer Family Clinic: ", sent.Subject, StringComparison.Ordinal);
        Assert.Equal("Iyer Family Clinic", sent.FromName);

        foreach (var (bad, code) in new[] { (new string('x', 61), "too_long"), ("Visit iyer-clinic.example.com", "link") })
        {
            var refused = await Assert.ThrowsAsync<Medvya.Tenancy.Application.SettingsValidationException>(() => Save(p => p with { SenderName = bad }));
            Assert.Contains(refused.Errors, e => e.Field == "senderName" && e.Code == code);
        }
        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains(await audit.Events.Where(e => e.Action == "settings.notifications.update").Select(e => e.Detail).ToListAsync(), d => d == "senderName:changed");
    }

    [Fact]
    public async Task TurningOffAReminderCancelsTheQueuedOnesAndStopsSchedulingMore()
    {
        var first = await BookTuesdayAsync();
        await RunJobAsync();
        Assert.Equal(4, (await RowsAsync(first.Id)).Count(r => r.Kind is NotificationKind.Reminder24h or NotificationKind.Reminder2h && r.Status == NotificationStatus.Queued));

        await Save(p => p with { Reminder24h = false });
        var rows = await RowsAsync(first.Id);
        Assert.All(rows.Where(r => r.Kind == NotificationKind.Reminder24h), r => Assert.Equal((NotificationStatus.Cancelled, "turned_off"), (r.Status, r.FailureCode)));
        Assert.All(rows.Where(r => r.Kind == NotificationKind.Reminder2h), r => Assert.Equal(NotificationStatus.Queued, r.Status)); // the other reminder is untouched
        Assert.All(rows.Where(r => r.Kind == NotificationKind.Confirmation), r => Assert.Equal(NotificationStatus.Sent, r.Status)); // already sent stays sent

        var second = await BookTuesdayAsync(11);
        await RunJobAsync();
        var later = await RowsAsync(second.Id);
        Assert.DoesNotContain(later, r => r.Kind == NotificationKind.Reminder24h);
        Assert.Contains(later, r => r.Kind == NotificationKind.Reminder2h);

        await using var audit = sql.Audit(h.Tenant);
        var saved = await audit.Events.SingleAsync(e => e.Action == "settings.notifications.update");
        Assert.Equal("reminder24h:on>off", saved.Detail); // the before and after of what changed, nothing else
    }

    [Fact]
    public void TheAuditDetailListsEveryChangedSettingAndFitsTheColumn()
    {
        var before = new NotificationPreferencesView(true, true, true, true, true, true, new TimeOnly(21, 0), new TimeOnly(8, 0));
        Assert.Null(NotificationPreferences.Diff(before, before));
        var everything = new NotificationPreferencesView(false, false, false, false, false, false, new TimeOnly(22, 30), new TimeOnly(7, 0));
        var detail = NotificationPreferences.Diff(before, everything)!;
        Assert.Equal("confirmation:on>off;reminder24h:on>off;reminder2h:on>off;changes:on>off;email:on>off;sms:on>off;quietStart:21:00>22:30;quietEnd:08:00>07:00", detail);
        Assert.True(detail.Length <= 256);
    }

    [Fact]
    public async Task TurningOffAChannelCancelsItsQueuedMessagesAndNothingNewIsMadeForIt()
    {
        var first = await BookTuesdayAsync();
        await RunJobAsync();
        await Save(p => p with { Sms = false });
        Assert.All((await RowsAsync(first.Id)).Where(r => r.Channel == NotificationChannel.Sms && r.Status != NotificationStatus.Sent), r => Assert.Equal(NotificationStatus.Cancelled, r.Status));
        Assert.Contains(await RowsAsync(first.Id), r => r.Channel == NotificationChannel.Email && r.Status == NotificationStatus.Queued);

        var second = await BookTuesdayAsync(11);
        Assert.DoesNotContain(await RowsAsync(second.Id), r => r.Channel == NotificationChannel.Sms);
    }

    // SAFETY: mutation check: dropping the terms filter in NotificationSender.SendDueAsync sends the confirmation (first assert fails);
    // filtering every kind keeps the cancellation queued (second assert fails).
    [Fact]
    public async Task UntilTheTermsAreAcceptedOnlyACancellationGoesOutAndTheRestWaits()
    {
        terms.Accepted = false;
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        Assert.Empty(email.Sent);
        Assert.All((await RowsAsync(visit.Id)).Where(r => r.Kind == NotificationKind.Confirmation), r => Assert.Equal(NotificationStatus.Queued, r.Status)); // waits, not dropped

        await h.Booking(h.Reception).CancelAsync(visit.Id, new CancelAppointment { Reason = CancelReason.PatientRequest });
        await RunJobAsync();
        Assert.Equal("Adyar (synthetic): अपॉइंटमेंट रद्द", Assert.Single(email.Sent).Subject);
    }

    [Fact]
    public async Task TurningOffConfirmationsAndChangesQueuesNone()
    {
        await Save(p => p with { Confirmation = false, Changes = false });
        var visit = await BookTuesdayAsync();
        await h.Booking(h.Reception).CancelAsync(visit.Id, new CancelAppointment { Reason = CancelReason.PatientRequest });
        await RunJobAsync();
        Assert.DoesNotContain(await RowsAsync(visit.Id), r => r.Kind is NotificationKind.Confirmation or NotificationKind.Cancelled);
        Assert.Empty(email.Sent);
    }

    /// <summary>Mutation check: remove the quiet-window branch in NotificationSender and the SMS goes out at once, failing this test.</summary>
    [Fact]
    public async Task SmsWaitsForTheEndOfTheQuietHoursAndEmailDoesNot()
    {
        await Save(p => p with { QuietStart = new(7, 0), QuietEnd = new(9, 30) }); // the clock is Monday 08:00 in the facility
        var visit = await BookTuesdayAsync();
        await RunJobAsync();
        var rows = await RowsAsync(visit.Id);
        var sms = rows.Single(r => r.Kind == NotificationKind.Confirmation && r.Channel == NotificationChannel.Sms);
        Assert.Equal((NotificationStatus.Queued, 0, At(9, 30)), (sms.Status, sms.Attempts, sms.NextAttemptAt));
        Assert.Empty(smsGateway.Sent);
        Assert.Single(email.Sent); // email is not held

        await RunJobAsync(); // still inside the window and before NextAttemptAt: nothing happens
        Assert.Empty(smsGateway.Sent);

        h.Clock.Advance(TimeSpan.FromMinutes(100)); // 09:40
        await RunJobAsync();
        Assert.Single(smsGateway.Sent);
    }

    [Fact]
    public async Task WithoutSavedPreferencesTheDefaultsApplyAndOnlyTheOwnTenantSeesItsRow()
    {
        var defaults = await Prefs();
        Assert.Equal((true, true, true, true, true, true), (defaults.Confirmation, defaults.Reminder24h, defaults.Reminder2h, defaults.Changes, defaults.Email, defaults.Sms));
        await Save(p => p with { Reminder2h = false });
        Assert.False((await Prefs()).Reminder2h);
        Rebuild(denyAll: false, inTenant: Guid.NewGuid());
        Assert.True((await Prefs()).Reminder2h); // another practice is unaffected
        Rebuild(denyAll: false);
    }

    [Fact]
    public void PreviewsShowPlaceholdersOnlyInEveryLanguage()
    {
        foreach (var lang in new[] { "en", "hi", "ar" })
        {
            var previews = MessageCatalog.Previews(lang);
            Assert.Equal(6, previews.Count);
            foreach (var p in previews)
            {
                Assert.DoesNotContain(MessageCatalog.Slot, p.Text); // slots are named, never raw
                Assert.Equal(["{clinic}", "{date}", "{time}"], System.Text.RegularExpressions.Regex.Matches(p.Text, @"\{[a-z]+\}").Select(m => m.Value).ToArray()); // no clinical merge field exists
                if (lang == "en") Assert.DoesNotMatch("(?i)doctor|diagnos|reason|medic|result", p.Text);
            }
        }
    }

    [Fact]
    public async Task TheJobFindsATenantOnlyOnceItHasWrittenAnEvent()
    {
        var before = await InScope(sp => sp.GetRequiredService<IOutboxDispatcher>().TenantsAsync());
        Assert.DoesNotContain(h.Tenant, before);
        await BookTuesdayAsync();
        await BookTuesdayAsync(18);
        var after = await InScope(sp => sp.GetRequiredService<IOutboxDispatcher>().TenantsAsync());
        Assert.Single(after, h.Tenant);
    }
}
