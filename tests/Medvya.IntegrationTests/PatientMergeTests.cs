using System.Data.Common;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-017 against real SQL Server: "not the same" suppresses a pair, a merge cannot run without its approvers (a second, different
/// clinician when both records hold clinical data), either MRN then finds the survivor, visits and identifiers move, the merge is
/// undone exactly, and a failing participant rolls everything back. Synthetic data only; IDs fail their checksums by construction.
/// </summary>
public sealed class PatientMergeTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), reception = Guid.NewGuid(), clinicianA = Guid.NewGuid(), clinicianB = Guid.NewGuid(), admin = Guid.NewGuid();
    readonly ManualClock clock = new(new DateTimeOffset(2026, 11, 2, 4, 0, 0, TimeSpan.Zero));

    sealed class User(Guid id) : ICurrentUser { public Guid? UserId => id; }

    sealed class Clinical(bool has) : IPatientMergeParticipant
    {
        public Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(has);
        public Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task MoveAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RestoreAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A module that fails after the identifiers moved: the whole merge must roll back.</summary>
    sealed class Failing : IPatientMergeParticipant
    {
        public Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task MoveAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken) => throw new InvalidOperationException("synthetic failure");
        public Task RestoreAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    PatientRegistry Registry(Guid user, Guid? at = null) =>
        new(sql.Patients(tenant), new PostgresFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(at ?? facility), new User(user),
            new AuditLog(sql.Audit(tenant)), clock, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));

    PatientMerges Merges(Guid user, bool clinical = false, Guid? at = null, IPatientMergeParticipant? extra = null)
    {
        var db = sql.Patients(tenant);
        var who = new PostgresFixture.FixedTenant(tenant);
        IPatientMergeParticipant[] parts = [new PatientMergeParticipant(sql.Scheduling(tenant), who), new Clinical(clinical), .. extra is null ? [] : new[] { extra }];
        return new(db, who, new PatientRegistryTests.FixedFacility(at ?? facility), new User(user), new AuditLog(sql.Audit(tenant)), clock, parts);
    }

    /// <summary>The older record (survivor) and a newer one with the same ABHA plus an ABHA address, asked for review at registration.</summary>
    async Task<(RegistrationResult Survivor, RegistrationResult Merged, Guid Request)> PairAsync(string survivorName = "Ravi Menon", string mergedName = "Ravindran Menon K")
    {
        var abha = new IdentifierInput(IdentifierScheme.Abha, "91-0000-0000-0" + Random.Shared.Next(100, 999));
        var survivor = await Registry(reception).RegisterAsync(IndiaBase with
        {
            FacilityId = facility, FullName = survivorName, DateOfBirth = new DateOnly(1971, 3, 9), Mobile = "+91 90000 0" + Random.Shared.Next(1000, 9999), Identifiers = [abha],
        });
        clock.Advance(TimeSpan.FromHours(1));
        var merged = await Registry(reception).RegisterAsync(IndiaBase with
        {
            FacilityId = facility, FullName = mergedName, DateOfBirth = new DateOnly(1971, 3, 9), Mobile = "+91 90000 0" + Random.Shared.Next(1000, 9999),
            Identifiers = [abha, new(IdentifierScheme.AbhaAddress, "merged" + Random.Shared.Next(100, 999) + "@abdm")],
            Duplicates = new(DuplicateDecision.MergeReview, [survivor.PatientId]),
        });
        var request = (await Merges(reception).QueueAsync()).Single(r => r.SurvivorMrn == survivor.Mrn && r.MergedMrn == merged.Mrn).Id;
        return (survivor, merged, request);
    }

    async Task AwaitApprovalAsync(Guid request) => Assert.True(await Merges(reception).RequestApprovalAsync(request));

    async Task AddVisitAsync(Guid patientId)
    {
        await using var db = sql.Scheduling(tenant);
        db.Appointments.Add(new Appointment
        {
            Id = Guid.CreateVersion7(), TenantId = tenant, FacilityId = facility, CalendarId = Guid.NewGuid(), CalendarKind = CalendarKind.Doctor, PatientId = patientId,
            Start = clock.GetUtcNow(), End = clock.GetUtcNow().AddMinutes(20), State = AppointmentState.Booked, BookedAt = clock.GetUtcNow(), ChangedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }

    async Task<int> VisitsOfAsync(Guid patientId) => await sql.Scheduling(tenant).Appointments.CountAsync(a => a.PatientId == patientId);

    async Task<List<string>> AuditActionsAsync(Guid patientId) =>
        await sql.Audit(tenant).Events.Where(e => e.PatientId == patientId).Select(e => e.Action).ToListAsync();

    static string ErrorCode(PatientValidationException e) => e.Errors.Single().Code;

    [Fact]
    public async Task NotTheSamePersonClosesTheRequestAndSuppressesThePairInEitherOrder()
    {
        var (survivor, merged, request) = await PairAsync();

        Assert.True(await Merges(reception).NotSameAsync(request));

        Assert.DoesNotContain(await Merges(reception).QueueAsync(), r => r.Id == request);
        Assert.Equal(MergeRequestStatus.NotSame, (await Merges(reception).GetAsync(request))!.Status);
        foreach (var (a, b) in new[] { (survivor.PatientId, merged.PatientId), (merged.PatientId, survivor.PatientId) })
            Assert.Equal("pair_decided", ErrorCode(await Assert.ThrowsAsync<PatientValidationException>(() => Merges(reception).RequestAsync(a, b))));
        // A closed request cannot be approved into a merge afterwards.
        await Assert.ThrowsAsync<PatientValidationException>(() => Merges(clinicianA).ApproveAsync(request));
        Assert.Contains("patient.merge.not_same", await AuditActionsAsync(survivor.PatientId));
    }

    [Fact]
    public async Task AMergeNeedsAnAwaitingRequestAndAClinicianApprovalAndAuditsEachApproval()
    {
        var (survivor, merged, request) = await PairAsync();

        // Still Proposed: nobody can approve it yet.
        Assert.Equal("not_awaiting_approval", ErrorCode(await Assert.ThrowsAsync<PatientValidationException>(() => Merges(clinicianA).ApproveAsync(request))));
        Assert.Null((await Registry(reception).GetAsync(merged.PatientId))!.Patient.MergedIntoPatientId);

        await AwaitApprovalAsync(request);
        Assert.Equal(MergeRequestStatus.Merged, await Merges(clinicianA).ApproveAsync(request));

        var actions = await AuditActionsAsync(survivor.PatientId);
        Assert.Contains("patient.merge.approve", actions);
        Assert.Contains("patient.merge", actions);
    }

    [Fact]
    public async Task WhenBothRecordsHoldClinicalDataASecondDifferentClinicianMustApprove()
    {
        var (survivor, merged, request) = await PairAsync();
        await AwaitApprovalAsync(request);

        Assert.Equal(MergeRequestStatus.AwaitingApproval, await Merges(clinicianA, clinical: true).ApproveAsync(request));
        Assert.Equal("same_approver", ErrorCode(await Assert.ThrowsAsync<PatientValidationException>(() => Merges(clinicianA, clinical: true).ApproveAsync(request))));
        Assert.Equal(2, (await Merges(clinicianA, clinical: true).GetAsync(request))!.ApprovalsRequired);
        Assert.Null(await sql.Patients(tenant).Patients.Where(p => p.Id == merged.PatientId).Select(p => p.MergedIntoPatientId).SingleAsync());

        Assert.Equal(MergeRequestStatus.Merged, await Merges(clinicianB, clinical: true).ApproveAsync(request));
        Assert.NotNull(await sql.Patients(tenant).Patients.Where(p => p.Id == merged.PatientId).Select(p => p.MergedIntoPatientId).SingleAsync());
        Assert.Equal(2, (await AuditActionsAsync(survivor.PatientId)).Count(a => a == "patient.merge.approve"));
    }

    [Fact]
    public async Task AfterTheMergeEitherMrnFindsTheSurvivorAndVisitsAndIdentifiersMove()
    {
        var (survivor, merged, request) = await PairAsync();
        await AddVisitAsync(merged.PatientId);
        await AddVisitAsync(survivor.PatientId);
        await AwaitApprovalAsync(request);
        await Merges(clinicianA).ApproveAsync(request);

        foreach (var mrn in new[] { survivor.Mrn, merged.Mrn })
            Assert.Equal(survivor.PatientId, (await Registry(reception).SearchAsync(new("IN", mrn))).Items.Single().Id);
        Assert.Null(await Registry(reception).GetAsync(merged.PatientId)); // the merged record is hidden, not deleted
        Assert.Equal(2, await VisitsOfAsync(survivor.PatientId));
        Assert.Equal(0, await VisitsOfAsync(merged.PatientId));
        var held = (await Registry(reception).GetAsync(survivor.PatientId))!.Identifiers;
        // The shared ABHA is not duplicated on the survivor; the ABHA address moved to it.
        Assert.Equal([IdentifierScheme.Abha, IdentifierScheme.AbhaAddress], held.Select(i => i.Scheme).Order());
        Assert.NotNull(await sql.Patients(tenant).Patients.SingleAsync(p => p.Id == merged.PatientId)); // both originals are kept
    }

    /// <summary>An MRN typed as digits is a number too: it must find the survivor alone, never also the patients whose phone happens to contain those digits.</summary>
    [Fact]
    public async Task AnMrnTypedAsDigitsWinsOverPhonesContainingThemAndFindsTheSurvivorOnly()
    {
        var (survivor, merged, request) = await PairAsync();
        var lookalike = await Registry(reception).RegisterAsync(IndiaBase with { FacilityId = facility, FullName = "Lata Pillai", Mobile = $"+91 9{merged.Mrn}123", Identifiers = [] });
        await AwaitApprovalAsync(request);
        await Merges(clinicianA).ApproveAsync(request);

        foreach (var mrn in new[] { survivor.Mrn, merged.Mrn })
            Assert.Equal(survivor.PatientId, (await Registry(reception).SearchAsync(new("IN", mrn))).Items.Single().Id);
        // 7 digits cannot be an MRN: a phone fragment. Other tests' patients share this database, so other phones may contain the digits too: no Single().
        var byPhoneFragment = (await Registry(reception).SearchAsync(new("IN", "9" + merged.Mrn))).Items;
        Assert.Contains(byPhoneFragment, i => i.Id == lookalike.PatientId);
        Assert.DoesNotContain(byPhoneFragment, i => i.Id == survivor.PatientId || i.Id == merged.PatientId);

        Assert.True(await Merges(admin).UndoAsync(request));
        Assert.Equal(merged.PatientId, (await Registry(reception).SearchAsync(new("IN", merged.Mrn))).Items.Single().Id);
        Assert.Equal(survivor.PatientId, (await Registry(reception).SearchAsync(new("IN", survivor.Mrn))).Items.Single().Id);
    }

    [Fact]
    public async Task AnAdminUndoRestoresBothOriginalsAndLeavesLaterVisitsOnTheSurvivor()
    {
        var (survivor, merged, request) = await PairAsync();
        await AddVisitAsync(merged.PatientId);
        await AwaitApprovalAsync(request);
        await Merges(clinicianA).ApproveAsync(request);
        await AddVisitAsync(survivor.PatientId); // booked after the merge

        Assert.True(await Merges(admin).UndoAsync(request));

        Assert.Equal(1, await VisitsOfAsync(merged.PatientId));
        Assert.Equal(1, await VisitsOfAsync(survivor.PatientId));
        Assert.Equal(merged.PatientId, (await Registry(reception).SearchAsync(new("IN", merged.Mrn))).Items.Single().Id);
        Assert.Equal([IdentifierScheme.Abha, IdentifierScheme.AbhaAddress], (await Registry(reception).GetAsync(merged.PatientId))!.Identifiers.Select(i => i.Scheme).Order());
        Assert.Equal([IdentifierScheme.Abha], (await Registry(reception).GetAsync(survivor.PatientId))!.Identifiers.Select(i => i.Scheme));
        Assert.Contains("patient.merge.undo", await AuditActionsAsync(survivor.PatientId));
        // The history keeps both lines of the story.
        Assert.Equal(MergeRequestStatus.Unmerged, Assert.Single((await Merges(reception).HistoryAsync(survivor.PatientId))!).Status);
    }

    [Fact]
    public async Task AnUndoAfterTheRetentionWindowIsRefused()
    {
        var (_, merged, request) = await PairAsync();
        await AwaitApprovalAsync(request);
        await Merges(clinicianA).ApproveAsync(request);
        clock.Advance(PatientMerge.UndoWindow + TimeSpan.FromDays(1));

        Assert.Equal("undo_window_over", ErrorCode(await Assert.ThrowsAsync<PatientValidationException>(() => Merges(admin).UndoAsync(request))));
        Assert.NotNull(await sql.Patients(tenant).Patients.Where(p => p.Id == merged.PatientId).Select(p => p.MergedIntoPatientId).SingleAsync());
    }

    [Fact]
    public async Task TheSurvivorsRecordShowsTheMergeHistory()
    {
        var (survivor, merged, request) = await PairAsync();
        await AwaitApprovalAsync(request);
        await Merges(clinicianA).ApproveAsync(request);

        var onSurvivor = Assert.Single((await Merges(reception).HistoryAsync(survivor.PatientId))!);
        Assert.Equal((MergeRequestStatus.Merged, true, merged.Mrn), (onSurvivor.Status, onSurvivor.ThisIsSurvivor, onSurvivor.OtherMrn));
        Assert.Equal((false, survivor.Mrn), (Assert.Single((await Merges(reception).HistoryAsync(merged.PatientId))!) is var m ? (m.ThisIsSurvivor, m.OtherMrn) : default));
    }

    [Fact]
    public async Task AFailingModuleRollsTheWholeMergeBackSoNothingIsLost()
    {
        var (survivor, merged, request) = await PairAsync();
        await AddVisitAsync(merged.PatientId);
        await AwaitApprovalAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Merges(clinicianA, extra: new Failing()).ApproveAsync(request));

        Assert.Equal(MergeRequestStatus.AwaitingApproval, (await Merges(reception).GetAsync(request))!.Status);
        Assert.Null(await sql.Patients(tenant).Patients.Where(p => p.Id == merged.PatientId).Select(p => p.MergedIntoPatientId).SingleAsync());
        Assert.Equal(1, await VisitsOfAsync(merged.PatientId));
        Assert.Equal(2, (await sql.Patients(tenant).Identifiers.Where(i => i.PatientId == merged.PatientId).ToListAsync()).Count);
        Assert.DoesNotContain("patient.merge", await AuditActionsAsync(survivor.PatientId));
    }

    [Fact]
    public async Task AnotherFacilityCannotSeeOrDecideTheRequest()
    {
        var (_, _, request) = await PairAsync();
        var elsewhere = Guid.NewGuid();

        Assert.Null(await Merges(reception, at: elsewhere).GetAsync(request));
        Assert.False(await Merges(reception, at: elsewhere).NotSameAsync(request));
        Assert.Null(await Merges(clinicianA, at: elsewhere).ApproveAsync(request));
        Assert.Empty(await Merges(reception, at: elsewhere).QueueAsync());
        Assert.Equal(MergeRequestStatus.Proposed, (await Merges(reception).GetAsync(request))!.Status);
    }

    [Fact]
    public async Task ACrossTenantRequestIsInvisible()
    {
        var (_, _, request) = await PairAsync();
        var other = Guid.NewGuid();
        var db = sql.Patients(other);
        var stranger = new PatientMerges(db, new PostgresFixture.FixedTenant(other), new PatientRegistryTests.FixedFacility(facility), new User(clinicianA),
            new AuditLog(sql.Audit(other)), clock, []);

        Assert.Null(await stranger.GetAsync(request));
        Assert.Null(await stranger.ApproveAsync(request));
    }
}
