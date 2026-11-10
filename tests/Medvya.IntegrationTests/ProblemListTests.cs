using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-010 on real PostgreSQL: a coded problem keeps its code system and version, verification, author and encounter; a code outside the
/// country's set is refused and a blank one stays blank; provisional problems are never marked confirmed; only a doctor with a care relationship
/// may read or change the list, and every call is audited without the diagnosis in it. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class ProblemListTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    IProblemList List(Guid user) => h.Get<IProblemList>(user);

    async Task<Guid> InConsultAsync(int hour = 10, Guid? patient = null)
    {
        var visit = await h.BookAsync(hour, patient: patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        return (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id, LeaveOpen: true)))!.Id;
    }

    static AddProblem Coded(string code, ProblemVerification v = ProblemVerification.Verified, string? description = null, DateOnly? onset = null) => new(code, description, v, onset);

    [Fact]
    public async Task ACodedProblemKeepsItsCodeSystemVersionVerificationAuthorAndEncounter()
    {
        var encounter = await InConsultAsync();
        var view = (await List(h.Doctor).AddAsync(encounter, Coded("e11.9", onset: new DateOnly(2019, 6, 1))))!;

        var row = Assert.Single(view.Rows);
        Assert.Equal(("E11.9", "ICD-10", "2019-medvya-starter-1", "Type 2 diabetes mellitus without complications"), (row.Code, row.CodeSystem, row.CodeSystemVersion, row.Description));
        Assert.Equal((ProblemVerification.Verified, ProblemStatus.Active, true, true, ChartSource.ClinicianVerified), (row.Verification, row.Status, row.AddedHere, row.Confirmed, row.Source));
        await using var db = sql.Encounters(h.Tenant);
        var stored = await db.Problems.SingleAsync();
        Assert.Equal((encounter, (Guid?)h.Doctor, h.Patient, h.Clock.GetUtcNow()), (stored.EncounterId!.Value, stored.RecordedBy, stored.PatientId, stored.RecordedAt));
        Assert.Equal(new DateOnly(2019, 6, 1), stored.OnsetOn);

        // Read back in a later consultation: the same problem, no longer "added today".
        var later = await InConsultAsync(11, h.SecondPatient);
        Assert.Empty((await List(h.Doctor).GetAsync(later))!.Rows); // another patient's list
        var chart = await h.Get<IPatientChart>(h.Doctor).GetAsync(h.Patient, h.Facility);
        Assert.Equal(("E11.9", ProblemStatus.Active), (Assert.Single(chart.Problems).Icd10Code, chart.Problems[0].Status));
    }

    /// <summary>SAFETY (unknowns stay blank), mutation check: drop the "unknown" refusal in AddAsync and a code outside the set is stored.</summary>
    [Fact]
    public async Task ACodeOutsideTheSetIsRefusedAndABlankCodeStaysBlank()
    {
        var encounter = await InConsultAsync();
        var list = List(h.Doctor);
        Assert.Contains(("code", "unknown"), (await Assert.ThrowsAsync<ProblemValidationException>(() => list.AddAsync(encounter, Coded("E11.99")))).Errors);
        Assert.Equal([("description", "required"), ("verification", "required")], (await Assert.ThrowsAsync<ProblemValidationException>(() => list.AddAsync(encounter, new AddProblem(" ", " ", null, null)))).Errors);
        Assert.Contains(("onsetOn", "future"), (await Assert.ThrowsAsync<ProblemValidationException>(() => list.AddAsync(encounter, Coded("I10", onset: new DateOnly(2030, 1, 1))))).Errors);
        Assert.Contains(("description", "too_long"), (await Assert.ThrowsAsync<ProblemValidationException>(() => list.AddAsync(encounter, new AddProblem(null, new string('x', 201), ProblemVerification.Provisional, null)))).Errors);

        var row = Assert.Single((await list.AddAsync(encounter, new AddProblem(null, "Chest tightness on exertion (synthetic)", ProblemVerification.Provisional, null)))!.Rows);
        Assert.Equal((null, null, null), (row.Code, row.CodeSystem, row.CodeSystemVersion)); // never a guessed code
        Assert.False(row.Confirmed); // provisional: never passed on as a confirmed diagnosis
        Assert.Equal(ChartSource.ClinicianVerified, row.Source);
        var reported = (await list.AddAsync(encounter, Coded("J45.9", ProblemVerification.PatientReported, "Asthma as a child (synthetic)")))!.Rows.Single(r => r.Code == "J45.9");
        Assert.Equal(("Asthma as a child (synthetic)", ChartSource.PatientReported, false), (reported.Description, reported.Source, reported.Confirmed));
    }

    [Fact]
    public async Task AnActiveCodeIsNotAddedTwiceButAResolvedOneMayReturn()
    {
        var encounter = await InConsultAsync();
        var list = List(h.Doctor);
        var first = Assert.Single((await list.AddAsync(encounter, Coded("J06.9")))!.Rows);
        Assert.Equal("duplicate", (await Assert.ThrowsAsync<ProblemConflictException>(() => list.AddAsync(encounter, Coded("J06.9")))).Code);
        await list.UpdateAsync(encounter, first.Id, new UpdateProblem(ProblemVerification.Verified, ProblemStatus.Resolved, null));
        var rows = (await list.AddAsync(encounter, Coded("J06.9")))!.Rows;
        Assert.Equal([ProblemStatus.Active, ProblemStatus.Resolved], rows.Select(r => r.Status)); // active first
    }

    [Fact]
    public async Task AnUpdateChangesVerificationStatusAndOnsetAndRecordsWho()
    {
        var encounter = await InConsultAsync();
        var row = Assert.Single((await List(h.Doctor).AddAsync(encounter, Coded("I10", ProblemVerification.Provisional)))!.Rows);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var updated = Assert.Single((await List(h.Doctor).UpdateAsync(encounter, row.Id, new UpdateProblem(ProblemVerification.Verified, ProblemStatus.Active, new DateOnly(2024, 2, 1))))!.Rows);
        Assert.Equal((ProblemVerification.Verified, true, new DateOnly(2024, 2, 1), "I10"), (updated.Verification, updated.Confirmed, updated.OnsetOn!.Value, updated.Code));
        await using var db = sql.Encounters(h.Tenant);
        var stored = await db.Problems.SingleAsync();
        Assert.Equal(((Guid?)h.Doctor, (DateTimeOffset?)h.Clock.GetUtcNow()), (stored.UpdatedBy, stored.UpdatedAt));
        Assert.Equal([("status", "required")], (await Assert.ThrowsAsync<ProblemValidationException>(() => List(h.Doctor).UpdateAsync(encounter, row.Id, new UpdateProblem(ProblemVerification.Verified, null, null)))).Errors);
    }

    /// <summary>SAFETY (IDOR), mutation check: drop the patient match in UpdateAsync and another patient's problem is changed from this consultation.</summary>
    [Fact]
    public async Task OnlyAProblemOfTheConsultationsPatientCanBeChanged()
    {
        var mine = await InConsultAsync();
        var theirs = await InConsultAsync(11, h.SecondPatient);
        var other = Assert.Single((await List(h.Doctor).AddAsync(theirs, Coded("K21.9")))!.Rows);
        Assert.Null(await List(h.Doctor).UpdateAsync(mine, other.Id, new UpdateProblem(ProblemVerification.Provisional, ProblemStatus.Resolved, null)));
        Assert.Null(await List(h.Doctor).UpdateAsync(mine, Guid.NewGuid(), new UpdateProblem(ProblemVerification.Provisional, ProblemStatus.Resolved, null)));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(ProblemStatus.Active, (await db.Problems.SingleAsync()).Status);
        Assert.Null(await List(h.Doctor).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AnEndedConsultationIsReadOnly()
    {
        var encounter = await InConsultAsync();
        var row = Assert.Single((await List(h.Doctor).AddAsync(encounter, Coded("M54.5")))!.Rows);
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
        Assert.Equal("ended", (await Assert.ThrowsAsync<ProblemConflictException>(() => List(h.Doctor).AddAsync(encounter, Coded("R51")))).Code);
        Assert.Equal("ended", (await Assert.ThrowsAsync<ProblemConflictException>(() => List(h.Doctor).UpdateAsync(encounter, row.Id, new UpdateProblem(ProblemVerification.Verified, ProblemStatus.Resolved, null)))).Code);
        Assert.True((await List(h.Doctor).GetAsync(encounter))!.Locked);
    }

    /// <summary>SAFETY, mutation check: drop clinical.RequireAsync in FindAsync (or add Limited to Doctors) and a nurse or receptionist reads and codes diagnoses.</summary>
    [Fact]
    public async Task OnlyADoctorWithACareRelationshipMayCodeAndEveryCallIsAuditedWithoutTheDiagnosis()
    {
        var encounter = await InConsultAsync();
        foreach (var user in new[] { h.Reception, h.Nurse, h.Admin })
        {
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => List(user).GetAsync(encounter));
            await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => List(user).AddAsync(encounter, Coded("I10")));
        }
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).GetAsync(new Uri($"/v1/encounters/{encounter}/problems", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).GetAsync(new Uri("/v1/diagnosis-codes", UriKind.Relative))).StatusCode);
        }
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        Assert.True((await Assert.ThrowsAsync<ClinicalAccessDeniedException>(() => List(h.SecondDoctor).AddAsync(encounter, Coded("I10")))).CanRequest);

        await List(h.Doctor).AddAsync(encounter, Coded("F32.9", description: "Low mood (synthetic)"));
        await List(h.Doctor).GetAsync(encounter);
        await using var db = sql.Encounters(h.Tenant);
        Assert.Empty(await db.Problems.Where(p => p.Icd10Code == "I10").ToListAsync()); // nothing was written by a refused caller
        await using var audit = sql.Audit(h.Tenant);
        var events = await audit.Events.Where(e => e.Action.StartsWith("problem.")).ToListAsync();
        Assert.Contains(events, e => e.Action == ProblemListService.AddAction && e.Outcome == AuditOutcome.Allowed && e.PatientId == h.Patient && e.ResourceId == encounter);
        Assert.Contains(events, e => e.Action == ProblemListService.ReadAction && e.Outcome == AuditOutcome.Allowed);
        Assert.DoesNotContain(events, e => (e.ToString() + e.Action).Contains("F32", StringComparison.OrdinalIgnoreCase) || e.ToString()!.Contains("mood", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OverHttpTheListAnswersWithCodesAndStatuses()
    {
        var encounter = await InConsultAsync();
        var client = Client(h.Doctor);
        var set = (await client.GetFromJsonAsync<DiagnosisCodeSet>(new Uri("/v1/diagnosis-codes", UriKind.Relative)))!;
        Assert.Equal(("ICD-10", "2019-medvya-starter-1"), (set.System, set.Version));
        Assert.Contains(set.Codes, c => c.Code == "I10");

        var path = new Uri($"/v1/encounters/{encounter}/problems", UriKind.Relative);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { code = "NOT-A-CODE", verification = "Verified" })).StatusCode);
        var added = await client.PostAsJsonAsync(path, new { code = "I10", verification = "Provisional" });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var row = Assert.Single((await added.Content.ReadFromJsonAsync<ProblemListView>())!.Rows);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new { code = "I10", verification = "Verified" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(new Uri($"{path}/{row.Id}", UriKind.Relative), new { verification = "Verified", status = "Resolved" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(new Uri($"{path}/{Guid.NewGuid()}", UriKind.Relative), new { verification = "Verified", status = "Resolved" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri($"/v1/encounters/{Guid.NewGuid()}/problems", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AMergeMovesProblemsToTheSurvivorAndAnUndoMovesThemBack()
    {
        var encounter = await InConsultAsync();
        await List(h.Doctor).AddAsync(encounter, Coded("E78.5"));
        foreach (var undo in new[] { false, true })
        {
            await using (var db = sql.Encounters(h.Tenant))
            await using (var conn = await sql.OpenAs("app_user", h.Tenant))
            await using (var tx = await conn.BeginTransactionAsync())
            {
                var participant = new EncounterMergeParticipant(db, new PostgresFixture.FixedTenant(h.Tenant));
                await (undo ? participant.RestoreAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None) : participant.MoveAsync(h.Patient, h.SecondPatient, tx, CancellationToken.None));
                await tx.CommitAsync();
            }
            await using var check = sql.Encounters(h.Tenant);
            Assert.Equal(undo ? (h.Patient, (Guid?)null) : (h.SecondPatient, h.Patient), await check.Problems.Select(p => new ValueTuple<Guid, Guid?>(p.PatientId, p.MergedFromPatientId)).SingleAsync());
        }
    }

    [Fact]
    public void EachCountryHasAVersionedCodeSetOfWellFormedUniqueCodesAndSearchIsFast()
    {
        foreach (var country in new[] { "IN", "AE" })
        {
            var set = DiagnosisCodeRules.For(country).CodeSet();
            Assert.Equal(("ICD-10", "2019-medvya-starter-1"), (set.System, set.Version)); // ADR-017: the UAE moves to ICD-10-CM with claims (P5)
            Assert.All(set.Codes, c => Assert.Matches(@"^[A-Z]\d{2}(\.\d)?$", c.Code));
            Assert.All(set.Codes, c => Assert.InRange(c.Title.Length, 3, ProblemListService.MaxDescription));
            Assert.Equal(set.Codes.Count, set.Codes.Select(c => c.Code).Distinct().Count());
            Assert.Null(set.Find("E11.99"));
            Assert.Equal("I10", set.Find(" i10 ")!.Code);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosisCodeRules.For("US"));

        // Search runs in the browser over this list (diagnosisCodes.ts); the server side of "< 300 ms" is one lookup per add.
        var codes = DiagnosisCodeRules.India.CodeSet();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) _ = codes.Find("Z72.0");
        Assert.True(watch.ElapsedMilliseconds < 300, $"{watch.ElapsedMilliseconds} ms");
    }
}
