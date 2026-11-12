using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Azure.Storage.Blobs;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-017 over HTTP on real PostgreSQL: authorising the prescription is its own action (Sign on Prescriptions and orders, a care relationship,
/// a re-authentication in the last 5 minutes, the safety checks run again), frozen as an append-only version; only an authorised, unchanged
/// prescription prints; the PDF renders server-side on A4 and A5 and is stored with its hash; the QR verify check is public and shows validity only;
/// each doctor's layout approval is recorded. SAFETY mutation checks are named on the tests. Synthetic data only.
/// </summary>
public sealed class RxAuthorisationTests(PostgresFixture sql, AzuriteFixture blobs) : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.NoKnownAllergiesAsync(h.Patient);
        await h.DoctorTemplateAsync();
        // The doctor's profile and the licence the owner attested (ADR-016): what the paper prints.
        await using (var db = sql.Context(h.Tenant))
        {
            var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
            db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = h.Clock.GetUtcNow() });
            db.DoctorProfiles.Add(new DoctorProfile { MembershipId = membership.Id, TenantId = h.Tenant, FullName = "Dr. Arjun Rao (synthetic)", Qualifications = "MBBS, MD (General Medicine)" });
            await db.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test/app")
            .UseSetting("RateLimiting:SignInPerMinute", "1000")
            .UseSetting("Storage:ConnectionString", blobs.ConnectionString) // the stored copies, the logo and the signature: a real Azurite
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    /// <summary>Re-authenticated <paramref name="minutesAgo"/> minutes before the test clock; null: never. No user: not signed in.</summary>
    HttpClient Client(Guid? user, int? minutesAgo = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is null) return client;
        client.DefaultRequestHeaders.Add("X-Test-User", user.Value.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (minutesAgo is { } m) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().AddMinutes(-m).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    static async Task<string?> Code(HttpResponseMessage r) => (await Json(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    /// <summary>A formulary medicine with a complete instruction, added by the doctor.</summary>
    async Task<Guid> PrescribeAsync(string generic, UpdateRxLine sig)
    {
        var rx = h.Get<IPrescriptions>(h.Doctor);
        var id = (await h.Get<IFormulary>(h.Doctor).SearchAsync(generic, 1)).Single().Id;
        var line = (await rx.AddAsync(encounter, new AddRxLine(id, null, null, null)))!.Last(l => l.FormularyItemId == id).Id!.Value;
        await rx.UpdateAsync(line, sig);
        return line;
    }

    static readonly UpdateRxLine Twice = new("1", "tablet", "oral", "BD", "after_food", 5, 10, null);

    Task<HttpResponseMessage> AuthoriseAsync(Guid user, int? reauthMinutesAgo = 0) =>
        Client(user, reauthMinutesAgo).PostAsync(U($"/v1/encounters/{encounter}/prescription/authorisation"), null);

    Task<HttpResponseMessage> PdfAsync(string? paper = null) =>
        Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/prescription/pdf{(paper is null ? "" : $"?paper={paper}")}"), null);

    Task<HttpResponseMessage> VerifyAsync(string token) => Client(null).PostAsJsonAsync(U("/v1/prescriptions/verify"), new { token });

    async Task<List<AuthorisedPrescription>> AuthorisedAsync()
    {
        await using var db = sql.Prescribing(h.Tenant);
        return await db.AuthorisedPrescriptions.AsNoTracking().OrderBy(p => p.VersionNo).ToListAsync();
    }

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    async Task<Guid?> BrokenChainAsync(string table)
    {
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        return await new NpgsqlCommand($"""SELECT ledger.verify_chain('prescribing.{table}', '{h.Tenant}')""", conn).ExecuteScalarAsync() is Guid g ? g : null;
    }

    /// <summary>The token in the print view's verify link.</summary>
    async Task<string> TokenAsync()
    {
        using var print = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/print"));
        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        return (await Json(print)).GetProperty("verifyUrl").GetString()!.Split('/')[^1];
    }

    // SAFETY mutation check: remove the re-authentication check in PrescriptionService.AuthoriseAsync and the reauth_required cases fail;
    // drop PrintableAsync's not_authorised throw and the unauthorised print and PDF cases fail.
    [Fact]
    public async Task AnUnauthorisedPrescriptionDoesNotPrintAndAuthorisingNeedsAFreshStepUpAndIsAudited()
    {
        await PrescribeAsync("Metformin", Twice);
        using (var print = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/print")))
            Assert.Equal((HttpStatusCode.Conflict, "not_authorised"), (print.StatusCode, await Code(print)));
        using (var pdf = await PdfAsync()) Assert.Equal((HttpStatusCode.Conflict, "not_authorised"), (pdf.StatusCode, await Code(pdf)));

        foreach (var stale in new int?[] { null, 6 })
        {
            using var r = await AuthoriseAsync(h.Doctor, stale);
            Assert.Equal((HttpStatusCode.Forbidden, "reauth_required"), (r.StatusCode, await Code(r)));
        }
        Assert.Empty(await AuthorisedAsync());

        using var ok = await AuthoriseAsync(h.Doctor, 4);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var state = await Json(ok);
        Assert.Equal((1, false), (state.GetProperty("authorised").GetProperty("versionNo").GetInt32(), state.GetProperty("changed").GetBoolean()));
        var row = Assert.Single(await AuthorisedAsync());
        Assert.Equal((h.Doctor, h.Clock.GetUtcNow(), SafetyRules.Version), (row.AuthorisedBy, row.AuthorisedAt, row.RuleSetVersion));
        Assert.Equal("Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10.", Assert.Single(row.Lines).Sig["en"]);

        var rows = await AuditAsync("rx.authorise");
        Assert.Equal(2, rows.Count(e => e.Outcome != AuditOutcome.Allowed)); // each refused attempt is audited too
        var audited = Assert.Single(rows, e => e.Outcome == AuditOutcome.Allowed);
        Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, (Guid?)h.Doctor), (audited.Outcome, audited.PatientId, audited.ActorUserId));
        Assert.Equal($"version:1;lines:1;rules:{SafetyRules.Version};reauth:ok", audited.Detail); // codes only, never a medicine
        Assert.Empty(await AuditAsync("note.sign")); // separate from the note: the note is still a draft

        using (var again = await AuthoriseAsync(h.Doctor)) Assert.Equal(1, (await Json(again)).GetProperty("authorised").GetProperty("versionNo").GetInt32());
        Assert.Single(await AuthorisedAsync()); // the same draft twice: nothing new is written
    }

    // SAFETY mutation check: remove the Unresolved > 0 gate in PrescriptionService.AuthoriseAsync and this fails (an overdose would be authorised).
    [Fact]
    public async Task AuthorisingRunsTheSafetyChecksAgainAndRefusesWhileASevereAlertHasNoReason()
    {
        await PrescribeAsync("Paracetamol", new UpdateRxLine("3", "tablet", "oral", "QID", null, 3, 36, null)); // 6 g a day
        using (var refused = await AuthoriseAsync(h.Doctor))
            Assert.Equal((HttpStatusCode.Conflict, "unresolved_alerts"), (refused.StatusCode, await Code(refused)));
        Assert.Empty(await AuthorisedAsync());

        var alert = (await h.Get<IPrescriptions>(h.Doctor).CheckAsync(encounter))!.Alerts.Single(a => a.Severity == RxSeverity.Severe);
        await h.Get<IPrescriptions>(h.Doctor).OverrideAsync(encounter, new OverrideRxAlert(alert.Key, "Loading dose, reviewed (synthetic)"));
        using var ok = await AuthoriseAsync(h.Doctor);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Single(await AuthorisedAsync());
    }

    // MDV audit P0-2. SAFETY mutation check: make AllergiesNotRecorded Moderate again in SafetyRules.Evaluate and this fails.
    [Fact]
    public async Task AllergiesNotRecordedNeedAReasonToAuthorise()
    {
        // The second patient has nothing on the chart: no allergy, no "no known allergies". One open consultation at a time.
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
        var visit = await h.BookAsync(11, patient: h.SecondPatient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
        await PrescribeAsync("Metformin", Twice);
        using (var refused = await AuthoriseAsync(h.Doctor))
            Assert.Equal((HttpStatusCode.Conflict, "unresolved_alerts"), (refused.StatusCode, await Code(refused)));
        Assert.Empty(await AuthorisedAsync());

        await h.Get<IPrescriptions>(h.Doctor).OverrideAsync(encounter, new OverrideRxAlert("allergies:not_recorded", "Patient unconscious, no relative present (synthetic)"));
        using var ok = await AuthoriseAsync(h.Doctor);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // SAFETY mutation check: drop the care-relationship check (RequireAsync in AuthoriseAsync) and the colleague case fails.
    [Fact]
    public async Task OnlyADoctorWithACareRelationshipAndALicenceAuthorises()
    {
        await PrescribeAsync("Metformin", Twice);
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var r = await AuthoriseAsync(user);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        using (var unlicensed = await AuthoriseAsync(h.SecondDoctor)) // a doctor with no licence on record cannot put one on paper
            Assert.Equal((HttpStatusCode.Conflict, "no_licence"), (unlicensed.StatusCode, await Code(unlicensed)));
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var colleague = await AuthoriseAsync(h.SecondDoctor))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, await Code(colleague)));
        using (var unknown = await Client(h.Doctor, 0).PostAsync(U($"/v1/encounters/{Guid.NewGuid()}/prescription/authorisation"), null))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(await AuthorisedAsync());
    }

    [Fact]
    public async Task AnIncompleteOrEmptyDraftIsNotAuthorised()
    {
        using (var empty = await AuthoriseAsync(h.Doctor)) Assert.Equal((HttpStatusCode.Conflict, "nothing_to_authorise"), (empty.StatusCode, await Code(empty)));
        await PrescribeAsync("Metformin", new UpdateRxLine(null, null, null, null, null, null, null, null));
        using (var blank = await AuthoriseAsync(h.Doctor)) Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode); // lines: no_instructions
        Assert.Empty(await AuthorisedAsync());
    }

    // SAFETY mutation check: drop AddLedger for AuthorisedPrescriptions (migration RxAuthorisation) and the database UPDATE succeeds; drop the
    // Changed check in PrintableAsync and the changed draft prints; return the stored version without MaxAsync in PrescriptionVerification and v1 verifies as valid.
    [Fact]
    public async Task AnAuthorisedPrescriptionIsNeverEditedAChangedDraftIsANewVersionThatReplacesTheOld()
    {
        var line = await PrescribeAsync("Metformin", Twice);
        using (await AuthoriseAsync(h.Doctor)) { }
        var first = await TokenAsync();

        await h.Get<IPrescriptions>(h.Doctor).UpdateAsync(line, Twice with { Dose = "2" });
        using (var state = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/authorisation")))
            Assert.True((await Json(state)).GetProperty("changed").GetBoolean());
        using (var print = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/print")))
            Assert.Equal((HttpStatusCode.Conflict, "changed"), (print.StatusCode, await Code(print)));
        Assert.Equal("Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10.", Assert.Single(await AuthorisedAsync()).Lines[0].Sig["en"]); // v1 as it was

        using (var again = await AuthoriseAsync(h.Doctor)) Assert.Equal(2, (await Json(again)).GetProperty("authorised").GetProperty("versionNo").GetInt32());
        var second = await TokenAsync();
        var rows = await AuthorisedAsync();
        Assert.Equal(["Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10.", "Take 2 tablets by mouth twice a day after food for 5 days. Quantity: 10."],
            rows.Select(r => r.Lines[0].Sig["en"]));
        using (var old = await VerifyAsync(first)) Assert.Equal("Replaced", (await Json(old)).GetProperty("status").GetString());
        using (var current = await VerifyAsync(second)) Assert.Equal("Valid", (await Json(current)).GetProperty("status").GetString());

        Assert.Null(await BrokenChainAsync("AuthorisedPrescriptions"));
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."AuthorisedPrescriptions" SET "Lines" = '[]'""", conn).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""DELETE FROM prescribing."AuthorisedPrescriptions" """, conn).ExecuteNonQueryAsync()); // no right, and the ledger refuses
    }

    Task<HttpResponseMessage> CancelAsync(Guid user, object body, int? reauthMinutesAgo = 0) =>
        Client(user, reauthMinutesAgo).PostAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/cancellation"), body);

    // SAFETY mutation check: remove RequireFreshReauth from PrescriptionService.CancelAsync and the stale cancellations go through; drop the cancelled
    // throw in PrintableAsync and the cancelled prescription prints; drop the cancellation look-up in PrescriptionVerification and it verifies as valid;
    // drop AddLedger for PrescriptionCancellations (migration RxCancellations) and the database UPDATE succeeds.
    [Fact]
    public async Task CancellingNeedsAFreshStepUpAndAReasonThenItNeverPrintsAndVerifiesAsCancelled()
    {
        await PrescribeAsync("Metformin", Twice);
        using (var none = await CancelAsync(h.Doctor, new { reason = "Synthetic" })) Assert.Equal((HttpStatusCode.Conflict, "not_authorised"), (none.StatusCode, await Code(none)));
        using (await AuthoriseAsync(h.Doctor)) { }
        var first = await TokenAsync();

        foreach (var stale in new int?[] { null, 6 })
        {
            using var r = await CancelAsync(h.Doctor, new { reason = "Synthetic" }, stale);
            Assert.Equal((HttpStatusCode.Forbidden, "reauth_required"), (r.StatusCode, await Code(r)));
        }
        foreach (var reason in new[] { "", new string('x', 201) })
        {
            using var r = await CancelAsync(h.Doctor, new { reason });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var r = await CancelAsync(user, new { reason = "Synthetic" });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        await using (var db = sql.Prescribing(h.Tenant)) Assert.Equal(0, await db.PrescriptionCancellations.CountAsync());

        using (var ok = await CancelAsync(h.Doctor, new { reason = "Wrong dose written (synthetic)" }))
        {
            var cancellation = (await Json(ok)).GetProperty("authorised").GetProperty("cancellation");
            Assert.Equal(("Wrong dose written (synthetic)", h.Doctor), (cancellation.GetProperty("reason").GetString(), cancellation.GetProperty("cancelledBy").GetGuid()));
        }
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        using (var twice = await CancelAsync(h.Doctor, new { reason = "Again (synthetic)" })) // the first cancellation stands
            Assert.Equal("Wrong dose written (synthetic)", (await Json(twice)).GetProperty("authorised").GetProperty("cancellation").GetProperty("reason").GetString());
        using (var print = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/print")))
            Assert.Equal((HttpStatusCode.Conflict, "cancelled"), (print.StatusCode, await Code(print)));
        using (var pdf = await PdfAsync()) Assert.Equal((HttpStatusCode.Conflict, "cancelled"), (pdf.StatusCode, await Code(pdf)));
        using (var old = await VerifyAsync(first)) Assert.Equal("Cancelled", (await Json(old)).GetProperty("status").GetString());
        Assert.Equal(["version:1;reauth:ok"], (await AuditAsync("rx.cancel")).Where(e => e.Outcome == AuditOutcome.Allowed).Select(e => e.Detail)); // refusals are audited Denied

        // The same draft authorised again is a new version; the cancelled paper stays cancelled.
        using (var again = await AuthoriseAsync(h.Doctor)) Assert.Equal(2, (await Json(again)).GetProperty("authorised").GetProperty("versionNo").GetInt32());
        using (var current = await VerifyAsync(await TokenAsync())) Assert.Equal("Valid", (await Json(current)).GetProperty("status").GetString());
        using (var old = await VerifyAsync(first)) Assert.Equal("Cancelled", (await Json(old)).GetProperty("status").GetString());

        Assert.Null(await BrokenChainAsync("PrescriptionCancellations"));
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."PrescriptionCancellations" SET "Reason" = 'x'""", conn).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""DELETE FROM prescribing."PrescriptionCancellations" """, conn).ExecuteNonQueryAsync());
    }

    // SAFETY mutation check (MDV-1087): drop the ReplaceAsync call in AuthoriseAsync and the chart, timeline and next-visit facts stay empty; drop it in
    // CancelAsync and the cancelled lines stay active; drop the m.EncounterId != encounter.Id filter in ChecksAsync and the same visit's checks gain a duplicate.
    [Fact]
    public async Task AuthorisedLinesAreTheChartsMedicinesLinkedToTheVisitAndACancellationStopsThem()
    {
        // Clopidogrel with omeprazole is an interaction: on the chart the pair must not be found a second time against this visit's own lines.
        var line = await PrescribeAsync("Clopidogrel", Twice);
        await PrescribeAsync("Omeprazole", new("1", "capsule", "oral", "OD", null, 5, 5, null));
        var rx = h.Get<IPrescriptions>(h.Doctor);
        var before = (await rx.CheckAsync(encounter))!.Alerts.Select(a => a.Key).ToList();
        using (await AuthoriseAsync(h.Doctor)) { }
        var v1 = Assert.Single(await AuthorisedAsync()).Id;

        async Task<List<MedicineView>> ChartAsync() => [.. (await h.Get<IPatientChart>(h.Doctor).GetAsync(h.Patient, h.Facility)).Medicines.Where(m => m.Name.StartsWith("Clopidogrel", StringComparison.Ordinal))];
        Assert.Contains("interaction:clopidogrel_omeprazole", string.Join(' ', before), StringComparison.Ordinal);
        var medicine = Assert.Single(await ChartAsync());
        Assert.Equal("Clopidogrel 75 mg", medicine.Name); // with its strength
        Assert.Equal(("Take 1 tablet by mouth twice a day after food for 5 days.", MedicationState.Active, ChartSource.ClinicianVerified, (Guid?)encounter),
            (medicine.Dose, medicine.State, medicine.Source, medicine.EncounterId));
        await using (var db = sql.Encounters(h.Tenant)) Assert.All(await db.MedicationStatements.ToListAsync(), m => Assert.Equal(v1, m.PrescriptionId));

        using (var timeline = await Client(h.Doctor).GetAsync(U($"/v1/patients/{h.Patient}/timeline?kinds=Medicine")))
        {
            var entry = Assert.Single((await Json(timeline)).GetProperty("entries").EnumerateArray(), e => e.GetProperty("title").GetString() == medicine.Name);
            Assert.Equal(("Medicine", medicine.Name, encounter), (entry.GetProperty("kind").GetString(), entry.GetProperty("title").GetString(), entry.GetProperty("encounterId").GetGuid()));
        }
        // What the next visit's checks compare against; this visit's own lines are not checked against themselves, nor reconciled as a medicine the patient came in on.
        Assert.Equal([(medicine.Name, (Guid?)encounter), ("Omeprazole 20 mg", encounter)], (await h.Get<IPrescribingChart>(h.Doctor).FactsAsync(h.Patient)).ActiveMedicines.Select(m => (m.Name, m.EncounterId)));
        Assert.Equal(before, (await rx.CheckAsync(encounter))!.Alerts.Select(a => a.Key));
        Assert.Empty((await h.Get<IMedicationReconciliation>(h.Doctor).GetAsync(encounter))!.Rows);

        // A changed draft authorised again: the old version's line is stopped, the new one is active.
        await rx.UpdateAsync(line, Twice with { Dose = "2" });
        using (await AuthoriseAsync(h.Doctor)) { }
        var chart = await ChartAsync();
        Assert.Equal([(MedicationState.Active, "Take 2 tablets by mouth twice a day after food for 5 days."), (MedicationState.Stopped, "Take 1 tablet by mouth twice a day after food for 5 days.")],
            chart.Select(m => (m.State, m.Dose)));

        using (var ok = await CancelAsync(h.Doctor, new { reason = "Wrong medicine (synthetic)" })) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.All(await ChartAsync(), m => Assert.Equal(MedicationState.Stopped, m.State));
        Assert.Empty((await h.Get<IPrescribingChart>(h.Doctor).FactsAsync(h.Patient)).ActiveMedicines);
    }

    /// <summary>MDV-P2-043 AC1: a change and a taper decided in the reconciliation are lines of the prescription, frozen at authorisation and printed in words with the reason.</summary>
    [Fact]
    public async Task AChangeAndATaperFromTheReconciliationPrintOnThePrescription()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            foreach (var (name, dose) in new[] { ("Metformin", "500 mg"), ("Prednisolone", "20 mg") })
                db.MedicationStatements.Add(new MedicationStatement { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Name = name, Dose = dose,
                    State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), RecordedBy = h.Doctor });
            await db.SaveChangesAsync();
        }
        var rec = h.Get<IMedicationReconciliation>(h.Doctor);
        await rec.DecideAsync(encounter, new DecideMedicine("metformin", MedicationDecisionKind.Change, "1000 mg", "HbA1c above target (synthetic)"));
        await rec.DecideAsync(encounter, new DecideMedicine("prednisolone", MedicationDecisionKind.Taper, null, "Flare settled (synthetic)", [new("10 mg", 5), new("5 mg", 5)]));

        var lines = (await h.Get<IPrescriptions>(h.Doctor).ListAsync(encounter))!;
        Assert.Equal([(RxLineKind.Change, "Metformin"), (RxLineKind.Taper, "Prednisolone")], lines.Select(l => (l.Kind, l.Medicine)));
        Assert.All(lines, l => Assert.Equal((RxSource.ChartReconciliation, (Guid?)null), (l.Source, l.Id)));
        Assert.Equal(("Change the dose from 500 mg to 1000 mg.", "غيّر الجرعة من 500 mg إلى 1000 mg."), (lines[0].Sig["en"], lines[0].Sig["ar"]));
        Assert.Equal("Reduce the dose step by step: 10 mg for 5 days, then 5 mg for 5 days. Last dose on 28 Oct 2026.", lines[1].Sig["en"]);

        using (var ok = await AuthoriseAsync(h.Doctor)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var frozen = Assert.Single(await AuthorisedAsync()).Lines;
        Assert.Equal(["Metformin", "Prednisolone"], frozen.Select(l => l.Medicine));
        Assert.All(frozen, l => Assert.False(l.Discontinue));
        using var pdf = await PdfAsync();
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var text = string.Join(' ', PdfPages.Read(await pdf.Content.ReadAsByteArrayAsync(), PlainPdf.A4.Margin).Select(p => p.Text));
        foreach (var expected in new[] { "1. Metformin", "Change the dose from 500 mg to 1000 mg.", "Reason: HbA1c above target (synthetic)", "2. Prednisolone", "Last dose on 28 Oct 2026." })
            Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    /// <summary>MDV-P2-043: the advice is written once, frozen with the authorisation (a changed advice needs a new one), printed, and in English with a
    /// note for a patient whose language has no reviewed translation. A doctor's only, and only in this tenant.</summary>
    [Fact]
    public async Task TheAdviceIsFrozenWithThePrescriptionAndPrintsInEnglishWithANoteForAnotherLanguage()
    {
        await using (var patients = sql.Patients(h.Tenant))
        {
            (await patients.Patients.SingleAsync(p => p.Id == h.Patient)).PreferredLanguage = "ta";
            await patients.SaveChangesAsync();
        }
        await PrescribeAsync("Metformin", Twice);
        var rx = h.Get<IPrescriptions>(h.Doctor);
        Assert.Equal(new RxAdviceView(null, "ta", false), await rx.AdviceAsync(encounter));
        Assert.Equal(new RxAdviceView("Drink plenty of water (synthetic).", "ta", false), await rx.SetAdviceAsync(encounter, new SetRxAdvice("  Drink plenty of water (synthetic).  ")));
        Assert.Equal([("text", "too_long")], (await Assert.ThrowsAsync<PrescriptionValidationException>(() => rx.SetAdviceAsync(encounter, new SetRxAdvice(new string('a', 1001))))).Errors);

        using (var ok = await AuthoriseAsync(h.Doctor)) Assert.Equal((HttpStatusCode.OK, null), (ok.StatusCode, ok.IsSuccessStatusCode ? null : await Code(ok)));
        Assert.Equal("Drink plenty of water (synthetic).", Assert.Single(await AuthorisedAsync()).Advice);
        using (var pdf = await PdfAsync())
        {
            var text = string.Join(' ', PdfPages.Read(await pdf.Content.ReadAsByteArrayAsync(), PlainPdf.A4.Margin).Select(p => p.Text));
            foreach (var expected in new[] { "Advice for the patient", "Drink plenty of water (synthetic).", "Advice is in English: there is no reviewed translation" })
                Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        await rx.SetAdviceAsync(encounter, new SetRxAdvice("Rest for two days (synthetic)."));
        Assert.True((await rx.AuthorisationAsync(encounter))!.Changed); // the paper no longer says what the plan says: authorise again
        using (var changed = await PdfAsync()) Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        foreach (var user in new[] { h.Reception, h.Nurse })
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(user).PutAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/advice"), new { text = "x" })).StatusCode);
        await using var foreign = sql.Prescribing(Guid.NewGuid());
        Assert.Equal(0, await foreign.RxAdvice.IgnoreQueryFilters().CountAsync());
    }

    // SAFETY mutation check: drop page.Header() from PlainPdf.Write and the per-page identifier checks fail.
    [Fact]
    public async Task ThePdfRendersServerSideOnA4AndA5WithTheLicenceAndIsStoredWithItsHash()
    {
        await PrescribeAsync("Metformin", Twice); // Schedule H in India: its warning prints
        foreach (var i in Enumerable.Range(1, 12)) await PrescribeAsync("Cetirizine", Twice with { Instructions = $"Synthetic note {i}" }); // more than one A5 page
        foreach (var alert in (await h.Get<IPrescriptions>(h.Doctor).CheckAsync(encounter))!.Alerts.Where(a => a.Severity == RxSeverity.Severe))
            await h.Get<IPrescriptions>(h.Doctor).OverrideAsync(encounter, new OverrideRxAlert(alert.Key, "Synthetic reason"));
        using (var ok = await AuthoriseAsync(h.Doctor)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var hashes = new List<string>();
        foreach (var paper in new[] { PlainPdf.A4, PlainPdf.A5 })
        {
            using var r = await PdfAsync(paper.Name);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(hash, r.Headers.GetValues("X-Content-SHA256").Single());
            hashes.Add(hash);
            var pages = PdfPages.Read(bytes, paper.Margin);
            string[] markers = ["Licence 84213 · Tamil Nadu Medical Council", "MRN 000001", "Dr. Arjun Rao (synthetic) · MBBS, MD (General Medicine)"];
            Assert.True(paper == PlainPdf.A4 || pages.Count >= 2, $"{paper.Name}: {pages.Count} page(s)");
            Assert.All(pages, page =>
            {
                Assert.Equal(paper.Width, page.Width, 0.1); // Skia writes the page box to a quarter point
                Assert.True(page.InsideMargins, $"{paper.Name}: a line runs past the margin");
                foreach (var marker in markers)
                    Assert.Contains(marker, page.Text, StringComparison.Ordinal);
            });
            var text = string.Join(' ', pages.Select(x => x.Text));
            foreach (var expected in new[] { "1. Metformin 500 mg Tablet", "Take 1 tablet by mouth twice a day after food", "Schedule H drug. Warning", "Allergies: no known allergies" })
                Assert.Contains(expected, text, StringComparison.Ordinal);
            Assert.Contains("https://clinic.test/app/verify/rx/", text.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal); // A5 wraps the long link
            Assert.True(pages[^1].Shapes > 200, "the QR code is drawn"); // a version 5+ code has hundreds of dark modules
        }
        await using (var db = sql.Prescribing(h.Tenant))
        {
            var rows = await db.PrescriptionPdfs.AsNoTracking().ToListAsync();
            Assert.Equal(hashes.Order(), rows.Select(p => p.Sha256).Order());
            foreach (var row in rows) // the copy is in Blob, byte for byte, under an id-only key; not in the database
                Assert.Equal((null, $"{h.Tenant}/rx/{row.Id}", row.Sha256), (row.Content, row.BlobKey, Convert.ToHexStringLower(SHA256.HashData(await StoredAsync(row.BlobKey!)))));
        }
        Assert.Null(await BrokenChainAsync("PrescriptionPdfs"));
        Assert.Equal(["version:1;paper:A4", "version:1;paper:A5"], (await AuditAsync("rx.pdf")).Select(e => e.Detail));
        using (var unknown = await PdfAsync("A3")) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    async Task<byte[]> StoredAsync(string key) =>
        (await new BlobContainerClient(blobs.ConnectionString, BlobDocumentStore.Live).GetBlobClient(key).DownloadContentAsync()).Value.Content.ToArray();

    [Fact]
    public async Task ThePdfCarriesTheClinicLogoOnEveryPageAndTheDoctorsSignatureOnlyWhenUploaded()
    {
        foreach (var i in Enumerable.Range(1, 12)) await PrescribeAsync("Cetirizine", Twice with { Instructions = $"Synthetic note {i}" }); // more than one A5 page
        foreach (var alert in (await h.Get<IPrescriptions>(h.Doctor).CheckAsync(encounter))!.Alerts.Where(a => a.Severity == RxSeverity.Severe))
            await h.Get<IPrescriptions>(h.Doctor).OverrideAsync(encounter, new OverrideRxAlert(alert.Key, "Synthetic reason"));
        using (var ok = await AuthoriseAsync(h.Doctor)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        async Task<IReadOnlyList<PdfPages.Page>> PagesAsync()
        {
            using var r = await PdfAsync("A5");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return PdfPages.Read(await r.Content.ReadAsByteArrayAsync(), PlainPdf.A5.Margin);
        }
        Assert.All(await PagesAsync(), p => Assert.Equal(0, p.Images)); // nothing uploaded: no images, and the paper still prints

        // What the settings screens store (Tenancy re-encodes both to PNG): the keys on the profiles, the bytes in the private container.
        var logos = new BlobLogoStore(blobs.ConnectionString);
        var (logo, signature) = ($"{h.Tenant}/logo/{Guid.NewGuid()}", $"{h.Tenant}/signature/{Guid.NewGuid()}");
        await logos.PutAsync(logo, TestImages.Png, "image/png");
        await logos.PutAsync(signature, TestImages.Make(8, 2, SkiaSharp.SKEncodedImageFormat.Png), "image/png");
        await using (var db = sql.Context(h.Tenant))
        {
            db.Profiles.Add(new PracticeProfile { Id = Guid.NewGuid(), TenantId = h.Tenant, LogoKey = logo, ChangedAt = h.Clock.GetUtcNow() });
            var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
            (await db.DoctorProfiles.SingleAsync(p => p.MembershipId == membership.Id)).SignatureKey = signature;
            await db.SaveChangesAsync();
        }
        var pages = await PagesAsync();
        Assert.True(pages.Count >= 2, $"{pages.Count} page(s)");
        // The logo heads every page; the signature sits once, under the authorisation line.
        Assert.All(pages, p => Assert.Equal(p.Text.Contains("Authorised by", StringComparison.Ordinal) ? 2 : 1, p.Images));
        Assert.Single(pages, p => p.Text.Contains("Authorised by", StringComparison.Ordinal));
        Assert.All(pages, p => Assert.True(p.InsideMargins));
    }

    // SAFETY mutation check: drop the glyph catch in PlainPdf.Write and the emoji gives 500, not 422.
    [Fact]
    public async Task ThePrintViewCarriesEveryPrintLanguageAndThePdfPrintsTamilButRefusesAnEmoji()
    {
        await PrescribeAsync("Metformin", Twice);
        var free = (await h.Get<IPrescriptions>(h.Doctor).AddAsync(encounter, new AddRxLine(null, "சித்த மருந்து (synthetic)", null, null)))!.Single(l => l.FreeText).Id!.Value;
        await h.Get<IPrescriptions>(h.Doctor).UpdateAsync(free, Twice);
        using (await AuthoriseAsync(h.Doctor)) { }
        using var print = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/print"));
        var sig = (await Json(print)).GetProperty("lines")[0].GetProperty("sig");
        Assert.Equal("1 மாத்திரை வாய் வழியாக தினமும் இரண்டு முறை உணவுக்குப் பின் 5 நாட்களுக்கு எடுத்துக்கொள்ளவும். அளவு: 10.", sig.GetProperty("ta").GetString());
        Assert.Equal("1 गोली मुँह से दिन में दो बार खाने के बाद 5 दिन तक लें। मात्रा: 10।", sig.GetProperty("hi").GetString());
        Assert.StartsWith("تناول 1 قرص", sig.GetProperty("ar").GetString(), StringComparison.Ordinal);
        using (var pdf = await PdfAsync())
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Contains("(synthetic)", PdfPages.Read(await pdf.Content.ReadAsByteArrayAsync(), PlainPdf.A4.Margin)[0].Text, StringComparison.Ordinal);
        }

        var emoji = (await h.Get<IPrescriptions>(h.Doctor).AddAsync(encounter, new AddRxLine(null, "Synthetic syrup 🍯", null, null)))!.Single(l => l.Medicine.Contains("🍯", StringComparison.Ordinal)).Id!.Value;
        await h.Get<IPrescriptions>(h.Doctor).UpdateAsync(emoji, Twice);
        using (var again = await AuthoriseAsync(h.Doctor)) Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using (var refused = await PdfAsync())
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "pdf_unsupported_text"), (refused.StatusCode, await Code(refused)));
        await using var db = sql.Prescribing(h.Tenant);
        Assert.Equal(1, await db.PrescriptionPdfs.CountAsync());
    }

    // SAFETY mutation check: put the patient's name or the lines into RxVerifyView and the PHI assertions fail; drop EnterLinkTenant and the look-up finds nothing.
    [Fact]
    public async Task TheQrVerifyCheckIsPublicShowsValidityOnlyAndIsAuditedWithThePatient()
    {
        await PrescribeAsync("Metformin", Twice);
        using (await AuthoriseAsync(h.Doctor)) { }
        var token = await TokenAsync();

        using var r = await VerifyAsync(token);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        var view = JsonDocument.Parse(body).RootElement;
        Assert.Equal(("Valid", "Dr. Arjun Rao (synthetic)", 1), (view.GetProperty("status").GetString(), view.GetProperty("doctor").GetString(), view.GetProperty("lines").GetInt32()));
        foreach (var phi in new[] { "Meera", "000001", "1972", "Metformin", h.Patient.ToString() })
            Assert.DoesNotContain(phi, body, StringComparison.OrdinalIgnoreCase);

        var row = Assert.Single(await AuditAsync("rx.verify"));
        Assert.Equal(((Guid?)null, (Guid?)h.Patient, "status:Valid;version:1"), (row.ActorUserId, row.PatientId, row.Detail));

        var parsed = RxVerifyToken.Read(token)!.Value;
        foreach (var wrong in new[] { "not-a-token", RxVerifyToken.Token(h.Tenant, RxVerifyToken.NewCode()), RxVerifyToken.Token(Guid.NewGuid(), parsed.Code) })
        {
            using var miss = await VerifyAsync(wrong);
            Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode); // another tenant's id with a real code finds nothing (row-level security)
        }
    }

    [Fact]
    public async Task EachDoctorApprovesTheirOwnLayoutOnce()
    {
        using (var before = await Client(h.Doctor).GetAsync(U("/v1/prescription-layout")))
            Assert.Equal(JsonValueKind.Null, (await Json(before)).GetProperty("approvedAt").ValueKind);
        using (var approved = await Client(h.Doctor).PostAsync(U("/v1/prescription-layout/approval"), null)) Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        using (var twice = await Client(h.Doctor).PostAsync(U("/v1/prescription-layout/approval"), null))
            Assert.Equal(h.Clock.GetUtcNow().AddMinutes(-5), (await Json(twice)).GetProperty("approvedAt").GetDateTimeOffset()); // the first approval stands
        using (var colleague = await Client(h.SecondDoctor).GetAsync(U("/v1/prescription-layout")))
            Assert.Equal(JsonValueKind.Null, (await Json(colleague)).GetProperty("approvedAt").ValueKind); // per doctor
        using (var nurse = await Client(h.Nurse).PostAsync(U("/v1/prescription-layout/approval"), null)) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);

        var row = Assert.Single(await AuditAsync("rx.layout.approve"));
        Assert.Equal(((Guid?)h.Doctor, $"layout:{RxPdfDocument.LayoutVersion}", (Guid?)null), (row.ActorUserId, row.Detail, row.PatientId));
        Assert.Null(await BrokenChainAsync("RxLayoutApprovals"));
    }
}
