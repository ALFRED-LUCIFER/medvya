using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-013 / MDV-P1-014 full registration (P-02) over HTTP against real SQL Server: every step's data saved with the audit
/// event in one transaction, the country from the tenant (never the request), and only registration data in the answers.
/// Synthetic data only (IDs fail their checksums).
/// </summary>
public sealed class PatientRegistrationFlowTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    sealed record Clinic(Guid Tenant, Guid Facility, Guid Receptionist, WebApplicationFactory<Program> Host) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    async Task<Clinic> ClinicIn(string country)
    {
        var (tenant, facility, receptionist) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Registration Clinic (synthetic)", CountryCode = country, ResidencyRegion = country == "IN" ? "centralindia" : "uaenorth" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Front desk (synthetic)", TimeZone = country == "IN" ? "Asia/Kolkata" : "Asia/Dubai" });
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = receptionist, FacilityId = facility, Role = Role.Receptionist, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, tenant)));
        return new(tenant, facility, receptionist, host);
    }

    static HttpClient Desk(Clinic clinic)
    {
        var client = clinic.Host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", clinic.Receptionist.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", clinic.Facility.ToString());
        return client;
    }

    static readonly Uri Patients = new("/v1/patients", UriKind.Relative);

    /// <summary>The opened record holds registration data only: never clinical fields (P-02: receptionists cannot see clinical data).</summary>
    static readonly string[] RecordFields =
    [
        "id", "mrn", "fullName", "arabicName", "dateOfBirth", "dateOfBirthEstimated", "ageYears", "sexAtBirth", "genderIdentity", "preferredLanguage",
        "nationality", "mobile", "alternatePhone", "email", "address", "status", "registeredOn", "identifiers", "payer",
    ];

    [Fact]
    public async Task AFullIndiaRegistrationSavesEveryStepWithItsAuditEventAndShowsRegistrationDataOnly()
    {
        using var clinic = await ClinicIn("IN");
        using var desk = Desk(clinic);
        var saved = await desk.PostAsJsonAsync(Patients, new
        {
            quick = false, fullName = "Lakshmi Pillai (synthetic)", dateOfBirth = "1984-05-09", sexAtBirth = "Female", genderIdentity = "SameAsSexAtBirth",
            preferredLanguage = "ml", mobile = "+91 90000 00411", alternatePhone = "+91 90000 00412", email = "lakshmi.synthetic@example.in",
            identifiers = new object[]
            {
                new { scheme = "Abha", value = "91-0000-0000-0411" }, new { scheme = "AbhaAddress", value = "lakshmi.synthetic@sbx" },
                new { scheme = "AadhaarLast4", value = "0411" },
            },
            address = new { line1 = "12/402 Synthetic Apartments", line2 = "Marine Drive", city = "Kochi", district = "Ernakulam", region = "Kerala", postalCode = "682001" },
            payer = new { kind = "PrivateInsurance", payerName = "Synthetic Health Insurance", network = "Synthetic TPA", memberNumber = "SYN-23-000411-01", validTo = "2027-03-31" },
            countryCode = "AE", // ignored: the country is the tenant's
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Complete", result.GetProperty("status").GetString());
        Assert.Equal(clinic.Receptionist, result.GetProperty("registeredBy").GetGuid());
        var id = result.GetProperty("patientId").GetGuid();

        await using (var db = sql.Patients(clinic.Tenant))
        {
            var patient = await db.Patients.SingleAsync(p => p.Id == id);
            Assert.Equal(("IN", clinic.Facility, "Kerala", "682001"), (patient.CountryCode, patient.FacilityId, patient.Address!.Region, patient.Address.PostalCode));
            Assert.Equal(3, await db.Identifiers.CountAsync(i => i.PatientId == id));
            Assert.Equal("Synthetic TPA", (await db.PayerCoverages.SingleAsync(p => p.PatientId == id)).Network);
        }
        await using (var audit = sql.Audit(clinic.Tenant))
        {
            var registered = await audit.Events.SingleAsync(e => e.Action == "patient.register");
            Assert.Equal((id, Medvya.Audit.Domain.AuditOutcome.Allowed, clinic.Receptionist), (registered.PatientId!.Value, registered.Outcome, registered.ActorUserId!.Value));
            Assert.DoesNotContain("Lakshmi", JsonSerializer.Serialize(registered), StringComparison.OrdinalIgnoreCase); // PatientRegistered.v1: no PHI
        }

        var record = await desk.GetStringAsync(new Uri($"/v1/patients/{id}", UriKind.Relative));
        using var doc = JsonDocument.Parse(record);
        Assert.Equal(RecordFields.Order(), doc.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["Abha:0411", "AbhaAddress:@sbx", "AadhaarLast4:0411"],
            doc.RootElement.GetProperty("identifiers").EnumerateArray().Select(i => $"{i.GetProperty("scheme").GetString()}:{i.GetProperty("lastFour").GetString()}"));
        foreach (var full in new[] { "91000000000411", "SYN-23-000411-01" }) Assert.DoesNotContain(full, record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFullUaeRegistrationKeepsTheArabicNameEmirateAndInsuranceAndWarnsAboutAnExpiredEmiratesId()
    {
        using var clinic = await ClinicIn("AE");
        using var desk = Desk(clinic);
        var saved = await desk.PostAsJsonAsync(Patients, new
        {
            quick = false, fullName = "Fatima Al Mansoori (synthetic)", arabicName = "فاطمة المنصوري", dateOfBirth = "1988-02-03", sexAtBirth = "Female",
            nationality = "AE", preferredLanguage = "ar", mobile = "050 000 0458",
            identifiers = new object[] { new { scheme = "EmiratesId", value = "784-1988-1234567-1", expiresOn = "2020-11-30" } },
            address = new { line1 = "Villa 14", line2 = "Al Karamah", region = "AUH", makani = "1234567890", poBox = "4521" },
            payer = new { kind = "PrivateInsurance", payerName = "Synthetic Insurer", plan = "Enhanced", memberNumber = "1-2345-000458-2", validTo = "2026-12-31", network = "Synthetic network" },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["emirates_id.expired"], result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())); // warns, never blocks
        Assert.Equal("Complete", result.GetProperty("status").GetString());

        var record = await desk.GetFromJsonAsync<JsonElement>(new Uri($"/v1/patients/{result.GetProperty("patientId").GetGuid()}", UriKind.Relative));
        Assert.Equal("فاطمة المنصوري", record.GetProperty("arabicName").GetString());
        Assert.Equal(("AUH", "1234567890"), (record.GetProperty("address").GetProperty("region").GetString(), record.GetProperty("address").GetProperty("makani").GetString()));
        Assert.Equal("58-2", record.GetProperty("payer").GetProperty("memberNumberLastFour").GetString());
        Assert.Equal("+971500000458", record.GetProperty("mobile").GetString());
    }

    [Fact]
    public async Task AUaeRegistrationFollowsUaeRulesEvenWhenTheBodyIsShapedForIndia()
    {
        using var clinic = await ClinicIn("AE");
        using var desk = Desk(clinic);
        var refused = await desk.PostAsJsonAsync(Patients, new
        {
            fullName = "Fatima Al Mansoori (synthetic)", ageYears = 38, sexAtBirth = "Female", mobile = "050 000 0459", countryCode = "IN",
            identifiers = new object[] { new { scheme = "Abha", value = "91-0000-0000-0459" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        foreach (var code in new[] { "\"dateOfBirth\"", "scheme_not_allowed", "emirates_id_or_passport_required" }) Assert.Contains(code, body, StringComparison.Ordinal);
    }
}
