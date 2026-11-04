using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRegistryTests;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-019 against real SQL Server: separate versioned receipts written with the registration, an append-only ledger, and the reader contract. Synthetic data only.</summary>
public class ConsentTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    readonly Guid tenant = Guid.NewGuid(), actor = Guid.NewGuid(), facility = Guid.NewGuid();
    static int born;

    sealed class User(Guid id) : ICurrentUser { public Guid? UserId => id; }

    static ConsentSet Set(params ConsentDecisionInput[] d) => new(ConsentPolicy.CurrentVersion, d);
    static ConsentDecisionInput Treatment => new(ConsentType.Treatment, null, true);
    static ConsentDecisionInput Reminders(ConsentChannel c, bool granted) => new(ConsentType.Reminders, c, granted);

    PatientRegistry Registry(Guid? t = null, Guid? at = null) =>
        new(sql.Patients(t ?? tenant), new SqlServerFixture.FixedTenant(t ?? tenant), new FixedFacility(at ?? facility), new User(actor),
            new AuditLog(sql.Audit(t ?? tenant)), TimeProvider.System, new FacilityZone("Asia/Kolkata"));

    PatientConsents Consents(Guid? t = null, Guid? at = null) =>
        new(sql.Patients(t ?? tenant), new SqlServerFixture.FixedTenant(t ?? tenant), new FixedFacility(at ?? facility), new User(actor),
            new AuditLog(sql.Audit(t ?? tenant)), TimeProvider.System);

    Task<RegistrationResult> Register(ConsentSet? consents, string country = "IN", string? region = null) =>
        Registry().RegisterAsync((country == "IN" ? IndiaBase : UaeBase) with
        {
            FacilityId = facility, Quick = true, DateOfBirth = new DateOnly(1950, 1, 1).AddDays(Interlocked.Increment(ref born)), Consents = consents,
            Address = region is null ? null : new PatientAddress { Line1 = "Villa 1", Region = region },
        });

    [Fact]
    public async Task RegistrationWritesOneReceiptPerDecisionWithVersionJurisdictionAndActor()
    {
        var saved = await Register(Set(Treatment, Reminders(ConsentChannel.WhatsApp, true), Reminders(ConsentChannel.Sms, false),
            new(ConsentType.InformationExchange, null, true), new(ConsentType.Marketing, null, false)), "AE", "DXB");

        await using var db = sql.Patients(tenant);
        var receipts = await db.ConsentReceipts.Where(r => r.PatientId == saved.PatientId).ToListAsync();
        Assert.Equal(5, receipts.Count);
        Assert.All(receipts, r => Assert.Equal((ConsentPolicy.CurrentVersion, "AE-DXB", actor), (r.PolicyVersion, r.Jurisdiction, r.DecidedBy)));
        // Separate decisions: marketing and SMS reminders declined, the rest granted.
        Assert.Equal(["Marketing", "Reminders/Sms"], receipts.Where(r => !r.Granted).Select(r => r.Channel is null ? $"{r.Type}" : $"{r.Type}/{r.Channel}").Order());
    }

    // SAFETY: mutation check: removing ConsentRules.ForRegistration from PatientRegistry.RegisterAsync makes the first two cases save a patient.
    [Fact]
    public async Task RegistrationCannotBeSavedWithoutTreatmentConsentAndSavesNothing()
    {
        await using var db = sql.Patients(tenant);
        var before = await db.Patients.CountAsync();
        foreach (var bad in new ConsentSet?[]
        {
            null, Set(Reminders(ConsentChannel.Sms, true)), Set(new ConsentDecisionInput(ConsentType.Treatment, null, false)),
            new("2020-old", [Treatment]),
        })
            await Assert.ThrowsAsync<PatientValidationException>(() => Register(bad));
        Assert.Equal(before, await db.Patients.CountAsync());
        Assert.Equal(0, await db.ConsentReceipts.CountAsync());
    }

    [Theory]
    [InlineData(ConsentType.Reminders, null)]            // reminders are per channel
    [InlineData(ConsentType.Portal, ConsentChannel.Sms)] // no other type has a channel
    public void ADecisionWithTheWrongChannelShapeIsRefused(ConsentType type, ConsentChannel? channel) =>
        Assert.Contains(new ValidationError("consents", "invalid"), Assert.Throws<PatientValidationException>(() =>
            ConsentRules.ForRegistration(Set(Treatment, new(type, channel, true)))).Errors);

    [Fact]
    public void ADecisionIsMadeOncePerTypeAndChannelAndTreatmentCannotBeWithdrawnLater()
    {
        Assert.Contains(new ValidationError("consents", "duplicate"), Assert.Throws<PatientValidationException>(() =>
            ConsentRules.ForRegistration(Set(Treatment, Treatment))).Errors);
        Assert.Contains(new ValidationError("consents.treatment", "required"), Assert.Throws<PatientValidationException>(() =>
            ConsentRules.ForChange(Set(new ConsentDecisionInput(ConsentType.Treatment, null, false)))).Errors);
        Assert.Equal("AE-AUH", ConsentRules.Jurisdiction("AE", "AUH"));
        Assert.Equal("IN", ConsentRules.Jurisdiction("IN", "Kerala"));
    }

    [Fact]
    public async Task ChangingADecisionAddsAReceiptKeepsHistoryAndTheReaderFollowsTheNewest()
    {
        var saved = await Register(Set(Treatment, Reminders(ConsentChannel.WhatsApp, true)));
        Assert.True(await Consents().IsGrantedAsync(saved.PatientId, ConsentType.Reminders, ConsentChannel.WhatsApp));
        Assert.False(await Consents().IsGrantedAsync(saved.PatientId, ConsentType.Reminders, ConsentChannel.Sms)); // never decided: no consent
        Assert.False(await Consents().IsGrantedAsync(saved.PatientId, ConsentType.Marketing));

        Assert.NotNull(await Consents().ChangeAsync(saved.PatientId, Set(Reminders(ConsentChannel.WhatsApp, false))));

        Assert.False(await Consents().IsGrantedAsync(saved.PatientId, ConsentType.Reminders, ConsentChannel.WhatsApp)); // withdrawal applies to the next send
        await using var db = sql.Patients(tenant);
        Assert.Equal([true, false], await db.ConsentReceipts.Where(r => r.PatientId == saved.PatientId && r.Type == ConsentType.Reminders)
            .OrderBy(r => r.DecidedAt).Select(r => r.Granted).ToListAsync());
        await using var audit = sql.Audit(tenant);
        Assert.Equal(1, await audit.Events.CountAsync(e => e.PatientId == saved.PatientId && e.Action == "patient.consent.record"));
    }

    [Fact]
    public async Task ReceiptsCannotBeUpdatedOrDeletedAndOtherTenantsAndFacilitiesSeeNothing()
    {
        var saved = await Register(Set(Treatment, Reminders(ConsentChannel.Sms, true)));
        await using var db = sql.Patients(tenant);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"UPDATE patients.ConsentReceipts SET Granted = 0 WHERE PatientId = {saved.PatientId}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM patients.ConsentReceipts WHERE PatientId = {saved.PatientId}"));

        Assert.False(await Consents(Guid.NewGuid()).IsGrantedAsync(saved.PatientId, ConsentType.Treatment)); // another tenant
        Assert.Null(await Consents(at: Guid.NewGuid()).ChangeAsync(saved.PatientId, Set(Reminders(ConsentChannel.Sms, false)))); // another facility
        Assert.True(await Consents().IsGrantedAsync(saved.PatientId, ConsentType.Reminders, ConsentChannel.Sms));
    }
}
