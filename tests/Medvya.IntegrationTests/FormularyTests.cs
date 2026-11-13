using System.Diagnostics;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-014 on real PostgreSQL: the country starter list and its classes, fast search, clinic-added medicines (validated by country rules,
/// audited, private to the clinic), order sets, and the two isolation layers around the shared starter rows.
/// </summary>
public sealed class FormularyTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness india = new(sql);
    readonly SchedulingHarness uae = new(sql) { Country = "AE" };

    public async Task InitializeAsync()
    {
        await india.SeedAsync();
        await uae.SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await india.CloseAsync();
        await uae.CloseAsync();
    }

    static IFormulary Doctor(SchedulingHarness h) => h.Get<IFormulary>(h.Doctor);

    static IFormulary Admin(SchedulingHarness h) => h.Get<IFormulary>(h.Admin);

    static AddMedicine Valid(string generic = "Glimepiride", RegulatoryClass? regClass = RegulatoryClass.ScheduleH) =>
        new(generic, "2 mg", "Tablet", ["Amaryl (synthetic brand)"], null, regClass, "A10BB12", "1 tablet before breakfast");

    static async Task<FormularyValidationException> Refused(Task<FormularyItemView> add) => await Assert.ThrowsAsync<FormularyValidationException>(() => add);

    [Fact]
    public async Task EachCountryReadsItsOwnStarterListWithItsOwnClasses()
    {
        var inRows = await Doctor(india).SearchAsync("tramadol", 25);
        var aeRows = await Doctor(uae).SearchAsync("tramadol", 25);
        var inRow = Assert.Single(inRows);
        var aeRow = Assert.Single(aeRows);
        Assert.Equal((RegulatoryClass.ScheduleH1, FormularySource.Starter), (inRow.RegClass, inRow.Source));
        Assert.Equal((RegulatoryClass.Controlled, FormularySource.Starter), (aeRow.RegClass, aeRow.Source));
        Assert.Equal(["warning_schedule_h1", "pharmacist_keeps_copy"], inRow.PrintRequirements);
        Assert.Equal(["warning_controlled", "pharmacist_keeps_copy"], aeRow.PrintRequirements);
        Assert.Null(aeRow.NationalCode); // the register is not loaded, so the code stays blank rather than guessed
        Assert.Equal(["warning_schedule_h"], (await Doctor(india).SearchAsync("amoxicillin", 25)).Single().PrintRequirements);
        Assert.Empty((await Doctor(india).SearchAsync("paracetamol", 25)).Single().PrintRequirements);
    }

    [Fact]
    public async Task SearchFindsGenericOrBrandAnyCaseAndTreatsWildcardsAsText()
    {
        var added = await Admin(india).AddAsync(Valid());
        Assert.Equal(added.Id, (await Doctor(india).SearchAsync("GLIMEP", 25)).Single().Id);
        Assert.Equal(added.Id, (await Doctor(india).SearchAsync("amaryl", 25)).Single().Id); // by brand
        Assert.Empty(await Doctor(india).SearchAsync("%", 25)); // not "everything"
        Assert.Empty(await Doctor(india).SearchAsync("_", 25));
        Assert.Equal(["Metformin"], (await Doctor(india).SearchAsync("metf", 25)).Select(r => r.Generic));
        Assert.True((await Doctor(india).SearchAsync(null, 25)).Count is > 0 and <= 25); // blank lists the first ones
        Assert.Equal(2, (await Doctor(india).SearchAsync("Atorvastatin", 25)).Count); // strength options come back as separate rows
        Assert.Single(await Doctor(india).SearchAsync("a", 1)); // the limit holds
        // MDV-AUDIT-1093: a short term matches names that start with it only; "az" must not bring gliclazide or omeprazole.
        var short2 = (await Doctor(india).SearchAsync("az", 25)).Select(r => r.Generic).ToList();
        Assert.Contains("Azithromycin", short2);
        Assert.All(short2, g => Assert.StartsWith("az", g, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Gliclazide", (await Doctor(india).SearchAsync("clazi", 25)).Select(r => r.Generic)); // three characters or more also match inside a name
    }

    [Fact]
    public async Task SearchOverAThousandsOfMedicinesAnswersInUnder300Milliseconds()
    {
        await using (var db = sql.Prescribing(india.Tenant))
        {
            db.FormularyItems.AddRange(Enumerable.Range(0, 5000).Select(i => new FormularyItem
            {
                Id = Guid.NewGuid(), TenantId = india.Tenant, Country = "IN", Generic = $"Synthetic generic {i:D4}", Strength = "10 mg", Form = "Tablet",
                Brands = [$"Synthetic brand {i:D4}"], RegClass = RegulatoryClass.None, Source = FormularySource.ClinicAdded,
            }));
            await db.SaveChangesAsync();
        }
        var doctor = Doctor(india);
        await doctor.SearchAsync("warm", 25); // plan and connection warm-up, as in the running host
        var watch = Stopwatch.StartNew();
        var rows = await doctor.SearchAsync("generic 490", 25);
        watch.Stop();
        Assert.Equal(10, rows.Count); // 4900..4909
        Assert.True(watch.ElapsedMilliseconds < 300, $"search took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task AnAddedMedicineIsMarkedClinicAddedAuditedAndPrivateToTheClinic()
    {
        var added = await Admin(india).AddAsync(Valid("  Glimepiride ", RegulatoryClass.ScheduleH1));
        Assert.Equal(("Glimepiride", FormularySource.ClinicAdded, RegulatoryClass.ScheduleH1, null), (added.Generic, added.Source, added.RegClass, added.NationalCode));
        Assert.Equal(["warning_schedule_h1", "pharmacist_keeps_copy"], added.PrintRequirements);

        await using var audit = sql.Audit(india.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == "formulary.add" && e.ResourceId == added.Id);
        Assert.Equal((AuditOutcome.Allowed, AuditPurpose.Operations, (Guid?)india.Admin, (Guid?)null, "class:ScheduleH1"), (row.Outcome, row.Purpose, row.ActorUserId, row.PatientId, row.Detail));

        Assert.Empty(await Doctor(uae).SearchAsync("glimepiride", 25)); // another clinic never sees it
        await using var other = sql.Prescribing(uae.Tenant);
        Assert.Empty(await other.FormularyItems.Where(i => i.Generic == "Glimepiride").ToListAsync());
    }

    [Fact]
    public async Task CountryRulesDecideWhatIsValid()
    {
        Assert.Equal([("regClass", "not_in_country")], (await Refused(Admin(uae).AddAsync(Valid(regClass: RegulatoryClass.ScheduleH)))).Errors);
        Assert.Equal([("regClass", "not_in_country")], (await Refused(Admin(india).AddAsync(Valid(regClass: RegulatoryClass.Controlled)))).Errors);
        Assert.Equal(RegulatoryClass.Controlled, (await Admin(uae).AddAsync(Valid("Diazepam (synthetic entry)", RegulatoryClass.Controlled))).RegClass);
        Assert.Equal(RegulatoryClass.ScheduleX, (await Admin(india).AddAsync(Valid("Diazepam (synthetic entry)", RegulatoryClass.ScheduleX))).RegClass);
        Assert.Equal([("regClass", "required")], (await Refused(Admin(india).AddAsync(Valid(regClass: null)))).Errors); // never defaulted to an ordinary medicine
        Assert.Equal([("nationalCode", "invalid")], (await Refused(Admin(uae).AddAsync(Valid("Codetest", RegulatoryClass.None) with { NationalCode = "ab!" }))).Errors);
        var coded = await Admin(uae).AddAsync(Valid("Codetest") with { NationalCode = "A12-3456-789", RegClass = RegulatoryClass.None });
        Assert.Equal("A12-3456-789", coded.NationalCode);
    }

    [Fact]
    public async Task InvalidInputIsRefusedWithCodesAndNothingIsSaved()
    {
        var errors = (await Refused(Admin(india).AddAsync(new AddMedicine(" ", new string('x', 51), "Lozenge", [.. Enumerable.Repeat("b", 11).Select((b, i) => b + i)], null, RegulatoryClass.None, "nope", new string('s', 201))))).Errors;
        Assert.Equal(["atc", "brands", "defaultSig", "form", "generic", "strength"], errors.Select(e => e.Field).Order());
        Assert.DoesNotContain(await Doctor(india).SearchAsync(null, 50), r => r.Strength.StartsWith("xxx", StringComparison.Ordinal));
        await using var audit = sql.Audit(india.Tenant);
        Assert.False(await audit.Events.AnyAsync(e => e.Action == "formulary.add"));
        await Admin(india).AddAsync(Valid("Blanks") with { Atc = " ", DefaultSig = null, Brands = null }); // blanks stay blank
        var blank = (await Doctor(india).SearchAsync("Blanks", 5)).Single();
        Assert.Equal((null, null, 0), (blank.Atc, blank.DefaultSig, blank.Brands.Count));
    }

    [Fact]
    public async Task AMedicineAlreadyListedIsRefusedIgnoringCaseWhetherStarterOrAdded()
    {
        await Assert.ThrowsAsync<FormularyConflictException>(() => Admin(india).AddAsync(Valid("METFORMIN") with { Strength = "500 MG", RegClass = RegulatoryClass.ScheduleH }));
        await Admin(india).AddAsync(Valid());
        await Assert.ThrowsAsync<FormularyConflictException>(() => Admin(india).AddAsync(Valid("glimepiride")));
        await Admin(india).AddAsync(Valid("Glimepiride") with { Strength = "4 mg" }); // another strength is another row
        await Admin(uae).AddAsync(Valid("Glimepiride", RegulatoryClass.None)); // another clinic's list is its own
    }

    [Fact]
    public async Task OrderSetsAreCreatedListedAndPrivateToTheClinic()
    {
        var set = await Admin(india).CreateOrderSetAsync(new CreateOrderSet(" Diabetes review labs ", [" HbA1c", "Fasting glucose", "hba1c", "Serum creatinine", " "]));
        Assert.Equal("Diabetes review labs", set.Name);
        Assert.Equal(["HbA1c", "Fasting glucose", "Serum creatinine"], set.Items);
        Assert.Equal(set.Id, (await Doctor(india).OrderSetsAsync()).Single().Id); // a doctor uses it in the composer
        Assert.Empty(await Doctor(uae).OrderSetsAsync());

        await Assert.ThrowsAsync<FormularyConflictException>(() => Admin(india).CreateOrderSetAsync(new CreateOrderSet("diabetes REVIEW labs", ["Urine ACR"])));
        Assert.Equal([("items", "required")], (await Assert.ThrowsAsync<FormularyValidationException>(() => Admin(india).CreateOrderSetAsync(new CreateOrderSet("Empty", [" "])))).Errors);
        Assert.Equal([("name", "required")], (await Assert.ThrowsAsync<FormularyValidationException>(() => Admin(india).CreateOrderSetAsync(new CreateOrderSet(null, ["X"])))).Errors);

        await using var audit = sql.Audit(india.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == "orderset.create");
        Assert.Equal((set.Id, (Guid?)india.Admin, "items:3"), (row.ResourceId!.Value, row.ActorUserId, row.Detail));
    }

    [Fact]
    public async Task NoClinicCanChangeOrAddToTheStarterListAndTheDatabaseAgrees()
    {
        // Layer 1: the application can only add rows of its own tenant, never a shared one (no tenant on the row).
        await using (var db = sql.Prescribing(india.Tenant))
        {
            db.FormularyItems.Add(new FormularyItem { Id = Guid.NewGuid(), TenantId = null, Country = "IN", Generic = "Sneaky", Strength = "1 mg", Form = "Tablet", RegClass = RegulatoryClass.None, Source = FormularySource.Starter });
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsType<PostgresException>(refused.InnerException);
        }
        // Layer 2: a clinic's change to a starter row (its class, here) is refused by the database, and the row keeps its class.
        await using (var db = sql.Prescribing(india.Tenant))
            await Assert.ThrowsAsync<PostgresException>(() => db.FormularyItems.Where(i => i.Generic == "Tramadol").ExecuteUpdateAsync(s => s.SetProperty(i => i.RegClass, RegulatoryClass.None)));
        Assert.Equal(RegulatoryClass.ScheduleH1, (await Doctor(india).SearchAsync("tramadol", 5)).Single().RegClass);
        // Row-level security alone (the model's filter off) still hides another clinic's addition and shows only starter rows plus its own.
        await Admin(india).AddAsync(Valid());
        await using var other = sql.Prescribing(uae.Tenant);
        var seen = await other.FormularyItems.IgnoreQueryFilters().Where(i => i.Generic == "Glimepiride").ToListAsync();
        Assert.Empty(seen);
        Assert.Equal(0, await other.FormularyItems.IgnoreQueryFilters().CountAsync(i => i.TenantId == india.Tenant));
    }

    [Fact]
    public async Task WithNoTenantOnlyTheSharedStarterRowsAreReadable()
    {
        await Admin(india).AddAsync(Valid());
        await Admin(india).CreateOrderSetAsync(new CreateOrderSet("Synthetic set", ["HbA1c"]));
        await using var db = sql.Prescribing(null);
        Assert.Equal(0, await db.OrderSets.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.FormularyItems.IgnoreQueryFilters().CountAsync(i => i.TenantId != null));
        Assert.Equal(FormularySeed.Rows.Count, await db.FormularyItems.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void TheCountryRulesHoldTheClassesPrintRulesAndCodeShapes()
    {
        Assert.Equal([RegulatoryClass.None, RegulatoryClass.ScheduleH, RegulatoryClass.ScheduleH1, RegulatoryClass.ScheduleX], FormularyRules.India.Classes().Order());
        Assert.Equal([RegulatoryClass.None, RegulatoryClass.Controlled], FormularyRules.Uae.Classes().Order());
        Assert.Equal(["warning_schedule_x", "pharmacist_keeps_copy", "duplicate_prescription"], FormularyRules.India.PrintRequirements(RegulatoryClass.ScheduleX));
        Assert.Empty(FormularyRules.Uae.PrintRequirements(RegulatoryClass.None));
        Assert.All(Enum.GetValues<RegulatoryClass>().Where(c => c != RegulatoryClass.None), c =>
            Assert.True(FormularyRules.India.Classes().Contains(c) ? FormularyRules.India.PrintRequirements(c).Count > 0 : FormularyRules.Uae.PrintRequirements(c).Count > 0)); // a scheduled class always has print rules
        Assert.True(FormularyRules.Uae.IsNationalCode("A12-3456-789"));
        Assert.False(FormularyRules.Uae.IsNationalCode("ab"));
        Assert.False(FormularyRules.India.IsNationalCode("with space"));
        Assert.True(FormularyRules.IsAtc("A10BA02"));
        Assert.False(FormularyRules.IsAtc("A10BA0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormularyRules.For("US"));
    }

    [Fact]
    public void TheStarterListIsUniqueAndEveryClassBelongsToItsCountry()
    {
        Assert.Equal(FormularySeed.Rows.Count, FormularySeed.Rows.Select(FormularySeed.IdOf).Distinct().Count());
        Assert.All(FormularySeed.Rows, r =>
        {
            Assert.Contains(r.RegClass, FormularyRules.For(r.Country).Classes());
            Assert.Contains(r.Form, FormularyRules.Forms);
            Assert.True(FormularyRules.IsAtc(r.Atc));
        });
    }
}
