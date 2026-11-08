using Medvya.Audit.Domain;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-024 on real PostgreSQL: the role matrix first, then a care relationship under the clinic's policy (all facility clinicians or
/// assigned only) or a live, time-limited grant; request access is stored, audited and notifies the owner, and only another owner decides.
/// </summary>
public sealed class ClinicalAccessTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    static readonly Access[] Treating = [Access.Read, Access.Limited, Access.Full, Access.Sign];

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IClinicalAccess Clinical(Guid user) => h.Get<IClinicalAccess>(user);

    IAccessRequests Requests(Guid user) =>
        h.Get<IAccessRequests>(user, s => s.AddScoped<IAccessRequestParticipant, AccessRequestNotifier>());

    Task<Access> CheckAsync(Guid user, Guid patient, Capability capability = Capability.Patient360Chart) =>
        Clinical(user).RequireAsync(patient, h.Facility, capability, Treating);

    async Task<ClinicalAccessDeniedException> RefusedAsync(Guid user, Guid patient, Capability capability = Capability.Patient360Chart) =>
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => CheckAsync(user, patient, capability));

    Task<ClinicalAccessPolicy> AssignedOnlyAsync() => h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);

    [Fact]
    public async Task ByDefaultEveryClinicianOfTheFacilityTreatsItsPatientsAndNooneElse()
    {
        Assert.Equal(ClinicalAccessPolicy.AllFacilityClinicians, await h.Settings(h.Admin).GetClinicalAccessAsync());
        Assert.Equal(Access.Full, await CheckAsync(h.Doctor, h.Patient));
        Assert.Equal(Access.Full, await CheckAsync(h.SecondDoctor, h.SecondPatient));
        Assert.Equal(Access.Limited, await CheckAsync(h.Nurse, h.Patient));

        // Registered at the other location and never seen here: not this facility's patient, so a request can help.
        var elsewhere = await RefusedAsync(h.Doctor, h.OtherFacilityPatient);
        Assert.True(elsewhere.CanRequest);
        Assert.False(elsewhere.RequestPending);
    }

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.PracticeAdmin)]
    [InlineData(Role.Patient)]
    [InlineData(Role.HospitalIt)] // break-glass comes with hospitals (P7); until then it opens nothing
    public async Task ARoleThatDoesNotTreatPatientsIsRefusedAndCannotAsk(Role role)
    {
        var refused = await RefusedAsync(h.ByRole[role], h.Patient);
        Assert.False(refused.CanRequest);
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Requests(h.ByRole[role]).RequestAsync(h.Patient, h.Facility, AccessRequestReason.CoveringColleague));
    }

    [Fact]
    public async Task BreakGlassOpensNothingBeforeHospitals()
    {
        // Hospital IT holds Patient 360 at break-glass; even a caller that accepts that level gets nothing until P7 builds its review.
        var refused = await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() =>
            Clinical(h.HospitalIt).RequireAsync(h.Patient, h.Facility, Capability.Patient360Chart, [Access.BreakGlass]));
        Assert.False(refused.CanRequest);
    }

    [Fact]
    public async Task NurseAccessFollowsTheMatrixLimitedChartVitalsAndAllergiesButNoSigning()
    {
        Assert.Equal(Access.Limited, await CheckAsync(h.Nurse, h.Patient, Capability.Patient360Chart));
        Assert.Equal(Access.Full, await CheckAsync(h.Nurse, h.Patient, Capability.VitalsAllergiesEntry));
        Assert.Equal(Access.Read, await CheckAsync(h.Nurse, h.Patient, Capability.ClinicalNotes));
        // Signing a note asks for Sign: the nurse's Read is not enough, and no request can change a role.
        var sign = await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Clinical(h.Nurse).RequireAsync(h.Patient, h.Facility, Capability.ClinicalNotes, [Access.Sign]));
        Assert.False(sign.CanRequest);
        Assert.False((await RefusedAsync(h.Nurse, h.Patient, Capability.PrescriptionsOrders)).CanRequest);
    }

    [Fact]
    public async Task AssignedOnlyLimitsDoctorsToTheirOwnVisitsAndTheNurseToTodaysVisits()
    {
        await AssignedOnlyAsync();
        await h.BookAsync(10, patient: h.Patient); // today, in Dr. Rao's calendar
        await h.BookAsync(0, patient: h.SecondPatient, start: SchedulingHarness.At(10).AddDays(1)); // tomorrow, Dr. Rao

        Assert.Equal(Access.Full, await CheckAsync(h.Doctor, h.Patient));
        Assert.Equal(Access.Full, await CheckAsync(h.Doctor, h.SecondPatient));
        Assert.True((await RefusedAsync(h.SecondDoctor, h.Patient)).CanRequest); // registered here, but not his patient
        Assert.Equal(Access.Limited, await CheckAsync(h.Nurse, h.Patient));
        Assert.True((await RefusedAsync(h.Nurse, h.SecondPatient)).CanRequest); // not today

        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(Access.Limited, await CheckAsync(h.Nurse, h.SecondPatient));
        Assert.True((await RefusedAsync(h.Nurse, h.Patient)).CanRequest); // yesterday's visit no longer counts for the nurse
        Assert.Equal(Access.Full, await CheckAsync(h.Doctor, h.Patient)); // the doctor's own patient stays theirs
    }

    [Fact]
    public async Task ACancelledVisitIsNoRelationship()
    {
        await AssignedOnlyAsync();
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.Booking(h.Reception).CancelAsync(visit.Id, new CancelAppointment { Reason = Scheduling.Domain.CancelReason.BookedInError });
        Assert.True((await RefusedAsync(h.Doctor, h.Patient)).CanRequest);
    }

    [Fact]
    public async Task ARequestIsStoredOnceAuditedAndNotifiesTheOwners()
    {
        var first = await Requests(h.Doctor).RequestAsync(h.OtherFacilityPatient, h.Facility, AccessRequestReason.CoveringColleague);
        var again = await Requests(h.Doctor).RequestAsync(h.OtherFacilityPatient, h.Facility, AccessRequestReason.Referral);
        Assert.Equal(first.Id, again.Id); // one open request per clinician and patient
        Assert.Equal(AccessRequestStatus.Pending, first.Status);
        Assert.True((await RefusedAsync(h.Doctor, h.OtherFacilityPatient)).RequestPending);

        var queue = await Requests(h.Admin).PendingAsync(h.Facility);
        Assert.Equal(first.Id, Assert.Single(queue).Id);

        await using (var audit = sql.Audit(h.Tenant))
        {
            var rows = await audit.Events.Where(e => e.Action == "patient.access.request").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal((h.OtherFacilityPatient, (Guid?)h.Doctor, AuditPurpose.Treatment, AuditOutcome.Allowed), (r.PatientId!.Value, r.ActorUserId, r.Purpose, r.Outcome)));
        }
        await using var notifications = sql.Notifications(h.Tenant);
        var item = Assert.Single(await notifications.StaffNotifications.Where(n => n.Kind == StaffNotificationKind.AccessRequested).ToListAsync());
        Assert.Equal((h.Facility, (Guid?)first.Id), (item.FacilityId, item.RefId));
    }

    [Fact]
    public async Task SimultaneousRequestsLeaveOneOpenRequestAndEachAttemptItsAuditRow()
    {
        // Several tabs at once: the losers of the unique index answer with the winner's request, and every attempt is still audited.
        var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Requests(h.Doctor).RequestAsync(h.OtherFacilityPatient, h.Facility, AccessRequestReason.CoveringColleague))));
        Assert.Single(all.Select(r => r.Id).Distinct());
        Assert.Single(await Requests(h.Admin).PendingAsync(h.Facility));
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(8, await audit.Events.CountAsync(e => e.Action == "patient.access.request" && e.Outcome == AuditOutcome.Allowed));
    }

    [Fact]
    public async Task AGrantIsACareRelationshipUntilItExpires()
    {
        await AssignedOnlyAsync();
        var request = await Requests(h.SecondDoctor).RequestAsync(h.Patient, h.Facility, AccessRequestReason.CoveringColleague);
        var granted = await Requests(h.Admin).GrantAsync(request.Id, 1);
        Assert.Equal((AccessRequestStatus.Granted, h.Clock.GetUtcNow().AddDays(1)), (granted!.Status, granted.ExpiresAt!.Value));
        Assert.Empty(await Requests(h.Admin).PendingAsync(h.Facility));

        Assert.Equal(Access.Full, await CheckAsync(h.SecondDoctor, h.Patient));
        await RefusedAsync(h.SecondDoctor, h.SecondPatient); // for that patient only
        await RefusedAsync(h.Nurse, h.Patient); // and for that clinician only

        h.Clock.Advance(TimeSpan.FromDays(1));
        var after = await RefusedAsync(h.SecondDoctor, h.Patient);
        Assert.True(after.CanRequest);
        Assert.False(after.RequestPending);

        await using var audit = sql.Audit(h.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == "patient.access.grant");
        Assert.Equal((h.Patient, (Guid?)h.Admin, AuditPurpose.Operations), (row.PatientId!.Value, row.ActorUserId, row.Purpose));
    }

    [Fact]
    public async Task ADeclinedRequestOpensNothingAndCanBeAskedAgain()
    {
        await AssignedOnlyAsync();
        var request = await Requests(h.SecondDoctor).RequestAsync(h.Patient, h.Facility, AccessRequestReason.SeeingPatientToday);
        Assert.Equal(AccessRequestStatus.Declined, (await Requests(h.Admin).DeclineAsync(request.Id))!.Status);
        var refused = await RefusedAsync(h.SecondDoctor, h.Patient);
        Assert.False(refused.RequestPending);
        Assert.NotEqual(request.Id, (await Requests(h.SecondDoctor).RequestAsync(h.Patient, h.Facility, AccessRequestReason.SeeingPatientToday)).Id);
    }

    [Fact]
    public async Task OnlyAnotherOwnerOfThatFacilityDecidesOnceForAListedDuration()
    {
        var request = await Requests(h.Doctor).RequestAsync(h.OtherFacilityPatient, h.Facility, AccessRequestReason.Referral);

        // The solo doctor owns the practice (Config) but never approves their own access.
        Assert.Equal("own_request", (await Assert.ThrowsAsync<AccessRequestRejectedException>(() => Requests(h.Doctor).GrantAsync(request.Id, 7))).Code);
        // An admin of the other location only, a nurse, reception: not this facility's owners.
        foreach (var user in new[] { h.Elsewhere, h.Nurse, h.Reception })
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Requests(user).GrantAsync(request.Id, 7));
        await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Requests(h.Nurse).PendingAsync(h.Facility));
        // Open-ended or odd durations are refused.
        foreach (var days in new[] { 0, 2, 365, -1 })
            await Assert.ThrowsAsync<SettingsValidationException>(() => Requests(h.Admin).GrantAsync(request.Id, days));

        Assert.NotNull(await Requests(h.Admin).GrantAsync(request.Id, 30));
        Assert.Equal("not_pending", (await Assert.ThrowsAsync<AccessRequestRejectedException>(() => Requests(h.Admin).DeclineAsync(request.Id))).Code);
        Assert.Null(await Requests(h.Admin).GrantAsync(Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task AnotherTenantsRequestIsInvisible()
    {
        var other = new SchedulingHarness(sql);
        await other.SeedAsync();
        var request = await Requests(h.Doctor).RequestAsync(h.OtherFacilityPatient, h.Facility, AccessRequestReason.Referral);
        Assert.Null(await other.Get<IAccessRequests>(other.Admin).GrantAsync(request.Id, 30));
        await other.CloseAsync();
    }

    [Fact]
    public async Task ThePolicyIsASettingOfTheOwnerAuditedWithItsBeforeAndAfter()
    {
        Assert.Equal(ClinicalAccessPolicy.AssignedOnly, await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly));
        Assert.Equal(ClinicalAccessPolicy.AssignedOnly, await h.Settings(h.Doctor).GetClinicalAccessAsync());
        await Assert.ThrowsAsync<SettingsValidationException>(() => h.Settings(h.Admin).SaveClinicalAccessAsync((ClinicalAccessPolicy)7));

        await using var db = sql.Context(h.Tenant);
        var change = await db.SettingsChanges.SingleAsync(c => c.Section == "clinical_access");
        Assert.Equal(("{\"policy\":\"AllFacilityClinicians\"}", "{\"policy\":\"AssignedOnly\"}"), (change.Before, change.After));
    }
}
