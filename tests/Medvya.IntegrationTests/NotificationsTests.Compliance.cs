using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-010 on real SQL Server: licence expiry reminders at 60/30/7/0 days and the quarterly access review. Synthetic data only.</summary>
public sealed partial class NotificationsTests
{
    async Task<Guid> DoctorMembershipAsync()
    {
        await using var db = sql.Context(h.Tenant);
        return await db.Memberships.Where(m => m.UserId == h.Doctor).Select(m => m.Id).SingleAsync();
    }

    /// <summary>A verified licence for the owner doctor that expires on <paramref name="expires"/>, and a practice contact address.</summary>
    async Task<Guid> LicenceExpiringAsync(DateOnly expires)
    {
        var membership = await DoctorMembershipAsync();
        await using var db = sql.Context(h.Tenant);
        db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", ExpiresOn = expires, SubmittedAt = h.Clock.GetUtcNow(), VerifiedAt = h.Clock.GetUtcNow() });
        db.Profiles.Add(new PracticeProfile { Id = Guid.CreateVersion7(), TenantId = h.Tenant, Email = "desk@clinic.example" });
        await db.SaveChangesAsync();
        return membership;
    }

    Task<int> RunCompliance() => InScope(sp => sp.GetRequiredService<IComplianceReminders>().RunAsync());

    static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime);

    [Fact]
    public async Task ALicenceIsRemindedAt60And30And7AndOnItsLastDayEachOnceByNotificationAndEmail()
    {
        await ConfirmEveryoneAsync(); // this quarter's review is done, so only licence reminders appear
        var expires = Day(h.Clock.GetUtcNow()).AddDays(70);
        var membership = await LicenceExpiringAsync(expires);
        Assert.Equal(0, await RunCompliance()); // 70 days left: too early
        var steps = new List<int?>();
        foreach (var daysLeft in new[] { 55, 30, 29, 7, 6, 0, -1 })
        {
            h.Clock.Advance(TimeSpan.FromDays(expires.DayNumber - daysLeft - Day(h.Clock.GetUtcNow()).DayNumber));
            var raised = await RunCompliance();
            Assert.Equal(0, await RunCompliance()); // a second run adds nothing
            if (raised > 0) steps.Add(ComplianceReminders.StepFor(expires.DayNumber - Day(h.Clock.GetUtcNow()).DayNumber));
        }
        Assert.Equal([60, 30, 7, 0], steps);
        Assert.Equal(4, email.Sent.Count(m => m.To == "desk@clinic.example"));
        Assert.All(email.Sent, m => { Assert.DoesNotContain("Rao", m.Body); Assert.DoesNotContain("84213", m.Body); }); // no doctor name or licence number

        var items = (await As(h.Admin, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items.Where(i => i.Kind == StaffNotificationKind.LicenceExpiring).ToList();
        Assert.Equal([0, 7, 30, 60], items.Select(i => i.Detail).Order().ToArray());
        Assert.All(items, i => Assert.Equal(membership, i.RefId));
        // The desk does not see owner reminders.
        Assert.DoesNotContain((await As(h.Reception, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items, i => i.Kind == StaffNotificationKind.LicenceExpiring);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(4, await audit.Events.CountAsync(e => e.Action == "compliance.licence_expiry.notify"));
    }

    [Fact]
    public async Task AFailedEmailUndoesTheReminderSoTheNextRunTriesAgain()
    {
        await ConfirmEveryoneAsync();
        await LicenceExpiringAsync(Day(h.Clock.GetUtcNow()).AddDays(5));
        email.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(RunCompliance);
        await using (var db = sql.Notifications(h.Tenant)) Assert.Empty(await db.StaffNotifications.ToListAsync());
        email.Fail = false;
        Assert.Equal(1, await RunCompliance());
    }

    [Fact]
    public async Task TheQuarterlyReviewIsDueAtEachFacilityUntilItIsCompleteThenAgainNextQuarter()
    {
        Assert.Equal(2, await RunCompliance()); // two facilities
        Assert.Equal(0, await RunCompliance());
        var admin = await As(h.Admin, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility));
        Assert.Contains(admin.Items, i => i.Kind == StaffNotificationKind.AccessReviewDue);

        await ConfirmEveryoneAsync();
        h.Clock.Advance(TimeSpan.FromDays(10)); // still October (Q4): complete, so nothing new
        Assert.Equal(0, await RunCompliance());
        h.Clock.Advance(TimeSpan.FromDays(80)); // January: a new quarter, not reviewed yet
        Assert.Equal(2, await RunCompliance());
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(4, await audit.Events.CountAsync(e => e.Action == "compliance.access_review.due"));
    }

    async Task ConfirmEveryoneAsync()
    {
        var view = await InScope(sp => sp.GetRequiredService<IAccessReviews>().CurrentAsync());
        foreach (var m in view.Members) Assert.True(await InScope(sp => sp.GetRequiredService<IAccessReviews>().ConfirmAsync(m.MembershipId)));
    }

    [Fact]
    public async Task TheReviewListsEveryActiveMemberAndCompletesWhenEachHasADecision()
    {
        var first = await InScope(sp => sp.GetRequiredService<IAccessReviews>().CurrentAsync());
        Assert.Equal("2026-Q4", first.Quarter);
        Assert.Null(first.CompletedAt);
        Assert.DoesNotContain(first.Members, m => m.Role == Role.Patient); // portal accounts are not staff
        Assert.Equal(8, first.Members.Count); // every active staff membership of both facilities
        Assert.All(first.Members, m => Assert.Null(m.Decision));

        // Offboarding one person is their decision; confirming the rest completes it.
        var nurse = first.Members.Single(m => m.Role == Role.Nurse).MembershipId;
        await InScope(async sp => { await sp.GetRequiredService<IStaffRoster>().OffboardAsync(nurse, "Left the practice"); await sp.GetRequiredService<IAccessReviews>().RecordOffboardedAsync(nurse); return 0; });
        Assert.False(await InScope(sp => sp.GetRequiredService<IAccessReviews>().ConfirmAsync(nurse))); // no longer active
        Assert.False(await InScope(sp => sp.GetRequiredService<IAccessReviews>().ConfirmAsync(Guid.NewGuid())));
        var rest = (await InScope(sp => sp.GetRequiredService<IAccessReviews>().CurrentAsync())).Members;
        Assert.Equal(7, rest.Count);
        foreach (var m in rest.Take(rest.Count - 1)) await InScope(sp => sp.GetRequiredService<IAccessReviews>().ConfirmAsync(m.MembershipId));
        Assert.Null((await InScope(sp => sp.GetRequiredService<IAccessReviews>().CurrentAsync())).CompletedAt);
        await InScope(sp => sp.GetRequiredService<IAccessReviews>().ConfirmAsync(rest[^1].MembershipId));
        var done = await InScope(sp => sp.GetRequiredService<IAccessReviews>().CurrentAsync());
        Assert.Equal((h.Clock.GetUtcNow(), h.Clock.GetUtcNow()), (done.CompletedAt, done.LastCompletedAt));

        await using var audit = sql.Audit(h.Tenant);
        var actions = await audit.Events.Where(e => e.Action.StartsWith("access_review.")).Select(e => e.Action).ToListAsync();
        Assert.Equal((1, 1, 6), (actions.Count(a => a == "access_review.complete"), actions.Count(a => a == "access_review.offboard"), actions.Count(a => a == "access_review.confirm")));
    }

    [Fact]
    public async Task AnotherTenantSeesNoReviewAndTheJobFindsTenantsFromTheIndex()
    {
        await ConfirmEveryoneAsync();
        Rebuild(denyAll: false, inTenant: Guid.NewGuid());
        await using (var db = sql.Context(Guid.NewGuid())) Assert.Empty(await db.AccessReviews.ToListAsync());
        Rebuild(denyAll: false);
        Assert.Contains(h.Tenant, await InScope(sp => sp.GetRequiredService<IComplianceFacts>().TenantsAsync())); // the trigger on new tenants
    }
}
