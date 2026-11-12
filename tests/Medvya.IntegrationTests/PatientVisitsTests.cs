using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Domain;
using Medvya.Prescribing.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-1088 on real PostgreSQL over HTTP: a patient's past visits newest first, each with its doctor, diagnoses, prescribed medicines, placed tests
/// (never drafts) and whether the note is signed; the same gate and audit as the chart (reception refused, a colleague without a care relationship refused).
/// </summary>
public sealed class PatientVisitsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateTimeOffset T0 = new(2026, 9, 1, 4, 30, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient);
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

    Uri Visits() => new($"/v1/patients/{h.Patient}/visits", UriKind.Relative);

    Encounter Visit(DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, AppointmentId = Guid.NewGuid(), LeadClinicianId = h.Doctor,
        Status = EncounterStatus.Ended, AuthorityMode = AuthorityMode.Medvya, StartedAt = at, EndedAt = at.AddMinutes(20),
    };

    [Fact]
    public async Task EachVisitCarriesItsDoctorDiagnosisMedicinesAndTestsAndSaysIfSigned()
    {
        var older = Visit(T0);
        var newer = Visit(T0.AddDays(30));
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Encounters.AddRange(older, newer);
            db.ConsultNotes.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, EncounterId = newer.Id, TemplateKey = "general", TemplateVersion = 1, Status = NoteStatus.Signed, Version = 2, UpdatedBy = h.Doctor, UpdatedAt = T0 });
            db.Problems.Add(new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Description = "Type 2 diabetes", Icd10Code = "E11", Source = ChartSource.ClinicianVerified, RecordedAt = T0.AddDays(30), EncounterId = newer.Id });
            db.Problems.Add(new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Description = "Elsewhere", Source = ChartSource.Imported, RecordedAt = T0 }); // no visit: not on any row
            db.MedicationStatements.Add(new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Metformin", Dose = "500 mg", State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedAt = T0.AddDays(30), EncounterId = newer.Id });
            await db.SaveChangesAsync();
        }
        await using (var rx = sql.Prescribing(h.Tenant))
        {
            ClinicalOrder Order(OrderState state, string name) => new() { Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, EncounterId = newer.Id, Kind = OrderKind.Lab, Name = name, State = state, CreatedBy = h.Doctor, CreatedAt = T0.AddDays(30), OrderedBy = state == OrderState.Ordered ? h.Doctor : null, OrderedAt = state == OrderState.Ordered ? T0.AddDays(30) : null };
            rx.Orders.AddRange(Order(OrderState.Ordered, "HbA1c"), Order(OrderState.Draft, "TSH"), Order(OrderState.NotIncluded, "Lipid profile"));
            await rx.SaveChangesAsync();
        }

        using var r = await Client(h.Doctor).GetAsync(Visits());
        var visits = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("visits").EnumerateArray().ToList();
        Assert.Equal([newer.Id, older.Id], visits.Select(v => v.GetProperty("encounterId").GetGuid()));
        var v0 = visits[0];
        Assert.Equal(("Dr. Arjun Rao (synthetic)", true), (v0.GetProperty("doctor").GetString(), v0.GetProperty("noteSigned").GetBoolean()));
        Assert.Equal(("Type 2 diabetes", "E11"), (v0.GetProperty("diagnoses")[0].GetProperty("description").GetString(), v0.GetProperty("diagnoses")[0].GetProperty("code").GetString()));
        Assert.Equal(("Metformin", "500 mg"), (v0.GetProperty("medicines")[0].GetProperty("name").GetString(), v0.GetProperty("medicines")[0].GetProperty("dose").GetString()));
        Assert.Equal(["HbA1c"], v0.GetProperty("tests").EnumerateArray().Select(t => t.GetString()!)); // placed only: a draft or a switched-off order is not a test of the visit
        var v1 = visits[1];
        Assert.Equal((false, 0, 0, 0), (v1.GetProperty("noteSigned").GetBoolean(), v1.GetProperty("diagnoses").GetArrayLength(), v1.GetProperty("medicines").GetArrayLength(), v1.GetProperty("tests").GetArrayLength()));
    }

    [Fact]
    public async Task SameGateAndAuditAsTheChart()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var ok = await Client(h.Doctor).GetAsync(Visits())) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var desk = await Client(h.Reception).GetAsync(Visits())) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode); // reception has no clinical content
        using (var colleague = await Client(h.SecondDoctor).GetAsync(Visits())) Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
        using (var missing = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{Guid.NewGuid()}/visits", UriKind.Relative))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "patient.visits.read").OrderBy(e => e.Id).Select(e => new { e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal([AuditOutcome.Allowed, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.NotFound], rows.Select(x => x.Outcome));
        Assert.Equal((h.Patient, AuditPurpose.Treatment), (rows[0].PatientId, rows[0].Purpose));
    }
}
