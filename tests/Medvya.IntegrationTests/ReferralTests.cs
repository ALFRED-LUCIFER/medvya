using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-019 on real PostgreSQL: a referral moves one allowed step at a time with evidence, an emailed letter needs the patient's consent
/// at the moment of sending, the letter carries only the sections the doctor chose, received letters are filed with their document, and
/// every call is behind clinical access and audited with the patient.
/// </summary>
public sealed class ReferralTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using var db = sql.Context(h.Tenant);
        var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
        db.DoctorLicences.Add(new Medvya.Tenancy.Domain.DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = h.Clock.GetUtcNow() });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IReferrals Referrals(Guid user) => h.Get<IReferrals>(user);

    async Task<EncounterView> ConsultationAsync(int hour = 10)
    {
        var visit = await h.BookAsync(hour);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        return (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!;
    }

    static CreateReferral Draft(params ReferralSection[] sections) => new("Dr. S. Menon, Endocrinology (synthetic)", ReferralUrgency.Routine, "Uncontrolled HbA1c (synthetic)", sections);

    async Task<Guid> DocumentAsync(Guid patient)
    {
        var id = Guid.CreateVersion7();
        await using var db = sql.Encounters(h.Tenant);
        db.Documents.Add(new ClinicalDocument
        {
            Id = id, TenantId = h.Tenant, FacilityId = h.Facility, PatientId = patient, Type = DocumentType.Referral, DocumentDate = new(2026, 10, 19), Source = ChartSource.Imported,
            ContentType = "application/pdf", SizeBytes = 1, Sha256 = new('0', 64), BlobKey = Guid.NewGuid().ToString("N"), ScanStatus = ScanStatus.Clean, UploadedBy = h.Reception, UploadedAt = h.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    Task<IReadOnlyList<ConsentDecisionInput>?> ConsentAsync(bool granted) => h.Get<IPatientConsents>(h.Doctor).ChangeAsync(h.Patient, new ConsentSet(ConsentPolicy.CurrentVersion, [new(ConsentType.InformationExchange, null, granted)]));

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task AReferralMovesThroughItsStatesWithEvidenceAndEveryStepIsAudited()
    {
        var enc = await ConsultationAsync();
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft(ReferralSection.Medicines)))!;
        Assert.Equal((ReferralState.Draft, true, ReferralDirection.Out), (referral.State, referral.Open, referral.Direction));

        var sent = (await Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Print)))!;
        Assert.Equal((ReferralState.Sent, ReferralChannel.Print), (sent.State, sent.Channel));
        Assert.NotNull(sent.SentAt);
        var booked = (await Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.AppointmentBooked, null, "Booked for 4 Nov")))!;
        var report = await DocumentAsync(h.Patient);
        var received = (await Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.ReportReceived, report, null)))!;
        var closed = (await Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.Closed, null, null)))!;

        Assert.Equal((ReferralState.Closed, false), (closed.State, closed.Open));
        Assert.Equal([ReferralState.Draft, ReferralState.Sent, ReferralState.AppointmentBooked, ReferralState.ReportReceived, ReferralState.Closed], closed.History.Select(s => s.State));
        Assert.Equal(("Booked for 4 Nov", report), (booked.History[2].Note, received.History[3].DocumentId)); // evidence: the document the report came in
        foreach (var action in new[] { "referral.create", "referral.send", "referral.advance" })
            Assert.All(await AuditAsync(action), r => Assert.Equal((AuditOutcome.Allowed, AuditPurpose.Treatment, (Guid?)h.Patient, (Guid?)h.Doctor), (r.Outcome, r.Purpose, r.PatientId, r.ActorUserId)));
        Assert.Equal(3, (await AuditAsync("referral.advance")).Count);
        Assert.Equal("channel:Print", Assert.Single(await AuditAsync("referral.send")).Detail);
    }

    [Fact]
    public async Task OnlyAllowedMovesAreMadeAndAClosedReferralStaysClosed()
    {
        var enc = await ConsultationAsync();
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        // A draft cannot jump over sending; nothing changes and nothing is audited as done.
        Assert.Equal("not_allowed", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.ReportReceived, null, null)))).Code);
        await Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.PatientCarries));
        Assert.Equal("already_sent", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Print)))).Code);
        await Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.Closed, null, null));
        foreach (var back in new[] { ReferralState.Sent, ReferralState.Draft, ReferralState.ReportReceived, ReferralState.Closed })
            Assert.Equal("not_allowed", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(back, null, null)))).Code);
        Assert.Single(await AuditAsync("referral.advance")); // only the move to Closed was done; the refused ones left nothing
    }

    [Fact]
    public async Task AnEmailedLetterNeedsTheConsentAtTheMomentOfSendingAndAPrintedOneDoesNot()
    {
        var enc = await ConsultationAsync();
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        Assert.Equal("consent_required", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Email)))).Code);
        Assert.Empty(await AuditAsync("referral.send")); // refused: nothing was done
        await using (var db = sql.Prescribing(h.Tenant)) Assert.Equal(ReferralState.Draft, (await db.Referrals.SingleAsync()).State);

        await ConsentAsync(true);
        var sent = (await Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Email)))!;
        Assert.Equal((ReferralState.Sent, ReferralChannel.Email), (sent.State, sent.Channel));
        await using (var db = sql.Prescribing(h.Tenant)) Assert.NotNull((await db.Referrals.SingleAsync()).ConsentDecidedAt); // the receipt the send relied on

        await ConsentAsync(false); // withdrawn: the next emailed letter is refused again
        var second = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        Assert.Equal("consent_required", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).SendAsync(second.Id, new SendReferral(ReferralChannel.Email)))).Code);
        Assert.Equal(ReferralChannel.Print, (await Referrals(h.Doctor).SendAsync(second.Id, new SendReferral(ReferralChannel.Print)))!.Channel);
        await using (var db = sql.Prescribing(h.Tenant)) Assert.Null((await db.Referrals.SingleAsync(r => r.Id == second.Id)).ConsentDecidedAt);
    }

    [Fact]
    public async Task TwoSimultaneousSendsSendOnce()
    {
        var enc = await ConsultationAsync();
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Print)); return true; }
            catch (ReferralConflictException) { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
        Assert.Single(await AuditAsync("referral.send"));
    }

    [Fact]
    public async Task TheLetterCarriesOnlyTheSelectedSectionsWithTheirSource()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Problems.Add(new Problem { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Description = "Type 2 diabetes (synthetic)", Verification = ProblemVerification.PatientReported, Source = ChartSource.PatientReported, RecordedAt = h.Clock.GetUtcNow() });
            db.MedicationStatements.Add(new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Metformin", Dose = "500 mg", State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() });
            db.MedicationStatements.Add(new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Stopped drug (synthetic)", State = MedicationState.Stopped, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var enc = await ConsultationAsync();
        var onlyMedicines = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft(ReferralSection.Medicines)))!;
        var letter = (await Referrals(h.Doctor).LetterAsync(onlyMedicines.Id))!;
        Assert.Equal([ReferralSection.Medicines], letter.Sections.Select(s => s.Section));
        var line = Assert.Single(letter.Sections.Single().Lines); // the stopped medicine is not "current"
        Assert.Equal(("Metformin 500 mg", "ClinicianVerified"), (line.Text, line.Source));
        Assert.Equal(("Meera Iyer (synthetic)", "000001", "Adyar (synthetic)"), (letter.Patient.FullName, letter.Patient.Mrn, letter.FacilityName));
        Assert.DoesNotContain(letter.Sections, s => s.Section == ReferralSection.Problems); // not selected: not in the letter

        var problems = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft(ReferralSection.Problems)))!;
        var problemLine = Assert.Single((await Referrals(h.Doctor).LetterAsync(problems.Id))!.Sections.Single().Lines);
        Assert.Equal(("Type 2 diabetes (synthetic)", "PatientReported"), (problemLine.Text, problemLine.Source)); // an unverified fact says so

        var none = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        var before = (await AuditAsync("patient.chart.read")).Count;
        Assert.Empty((await Referrals(h.Doctor).LetterAsync(none.Id))!.Sections);
        Assert.Equal(before, (await AuditAsync("patient.chart.read")).Count); // no section chosen: the chart was not even read
    }

    [Fact]
    public async Task AReceivedLetterIsFiledWithItsDocumentAndOnlyAPatientsOwnDocumentCounts()
    {
        var letter = await DocumentAsync(h.Patient);
        var filed = (await Referrals(h.Doctor).FileInAsync(h.Facility, new FileReferralIn(h.Patient, "Dr. R. Kumar, Cardiology (synthetic)", "Chest pain review (synthetic)", letter)))!;
        Assert.Equal((ReferralDirection.In, ReferralState.Received, true, letter), (filed.Direction, filed.State, filed.Open, filed.LetterDocumentId));
        Assert.Equal(ReferralState.Closed, (await Referrals(h.Doctor).AdvanceAsync(filed.Id, new AdvanceReferral(ReferralState.Closed, null, null)))!.State);
        Assert.Equal("not_allowed", (await Assert.ThrowsAsync<ReferralConflictException>(() => Referrals(h.Doctor).SendAsync(filed.Id, new SendReferral(ReferralChannel.Print)))).Code);

        var theirs = await DocumentAsync(h.SecondPatient);
        Assert.Equal([("documentId", "unknown")], (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).FileInAsync(h.Facility, new FileReferralIn(h.Patient, "Dr. X", null, theirs)))).Errors);
        Assert.Equal([("documentId", "required")], (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).FileInAsync(h.Facility, new FileReferralIn(h.Patient, "Dr. X", null, null)))).Errors);
        Assert.Null(await Referrals(h.Doctor).FileInAsync(h.Facility, new FileReferralIn(Guid.NewGuid(), "Dr. X", null, letter))); // no such patient here
        Assert.Null(await Referrals(h.Doctor).LetterAsync(filed.Id)); // a letter is written for referrals out only
    }

    [Fact]
    public async Task EvidenceMustBeThePatientsOwnDocument()
    {
        var enc = await ConsultationAsync();
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        await Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(ReferralChannel.Print));
        var theirs = await DocumentAsync(h.SecondPatient);
        Assert.Equal([("documentId", "unknown")], (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.ReportReceived, theirs, null)))).Errors);
        Assert.Equal(ReferralState.Sent, (await Referrals(h.Doctor).ForPatientAsync(h.Patient, h.Facility))!.Single().State);
    }

    [Fact]
    public async Task Patient360ListsOpenReferralsFirstAndOnlyTheClinicsOwn()
    {
        var enc = await ConsultationAsync();
        var closed = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        await Referrals(h.Doctor).SendAsync(closed.Id, new SendReferral(ReferralChannel.Print));
        await Referrals(h.Doctor).AdvanceAsync(closed.Id, new AdvanceReferral(ReferralState.Closed, null, null));
        var open = (await Referrals(h.Doctor).CreateAsync(enc.Id, new CreateReferral("Dr. R. Kumar (synthetic)", ReferralUrgency.Urgent, "Urgent review (synthetic)", [])))!;

        var list = (await Referrals(h.Doctor).ForPatientAsync(h.Patient, h.Facility))!;
        Assert.Equal([open.Id, closed.Id], list.Select(r => r.Id));
        Assert.Equal([true, false], list.Select(r => r.Open));
        Assert.Null(await Referrals(h.Doctor).ForPatientAsync(Guid.NewGuid(), h.Facility)); // no such patient

        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            await using var db = sql.Prescribing(other.Tenant);
            Assert.Equal(0, await db.Referrals.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.ReferralEvents.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            await other.CloseAsync();
        }
        Assert.Single(await AuditAsync("referral.read"));
    }

    [Fact]
    public async Task InvalidInputIsRefusedWithCodesAndOnlyOrderingDoctorsAct()
    {
        var enc = await ConsultationAsync();
        Assert.Equal([("provider", "required"), ("reason", "required"), ("urgency", "required")],
            (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).CreateAsync(enc.Id, new CreateReferral(" ", null, null, null)))).Errors.Order());
        Assert.Equal([("provider", "too_long"), ("reason", "too_long"), ("sections", "invalid")],
            (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).CreateAsync(enc.Id,
                new CreateReferral(new string('p', 201), ReferralUrgency.Routine, new string('r', 501), [(ReferralSection)9])))).Errors.Order());
        var referral = (await Referrals(h.Doctor).CreateAsync(enc.Id, Draft()))!;
        Assert.Equal([("channel", "required")], (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).SendAsync(referral.Id, new SendReferral(null)))).Errors);
        Assert.Equal([("note", "too_long")], (await Assert.ThrowsAsync<ReferralValidationException>(() => Referrals(h.Doctor).AdvanceAsync(referral.Id, new AdvanceReferral(ReferralState.Sent, null, new string('n', 201))))).Errors);

        foreach (var user in new[] { h.Nurse, h.Reception, h.Admin })
        {
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Referrals(user).CreateAsync(enc.Id, Draft()));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Referrals(user).SendAsync(referral.Id, new SendReferral(ReferralChannel.Print)));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => Referrals(user).LetterAsync(referral.Id));
        }
        Assert.Null(await Referrals(h.Doctor).CreateAsync(Guid.NewGuid(), Draft()));
        Assert.Null(await Referrals(h.Doctor).SendAsync(Guid.NewGuid(), new SendReferral(ReferralChannel.Print)));
    }

    [Fact]
    public async Task AMergeMovesReferralsToTheSurvivorAndAnUndoMovesThemBack()
    {
        var enc = await ConsultationAsync();
        await Referrals(h.Doctor).CreateAsync(enc.Id, Draft());
        var participant = new OrderMergeParticipant(new PostgresFixture.FixedTenant(h.Tenant));
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.MoveAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        await using (var db = sql.Prescribing(h.Tenant))
            Assert.Equal((h.SecondPatient, (Guid?)h.Patient), await db.Referrals.Select(r => new ValueTuple<Guid, Guid?>(r.PatientId, r.MergedFromPatientId)).SingleAsync());
        await using (var conn = await sql.OpenAs("app_user", h.Tenant))
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await participant.RestoreAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None);
            await tx.CommitAsync();
        }
        await using (var db = sql.Prescribing(h.Tenant))
            Assert.Equal((h.Patient, (Guid?)null), await db.Referrals.Select(r => new ValueTuple<Guid, Guid?>(r.PatientId, r.MergedFromPatientId)).SingleAsync());
    }

    [Fact]
    public void TheRulesAllowOnlyForwardMovesAndConsentOnlyWhereTheClinicDiscloses()
    {
        var all = Enum.GetValues<ReferralState>();
        foreach (var from in all) Assert.False(ReferralRules.CanMove(from, from));
        Assert.All(all, s => Assert.False(ReferralRules.CanMove(ReferralState.Closed, s)));
        Assert.All(all.Where(s => s is not ReferralState.Closed), s => Assert.True(ReferralRules.CanMove(s, ReferralState.Closed))); // any open referral can be closed
        Assert.False(ReferralRules.CanMove(ReferralState.Sent, ReferralState.Draft));
        Assert.True(ReferralRules.NeedsConsent(ReferralChannel.Email));
        Assert.False(ReferralRules.NeedsConsent(ReferralChannel.Print));
        Assert.False(ReferralRules.NeedsConsent(ReferralChannel.PatientCarries));
        Assert.False(ReferralRules.IsOpen(ReferralState.Closed));
    }
}
