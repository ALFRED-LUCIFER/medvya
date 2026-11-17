using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence.Migrations;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-002 on real PostgreSQL: the services catalogue, price versions and their resolution, billing settings, doctor fees kept only in the catalogue
/// (and copied once from Tenancy), tenant isolation in both layers, and the endpoints' capability and billing-flag gates.
/// </summary>
public sealed class BillingCatalogueTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly List<WebApplicationFactory<Program>> hosts = [];

    public Task InitializeAsync() => h.SeedAsync();

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        foreach (var host in hosts) await host.DisposeAsync();
    }

    ICatalogue Catalogue() => h.Get<ICatalogue>(h.Admin);

    Task<ServiceView> Ecg() => Catalogue().CreateAsync(new SaveService { Name = "ECG (synthetic)", Category = ServiceCategory.Test, Sac = "999316" });

    // ---- Price versions ----

    [Fact]
    public async Task ADoctorsOwnPriceBeatsTheClinicDefaultAndAFutureVersionWaitsForItsDay()
    {
        var ecg = await Ecg();
        var (doctor, other) = (Guid.NewGuid(), Guid.NewGuid());
        var catalogue = Catalogue();
        Assert.Null(await catalogue.ResolvePriceAsync(ecg.Id, doctor, h.Clock.GetUtcNow())); // not priced yet: the line cannot be added

        await catalogue.AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = 50_000 });
        await catalogue.AddPriceAsync(ecg.Id, new AddPrice { DoctorId = doctor, AmountMinor = 70_000 });
        var friday = SchedulingHarness.Monday.AddDays(4);
        await catalogue.AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = 90_000, EffectiveOn = friday });

        var now = h.Clock.GetUtcNow();
        Assert.Equal(70_000, (await catalogue.ResolvePriceAsync(ecg.Id, doctor, now))!.AmountMinor);
        Assert.Equal(50_000, (await catalogue.ResolvePriceAsync(ecg.Id, other, now))!.AmountMinor);
        Assert.Equal(50_000, (await catalogue.ResolvePriceAsync(ecg.Id, null, now))!.AmountMinor);
        // The new default starts at midnight in the facility's time zone (IST), not before.
        var fridayMidnight = new DateTimeOffset(friday.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5));
        Assert.Equal(50_000, (await catalogue.ResolvePriceAsync(ecg.Id, null, fridayMidnight.AddTicks(-1)))!.AmountMinor);
        var later = await catalogue.ResolvePriceAsync(ecg.Id, other, fridayMidnight);
        Assert.Equal((90_000L, "INR", fridayMidnight), (later!.AmountMinor, later.Currency, later.EffectiveFrom));
        Assert.Equal(70_000, (await catalogue.ResolvePriceAsync(ecg.Id, doctor, fridayMidnight))!.AmountMinor); // the doctor's own price still wins
        Assert.Equal(50_000, (await catalogue.ListAsync(false)).Single(s => s.Id == ecg.Id).ClinicPriceMinor); // the list shows the default in effect now
        Assert.Equal(3, (await catalogue.PricesAsync(ecg.Id))!.Count);
    }

    [Fact]
    public async Task APriceCannotStartInThePastOrBeOutOfRange()
    {
        var ecg = await Ecg();
        var refused = await Assert.ThrowsAsync<BillingValidationException>(() =>
            Catalogue().AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = -1, EffectiveOn = SchedulingHarness.Monday.AddDays(-1) }));
        Assert.Equal([("amountMinor", "range"), ("effectiveOn", "past")], refused.Errors);
        Assert.Null(await Catalogue().AddPriceAsync(Guid.NewGuid(), new AddPrice { AmountMinor = 100 }));
    }

    /// <summary>Saving a price inserts a version; the application's database role may not change or remove one (table-rights.sql).</summary>
    [Fact]
    public async Task PriceVersionsAreImmutable()
    {
        var ecg = await Ecg();
        await Catalogue().AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = 50_000 });
        foreach (var statement in new[] { "UPDATE billing.\"PriceVersions\" SET \"AmountMinor\" = 1", "DELETE FROM billing.\"PriceVersions\"" })
        {
            await using var conn = await sql.OpenAs("app_user", h.Tenant);
            await using var cmd = new NpgsqlCommand(statement, conn);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        }
        Assert.Equal(50_000, (await Catalogue().ResolvePriceAsync(ecg.Id, null, h.Clock.GetUtcNow()))!.AmountMinor);
    }

    // ---- Services ----

    [Fact]
    public async Task AServiceIsValidatedAndEditedWithTheVersionItWasReadAt()
    {
        var refused = await Assert.ThrowsAsync<BillingValidationException>(() => Catalogue().CreateAsync(new SaveService
        {
            Name = " ", Sac = "99A316", TaxTreatment = TaxTreatment.Taxable, DurationMinutes = 0,
        }));
        Assert.Equal([("name", "required"), ("category", "required"), ("sac", "format"), ("gstRateBasisPoints", "required"), ("durationMinutes", "range")], refused.Errors);
        var rate = await Assert.ThrowsAsync<BillingValidationException>(() => Catalogue().CreateAsync(new SaveService
        {
            Name = "Dressing (synthetic)", Category = ServiceCategory.Procedure, TaxTreatment = TaxTreatment.Taxable, GstRateBasisPoints = 2801,
        }));
        Assert.Equal([("gstRateBasisPoints", "range")], rate.Errors);

        // An exempt service keeps no rate, whatever was sent.
        var dressing = await Catalogue().CreateAsync(new SaveService { Name = "Dressing (synthetic)", Category = ServiceCategory.Procedure, GstRateBasisPoints = 1800 });
        Assert.Equal((TaxTreatment.Exempt, (int?)null), (dressing.TaxTreatment, dressing.GstRateBasisPoints));
        var edit = new SaveService { Name = "Wound dressing (synthetic)", Category = ServiceCategory.Procedure, TaxTreatment = TaxTreatment.Taxable, GstRateBasisPoints = 1800, Version = dressing.Version };
        var saved = (await Catalogue().UpdateAsync(dressing.Id, edit))!;
        Assert.Equal(("Wound dressing (synthetic)", (int?)1800), (saved.Name, saved.GstRateBasisPoints));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Catalogue().UpdateAsync(dressing.Id, edit with { Code = "DRS" })); // a stale editor: the version it read has moved on
    }

    [Fact]
    public async Task OnlyOneActiveServicePerConsultationKindAndArchivedOnesLeaveTheList()
    {
        await Catalogue().CreateAsync(new SaveService { Name = "Consultation (synthetic)", Category = ServiceCategory.Consultation, ConsultationKind = ConsultationKind.New });
        var taken = await Assert.ThrowsAsync<BillingValidationException>(() =>
            Catalogue().CreateAsync(new SaveService { Name = "Second consultation (synthetic)", Category = ServiceCategory.Consultation, ConsultationKind = ConsultationKind.New }));
        Assert.Equal([("consultationKind", "taken")], taken.Errors);

        var ecg = await Ecg();
        await Catalogue().ArchiveAsync(ecg.Id, true);
        Assert.DoesNotContain(await Catalogue().ListAsync(false), s => s.Id == ecg.Id);
        Assert.NotNull((await Catalogue().ListAsync(true)).Single(s => s.Id == ecg.Id).ArchivedAt);
        Assert.Null((await Catalogue().ArchiveAsync(ecg.Id, false))!.ArchivedAt);
    }

    /// <summary>A consultation service holds every doctor's fee: it is renamed, never archived, and its kind never changes or goes.</summary>
    [Fact]
    public async Task AConsultationServiceCannotBeArchivedOrLoseItsKind()
    {
        var consult = await Catalogue().CreateAsync(new SaveService { Name = "Consultation (synthetic)", Category = ServiceCategory.Consultation, ConsultationKind = ConsultationKind.New });
        await Catalogue().AddPriceAsync(consult.Id, new AddPrice { DoctorId = Guid.NewGuid(), AmountMinor = 60_000 });

        var archive = await Assert.ThrowsAsync<BillingValidationException>(() => Catalogue().ArchiveAsync(consult.Id, true));
        Assert.Equal([("consultationKind", "fixed")], archive.Errors);
        var edit = new SaveService { Name = "New patient visit (synthetic)", Category = ServiceCategory.Consultation, Version = consult.Version };
        foreach (var kind in new ConsultationKind?[] { null, ConsultationKind.FollowUp })
        {
            var changed = await Assert.ThrowsAsync<BillingValidationException>(() => Catalogue().UpdateAsync(consult.Id, edit with { ConsultationKind = kind }));
            Assert.Equal([("consultationKind", "fixed")], changed.Errors);
        }
        var renamed = (await Catalogue().UpdateAsync(consult.Id, edit with { ConsultationKind = ConsultationKind.New }))!;
        Assert.Equal(("New patient visit (synthetic)", (ConsultationKind?)ConsultationKind.New, (DateTimeOffset?)null), (renamed.Name, renamed.ConsultationKind, renamed.ArchivedAt));
    }

    /// <summary>Two doctors added at the same moment in a clinic with no consultation services yet: one creates them, the other waits and uses them.</summary>
    [Fact]
    public async Task TwoDoctorFeesSavedAtOnceCreateTheConsultationServicesOnce()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var fees = new Medvya.Billing.Infrastructure.DoctorFees(sql.Billing(h.Tenant), new PostgresFixture.FixedTenant(h.Tenant), new FixedUser(h.Admin), TimeProvider.System);
        await using var a = sql.Billing(h.Tenant);
        await using var b = sql.Billing(h.Tenant);
        await using var txA = await a.Database.BeginTransactionAsync();
        await using var txB = await b.Database.BeginTransactionAsync();
        await fees.SetAsync(first, "IN", 50_000, 30_000, txA.GetDbTransaction());
        var waiting = fees.SetAsync(second, "IN", 70_000, 40_000, txB.GetDbTransaction()); // blocks on the one-per-kind index until A ends
        await Task.Delay(200);
        Assert.False(waiting.IsCompleted);
        await txA.CommitAsync();
        await waiting;
        await txB.CommitAsync();

        Assert.Equal(2, (await Catalogue().ListAsync(true)).Count(s => s.ConsultationKind is not null));
        var both = await h.Get<IDoctorFees>(h.Admin, s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System))).ForDoctorsAsync([first, second]);
        Assert.Equal((new ConsultationFees(50_000, 30_000), new ConsultationFees(70_000, 40_000)), (both[first], both[second]));
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    // ---- Settings ----

    [Fact]
    public async Task BillingSettingsStartFromDefaultsAndAreValidatedByTheCountryRules()
    {
        var settings = h.Get<IBillingSettings>(h.Admin, s => RazorpayTestKeys.Connect(s, h.Tenant)); // Razorpay links need a connected account
        var defaults = await settings.GetAsync();
        Assert.Equal(("INV", "RCT", "CN", 10, 60), (defaults.InvoicePrefix, defaults.ReceiptPrefix, defaults.CreditNotePrefix, defaults.MaxDiscountPercent, defaults.PaymentLinkExpiryMinutes));
        Assert.Equal([PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.UpiDirect], defaults.PaymentMethods);

        var refused = await Assert.ThrowsAsync<BillingValidationException>(() => settings.SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INVOICE", ReceiptPrefix = "", CreditNotePrefix = "cn", PaymentMethods = [], MaxDiscountPercent = 101, PaymentLinkExpiryMinutes = 10,
        }));
        Assert.Equal([("version", "required"), ("invoicePrefix", "format"), ("receiptPrefix", "required"), ("creditNotePrefix", "format"), ("paymentMethods", "required"),
            ("maxDiscountPercent", "range"), ("paymentLinkExpiryMinutes", "range")], refused.Errors);

        await settings.SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INB", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash, PaymentMethod.RazorpayLink],
            DiscountReasons = ["Staff family", " staff family ", "Senior citizen"], MaxDiscountPercent = 15, PaymentLinkExpiryMinutes = 30, Version = defaults.Version,
        });
        var saved = await h.Get<IBillingSettings>(h.Admin).GetAsync();
        Assert.Equal(("INB", 15, 30), (saved.InvoicePrefix, saved.MaxDiscountPercent, saved.PaymentLinkExpiryMinutes));
        Assert.Equal(["Staff family", "Senior citizen"], saved.DiscountReasons);
        Assert.Equal([PaymentMethod.Cash, PaymentMethod.RazorpayLink], saved.PaymentMethods);
    }

    /// <summary>Two administrators editing at once: the later save, from the version both read, is refused instead of silently undoing the first (#889).</summary>
    [Fact]
    public async Task ASettingsSaveFromAnOlderVersionIsRefused()
    {
        var read = await h.Get<IBillingSettings>(h.Admin).GetAsync();
        Assert.Equal(0u, read.Version); // nothing saved yet
        SaveBillingSettings Save(int max, uint version) => new()
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash], MaxDiscountPercent = max, PaymentLinkExpiryMinutes = 60, Version = version,
        };

        // Two first saves at once: one creates the row, the other (which read no row) is refused, not a 500.
        var firsts = await Task.WhenAll(Enumerable.Range(12, 2).Select(async max =>
        {
            try { return (await h.Get<IBillingSettings>(h.Admin).SaveAsync(Save(max, 0))).Version; }
            catch (DbUpdateConcurrencyException) { return (uint?)null; }
        }));
        var saved = Assert.Single(firsts, v => v is not null)!.Value;

        var current = await h.Get<IBillingSettings>(h.Admin).SaveAsync(Save(20, saved));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Get<IBillingSettings>(h.Admin).SaveAsync(Save(30, saved))); // stale: read before the save above
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Get<IBillingSettings>(h.Admin).SaveAsync(Save(30, 0))); // a "first" save when a row exists
        Assert.Equal((20, current.Version), ((await h.Get<IBillingSettings>(h.Admin).GetAsync()).MaxDiscountPercent, (await h.Get<IBillingSettings>(h.Admin).GetAsync()).Version));
    }

    // ---- Tenant isolation ----

    /// <summary>Mutation check: drop the PriceVersions policy (migrationBuilder.Tenant) and the raw-SQL and IgnoreQueryFilters reads see tenant A's rows.</summary>
    [Fact]
    public async Task AnotherTenantSeesNoneOfTheCatalogueAndCannotWriteIntoIt()
    {
        var ecg = await Ecg();
        await Catalogue().AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = 50_000 });
        await h.Get<IBillingSettings>(h.Admin).SaveAsync(new SaveBillingSettings
        {
            InvoicePrefix = "INV", ReceiptPrefix = "RCT", CreditNotePrefix = "CN", PaymentMethods = [PaymentMethod.Cash], MaxDiscountPercent = 10, PaymentLinkExpiryMinutes = 60, Version = 0,
        });

        var other = Guid.NewGuid();
        await using var inOther = sql.Billing(other);
        Assert.Empty(await inOther.Services.ToListAsync()); // layer 1: the EF filter
        Assert.Empty(await inOther.Services.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        Assert.Empty(await inOther.PriceVersions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await inOther.Settings.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await inOther.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM billing."PriceVersions" """).ToListAsync());
        Assert.Empty(await inOther.Database.SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM billing."Services" """).ToListAsync());

        // Nor plant a price on tenant A's service in A's name.
        inOther.PriceVersions.Add(new PriceVersion { Id = Guid.NewGuid(), TenantId = h.Tenant, ServiceId = ecg.Id, AmountMinor = 1, Currency = "INR", EffectiveFrom = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => inOther.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(insert.InnerException).SqlState);
        // Nor through the catalogue: tenant A's service is not found from another tenant.
        var foreign = h.Get<ICatalogue>(h.Admin, s => s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(other))));
        Assert.Null(await foreign.AddPriceAsync(ecg.Id, new AddPrice { AmountMinor = 1 }));
        Assert.Null(await foreign.PricesAsync(ecg.Id));
        Assert.Null(await foreign.ArchiveAsync(ecg.Id, true));
        Assert.Null(await foreign.ResolvePriceAsync(ecg.Id, null, h.Clock.GetUtcNow()));
        await using var none = sql.Billing(null);
        Assert.Empty(await none.Services.IgnoreQueryFilters().ToListAsync());
    }

    // ---- Doctor fees ----

    /// <summary>
    /// The fees of Tenancy's doctor screens are catalogue prices: Settings, Doctors writes them through IDoctorFees in the membership's transaction, and a
    /// price saved in the catalogue is what Settings, Doctors shows next. All of it with billing switched off.
    /// </summary>
    [Fact]
    public async Task DoctorFeesRoundTripThroughTheCatalogueWithBillingOff()
    {
        using var admin = Client(h.Admin);
        var added = await admin.PostAsJsonAsync("/v1/settings/doctors", DoctorBody());
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var doctor = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal((50_000, 30_000), await FeesOf(admin, doctor));

        var consultation = (await Catalogue().ListAsync(false)).Single(s => s.ConsultationKind == ConsultationKind.New);
        Assert.Equal(("Consultation (new visit)", ServiceCategory.Consultation, TaxTreatment.Exempt), (consultation.Name, consultation.Category, consultation.TaxTreatment));
        await h.Get<ICatalogue>(h.Admin, s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)))
            .AddPriceAsync(consultation.Id, new AddPrice { DoctorId = doctor, AmountMinor = 65_000 });
        Assert.Equal((65_000, 30_000), await FeesOf(admin, doctor));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri("/v1/settings/services", UriKind.Relative))).StatusCode); // billing itself is still off
    }

    /// <summary>One transaction for both modules: when the fee cannot be saved, the doctor is not saved either (and no invitation is sent).</summary>
    [Fact]
    public async Task ADoctorIsNotSavedWhenTheirFeeCannotBe()
    {
        using var admin = Client(h.Admin, s => s.Replace(ServiceDescriptor.Scoped<IDoctorFees, FailingFees>()));
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync("/v1/settings/doctors", DoctorBody())).StatusCode);
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.DoctorProfiles.Where(p => p.FullName == "Dr. Sana Khan (synthetic)").ToListAsync());
        Assert.Empty(await db.Invitations.Where(i => i.Email == "sana.khan@example.test").ToListAsync());
    }

    sealed class FailingFees : IDoctorFees
    {
        public Task<IReadOnlyDictionary<Guid, ConsultationFees>> ForDoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsultationFees> DefaultsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAsync(Guid? doctorId, string countryCode, long newVisitMinor, long followUpMinor, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Billing is down (synthetic).");
        public Task CopyAsync(Guid fromDoctorId, Guid toDoctorId, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>The fees Tenancy kept before MDV-P4-002 are copied once into the catalogue at deploy, and running the copy again changes nothing.</summary>
    [Fact]
    public async Task ExistingFeesAreCopiedIntoTheCatalogueOnce()
    {
        var tenant = Guid.NewGuid();
        Guid doctor = Guid.NewGuid(), facility = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 9, 1, 4, 30, 0, TimeSpan.Zero);
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Old Fees Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia", CreatedAt = created });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Memberships.Add(new Membership { Id = doctor, TenantId = tenant, UserId = Guid.NewGuid(), FacilityId = facility, Role = Role.SoloDoctor, Status = MembershipStatus.Active, IsOwner = true });
            db.PracticeDefaultsSet.Add(new PracticeDefaults { TenantId = tenant, SlotMinutes = 15, NewVisitFeeMinor = 40_000, FollowUpFeeMinor = 20_000, Currency = "INR", WeekdayHours = "[]", SaturdayHours = "[]", NoteTemplates = "[]" });
            db.DoctorProfiles.Add(new DoctorProfile { MembershipId = doctor, TenantId = tenant, FullName = "Dr. Old Fees (synthetic)", NewVisitFeeMinor = 55_000, FollowUpFeeMinor = 35_000, ChangedAt = created.AddDays(3) });
            await db.SaveChangesAsync();
        }

        for (var run = 0; run < 2; run++)
        {
            await using var conn = await sql.OpenAs("migrator_user");
            await using var cmd = new NpgsqlCommand("SET ROLE medvya_owner; " + ServicesCatalogue.CopyFeesFromTenancy, conn); // as db-migrate runs migrations
            await cmd.ExecuteNonQueryAsync();
        }

        await using var billing = sql.Billing(tenant);
        Assert.Equal(2, await billing.Services.CountAsync());
        Assert.Equal(4, await billing.PriceVersions.CountAsync()); // once, though the copy ran twice
        var fees = h.Get<IDoctorFees>(h.Admin, s => s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(tenant))));
        Assert.Equal(new ConsultationFees(40_000, 20_000), await fees.DefaultsAsync());
        Assert.Equal(new ConsultationFees(55_000, 35_000), (await fees.ForDoctorsAsync([doctor]))[doctor]);
        Assert.Equal(created.AddDays(3), (await billing.PriceVersions.FirstAsync(v => v.DoctorId == doctor)).EffectiveFrom);
    }

    // ---- Endpoints ----

    /// <summary>Mutation check: take ManageStaff off the settings group and the receptionist's change is no longer 403.</summary>
    [Fact]
    public async Task OnlyPracticeConfigurationChangesTheCatalogueAndTheFrontDeskReadsIt()
    {
        await BillingOnAsync();
        using var admin = Client(h.Admin);
        var created = await admin.PostAsJsonAsync("/v1/settings/services", new { name = "ECG (synthetic)", category = "Test", sac = "999316" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/v1/settings/services/{id}/prices", new { amountMinor = 50_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/v1/settings/billing", new { invoicePrefix = "INVOICE" })).StatusCode);

        using var desk = Client(h.Reception);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync("/v1/settings/services", new { name = "Free (synthetic)", category = "Other" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync($"/v1/settings/services/{id}/prices", new { amountMinor = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsync(new Uri($"/v1/settings/services/{id}/archive", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PutAsJsonAsync("/v1/settings/billing", new { invoicePrefix = "INV" })).StatusCode);
        var list = await desk.GetFromJsonAsync<JsonElement>(new Uri("/v1/billing/services", UriKind.Relative));
        Assert.Equal(50_000, list.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id).GetProperty("clinicPriceMinor").GetInt64());
        using var nurse = Client(h.ByRole[Role.Nurse]);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync(new Uri("/v1/billing/services", UriKind.Relative))).StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains(await audit.Events.Where(e => e.Action == "billing.service.create").Select(e => e.Outcome).ToListAsync(), o => o == Medvya.Audit.Domain.AuditOutcome.Allowed);
        Assert.Contains(await audit.Events.Where(e => e.Action == "billing.price.add").Select(e => e.Outcome).ToListAsync(), o => o == Medvya.Audit.Domain.AuditOutcome.Denied);
    }

    [Fact]
    public async Task WithBillingOffEveryBillingRouteIs404()
    {
        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Billing, false, "practice", "Billing (synthetic test)");
        using var admin = Client(h.Admin);
        foreach (var path in new[] { "/v1/settings/services", "/v1/settings/billing", "/v1/billing/services" })
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/v1/settings/services", new { name = "ECG (synthetic)", category = "Test" })).StatusCode);
        Assert.Empty(await Catalogue().ListAsync(true));

        // The clinic switch alone is not enough while Medvya's release (Internal) does not reach the organisation.
        await BillingOnAsync();
        using var outside = Client(h.Admin, internalOrganisation: false);
        Assert.Equal(HttpStatusCode.NotFound, (await outside.GetAsync(new Uri("/v1/settings/services", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri("/v1/settings/services", UriKind.Relative))).StatusCode);
    }

    /// <summary>
    /// #1430: the owner switches billing on and off in Settings; every billing route follows, the desk and an administrator of one branch cannot switch
    /// it, each change is audited by the flag service, and the switch itself is 404 while Medvya's release does not reach the practice.
    /// SAFETY: mutation check: RequireRelease(Features.Billing) removed from PUT /billing-switch -> OnlyTheOwnerSwitchesBillingAndEveryRouteFollows failed
    /// </summary>
    [Fact]
    public async Task OnlyTheOwnerSwitchesBillingAndEveryRouteFollows()
    {
        var (services, switchPath) = (new Uri("/v1/billing/services", UriKind.Relative), new Uri("/v1/settings/billing-switch", UriKind.Relative));
        using var admin = Client(h.Admin);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>(switchPath)).GetProperty("enabled").GetBoolean()); // off until the owner says so
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(services)).StatusCode);

        var branchAdmin = Guid.NewGuid(); // administers Adyar, not Velachery, and is no owner
        await using (var db = sql.Context(h.Tenant))
        {
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = branchAdmin, FacilityId = h.Facility, Role = Role.PracticeAdmin, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        using var desk = Client(h.Reception);
        using var branch = Client(branchAdmin);
        foreach (var refused in new[] { desk, branch })
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.PutAsJsonAsync(switchPath, new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(services)).StatusCode); // neither refusal switched it

        var on = await admin.PutAsJsonAsync(switchPath, new { enabled = true });
        Assert.True((await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean());
        Assert.True((await admin.GetFromJsonAsync<JsonElement>(switchPath)).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync(services)).StatusCode);

        // The clinic's switch exists only while Medvya's release (Internal) reaches the organisation.
        using var outside = Client(h.Admin, internalOrganisation: false);
        Assert.Equal(HttpStatusCode.NotFound, (await outside.GetAsync(switchPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outside.PutAsJsonAsync(switchPath, new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync(services)).StatusCode); // the refused change changed nothing

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(switchPath, new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync(services)).StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = (await audit.Events.Where(e => e.Action.StartsWith("flag.")).ToListAsync()).OrderBy(e => e.Id).Select(e => (e.Action, e.ActorUserId)).ToList();
        Assert.Equal([("flag.enable", (Guid?)h.Admin), ("flag.disable", (Guid?)h.Admin)], rows);
    }

    Task BillingOnAsync() => h.Get<IFeatureFlags>(h.Admin).SetAsync(Features.Billing, true, "practice", "Billing (synthetic test)");

    static async Task<(long, long)> FeesOf(HttpClient client, Guid doctor)
    {
        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/settings/doctors", UriKind.Relative));
        var row = list.GetProperty("doctors").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == doctor);
        return (row.GetProperty("newVisitFee").GetInt64(), row.GetProperty("followUpFee").GetInt64());
    }

    object DoctorBody() => new
    {
        fullName = "Dr. Sana Khan (synthetic)", email = "sana.khan@example.test", specialty = "Paediatrics", facilityId = h.Facility,
        authority = "TNMC", licenceNumber = "84213", category = "Permanent", expiresOn = "2031-12-31", attested = true, newVisitFee = 50_000, followUpFee = 30_000,
    };

    sealed class NoMail : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The user in X-Test-User, in the test tenant and facility (as the facility picker sets them).</summary>
    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid user, Action<IServiceCollection>? replace = null, bool internalOrganisation = true)
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Medvya", sql.ConnectionString).UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
                .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility)))
                .ConfigureTestServices(s =>
                {
                    s.Replace(ServiceDescriptor.Singleton<IEmailSender>(new NoMail()));
                    replace?.Invoke(s);
                });
            // The Internal release stage reaches Medvya's own test organisations only (MDV-P5-041).
            if (internalOrganisation) b.UseSetting("Features:InternalOrganisations:0", h.Tenant.ToString());
        });
        hosts.Add(host);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }
}
