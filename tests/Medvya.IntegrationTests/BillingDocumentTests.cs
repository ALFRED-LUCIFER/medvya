using System.Net;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P4-005 on real PostgreSQL and a real Azurite: the bill (bill of supply, tax invoice) and its receipt as A5 and 80 mm thermal PDFs, each issued once,
/// stored with its SHA-256 and re-printed byte for byte; receipts numbered from the receipt series; append-only rows, tenant isolation in both layers, and the
/// endpoint's capability and flag.
/// </summary>
public sealed class BillingDocumentTests(PostgresFixture sql, AzuriteFixture blobs) : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    const string Gstin = "33AAAAA0000A1Z0"; // synthetic: fails the GSTIN check character
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program>? host;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await using var tenancy = sql.Context(h.Tenant);
        tenancy.Profiles.Add(new Medvya.Tenancy.Domain.PracticeProfile
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, Address = "12 Beach Road, Chennai (synthetic)", Phone = "044 0000 0000", TaxNumber = Gstin, RegistrationNumber = "TN/CE/2026/00042",
            ChangedAt = DateTimeOffset.UtcNow,
        });
        await tenancy.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (host is not null) await host.DisposeAsync();
        await h.CloseAsync();
    }

    void WithStore(IServiceCollection s) =>
        s.Replace(ServiceDescriptor.Singleton<IDocumentStore>(new BlobDocumentStore(new BlobServiceClient(blobs.ConnectionString), userDelegation: false, createContainers: true)));
    IBillingDocuments Printer(Guid? tenant = null) => h.Get<IBillingDocuments>(h.Reception, s =>
    {
        WithStore(s);
        if (tenant is { } t) s.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(new PostgresFixture.FixedTenant(t)));
    });
    IInvoices Desk() => h.Get<IInvoices>(h.Reception, s => s.Replace(ServiceDescriptor.Singleton<IPaymentLinks>(new BillingInvoiceTests.FakeLinks())));

    async Task<InvoiceView> IssuedAsync(params (string Name, long Amount, TaxTreatment Tax)[] services)
    {
        var bill = (await Desk().CreateAsync(new NewInvoice { PatientId = h.Patient }))!;
        foreach (var (name, amount, tax) in services)
        {
            var service = await h.Get<ICatalogue>(h.Admin).CreateAsync(new SaveService
            {
                Name = name, Category = ServiceCategory.Procedure, TaxTreatment = tax, GstRateBasisPoints = tax == TaxTreatment.Taxable ? 1800 : null, Sac = "999312",
            });
            await h.Get<ICatalogue>(h.Admin).AddPriceAsync(service.Id, new AddPrice { AmountMinor = amount });
            bill = (await Desk().AddLineAsync(bill.Id, new AddLine { ServiceId = service.Id, Quantity = 1, Version = bill.Version }))!;
        }
        return (await Desk().IssueAsync(bill.Id, new InvoiceVersion { Version = bill.Version }))!;
    }

    static string Text(byte[] pdf, double margin) => string.Join(' ', PdfPages.Read(pdf, margin).Select(p => p.Text));

    async Task<List<BillingDocument>> RowsAsync()
    {
        await using var db = sql.Billing(h.Tenant);
        return await db.BillingDocuments.AsNoTracking().OrderBy(d => d.IssuedAt).ThenBy(d => d.Id).ToListAsync();
    }

    async Task<List<string?>> AuditAsync(Guid bill)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == "billing.document.print" && e.ResourceId == bill && e.Outcome == AuditOutcome.Allowed)
            .OrderBy(e => e.Id).Select(e => e.Detail).ToListAsync();
    }

    static async Task<string> Refused(Task task) => string.Join(",", (await Assert.ThrowsAsync<BillingValidationException>(() => task)).Errors.Select(e => $"{e.Field}:{e.Code}"));

    /// <summary>
    /// An exempt bill prints as a bill of supply with the clinic, its GSTIN and place of supply, the patient, lines, the round-off line, the total and its
    /// words; a second print is the stored paper, byte for byte, audited as a re-print. The thermal copy is a separate 80 mm paper with the same facts.
    /// </summary>
    [Fact]
    public async Task AnExemptBillPrintsAsABillOfSupplyAndARePrintIsTheSamePaper()
    {
        var bill = await IssuedAsync(("Consultation, follow-up (synthetic)", 60_000, TaxTreatment.Exempt), ("ECG, 12-lead (synthetic)", 35_050, TaxTreatment.Exempt));
        var a5 = (await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5))!;
        var pages = PdfPages.Read(a5.Content, Medvya.BuildingBlocks.Infrastructure.PlainPdf.A5.Margin);
        Assert.All(pages, p => Assert.True(p.InsideMargins));
        Assert.Equal(419.53, pages[0].Width, 1); // A5
        var text = string.Join(' ', pages.Select(p => p.Text));
        foreach (var expected in new[]
                 {
                     "Iyer Family Clinic (synthetic)", "12 Beach Road, Chennai (synthetic)", $"GSTIN {Gstin}", "Registration no. TN/CE/2026/00042", "BILL OF SUPPLY", bill.Number!,
                     "Meera Iyer (synthetic)", "MRN 000001", "Tamil Nadu (33)", "Consultation, follow-up (synthetic)", "SAC 999312 · Exempt", "600.00", "350.50",
                     "Subtotal 950.50", "Round off 0.50", "Total (INR) 951.00", "Rupees nine hundred and fifty-one only", "exempt from GST", "For Iyer Family Clinic (synthetic)",
                     "Authorised signatory", "Page 1 of 1",
                 })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", text, StringComparison.OrdinalIgnoreCase); // owner decision: the draft format is not marked on the paper

        var again = (await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5))!;
        Assert.Equal(a5.Content, again.Content);
        Assert.Equal((a5.Sha256, Convert.ToHexStringLower(SHA256.HashData(again.Content))), (again.Sha256, a5.Sha256));

        var thermal = (await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.Thermal))!;
        var roll = Assert.Single(PdfPages.Read(thermal.Content, 4 * 2.8346));
        Assert.Equal(226.77, roll.Width, 1); // 80 mm
        Assert.True(roll.InsideMargins);
        foreach (var expected in new[] { "BILL OF SUPPLY", bill.Number!, "Meera Iyer (synthetic) · MRN 000001", "Tamil Nadu (33)", "Total (INR) 951.00", "Rupees nine hundred and fifty-one only" })
            Assert.Contains(expected, roll.Text, StringComparison.Ordinal);

        var rows = await RowsAsync();
        Assert.Equal([(BillingDocumentKind.Bill, DocumentLayout.A5, bill.Number!, 0L, a5.Sha256), (BillingDocumentKind.Bill, DocumentLayout.Thermal, bill.Number!, 0L, thermal.Sha256)],
            rows.Select(r => (r.Kind, r.Layout, r.Number, r.PaidMinor, r.Sha256)));
        Assert.All(rows, r => Assert.Equal($"{h.Tenant}/billing/{r.Id}", r.BlobKey)); // ids only
        Assert.Equal(["kind:Bill;layout:A5;copy:original", "kind:Bill;layout:A5;copy:reprint", "kind:Bill;layout:Thermal;copy:original"], await AuditAsync(bill.Id));
    }

    /// <summary>Any taxed line makes the paper a tax invoice (only taxed) or an invoice-cum-bill of supply (with exempt care), with the GST of each taxed line.</summary>
    [Fact]
    public async Task ATaxedLineMakesATaxInvoiceWithItsGst()
    {
        var taxed = await IssuedAsync(("Medical certificate (synthetic)", 10_000, TaxTreatment.Taxable));
        var text = Text((await Printer().PdfAsync(taxed.Id, BillingDocumentKind.Bill, DocumentLayout.A5))!.Content, Medvya.BuildingBlocks.Infrastructure.PlainPdf.A5.Margin);
        foreach (var expected in new[] { "TAX INVOICE", "SAC 999312 · GST 18% · 18.00", "GST 18.00", "Total (INR) 118.00", "GST is shown against each taxed service." })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        var mixed = await IssuedAsync(("Consultation (synthetic)", 50_000, TaxTreatment.Exempt), ("Certificate (synthetic)", 10_000, TaxTreatment.Taxable));
        var thermal = Text((await Printer().PdfAsync(mixed.Id, BillingDocumentKind.Bill, DocumentLayout.Thermal))!.Content, 4 * 2.8346);
        Assert.Contains("INVOICE-CUM-BILL OF SUPPLY", thermal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receipt needs a payment and an issued bill. It takes the next receipt number; its thermal copy shares it; a later payment makes a new receipt with
    /// the next number. Each payment prints its method and reference; the paid amount is in words; the balance shows while one is left.
    /// </summary>
    [Fact]
    public async Task AReceiptIsNumberedFromTheReceiptSeriesAndListsEachPayment()
    {
        var draft = (await Desk().CreateAsync(new NewInvoice { PatientId = h.Patient }))!;
        Assert.Equal("state:invalid", await Refused(Printer().PdfAsync(draft.Id, BillingDocumentKind.Bill, DocumentLayout.A5)));
        var bill = await IssuedAsync(("Consultation (synthetic)", 100_000, TaxTreatment.Exempt));
        Assert.Equal("payment:none", await Refused(Printer().PdfAsync(bill.Id, BillingDocumentKind.Receipt, DocumentLayout.A5)));

        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 30_000 });
        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Card, AmountMinor = 20_000, Reference = "TID 4471 RRN 8812" });
        // Paid on 31 March, printed today: the receipt is dated by its issue, the same instant its number's financial year comes from.
        await using (var db = sql.Billing(h.Tenant))
            await db.Database.ExecuteSqlAsync($"""UPDATE billing."Payments" SET "RecordedAt" = '2026-03-31T18:00:00Z', "ConfirmedAt" = '2026-03-31T18:00:00Z' WHERE "InvoiceId" = {bill.Id}""");
        var first = (await Printer().PdfAsync(bill.Id, BillingDocumentKind.Receipt, DocumentLayout.A5))!;
        var text = Text(first.Content, Medvya.BuildingBlocks.Infrastructure.PlainPdf.A5.Margin);
        foreach (var expected in new[]
                 {
                     "PAYMENT RECEIPT", "RCT/26-27/00001", $"AGAINST BILL {bill.Number}", "Bill total 1,000.00", "Balance due 500.00", "Paid (INR) 500.00",
                     "Rupees five hundred only", "Cash · 300.00", "· ref TID 4471 RRN 8812 · 200.00", "(Asia/Kolkata)",
                 })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        var roll = Text((await Printer().PdfAsync(bill.Id, BillingDocumentKind.Receipt, DocumentLayout.Thermal))!.Content, 4 * 2.8346);
        Assert.Contains("RCT/26-27/00001", roll, StringComparison.Ordinal); // the same receipt on the other paper
        // SAFETY: mutation check: dated the receipt by its last payment again -> this test failed (the receipt said 31 Mar 2026 under a 26-27 number)
        Assert.DoesNotContain("Date 31 Mar 2026", roll, StringComparison.Ordinal);

        await Desk().RecordPaymentAsync(bill.Id, new RecordPayment { Method = PaymentMethod.Cash, AmountMinor = 50_000 });
        var second = Text((await Printer().PdfAsync(bill.Id, BillingDocumentKind.Receipt, DocumentLayout.A5))!.Content, Medvya.BuildingBlocks.Infrastructure.PlainPdf.A5.Margin);
        Assert.Contains("RCT/26-27/00002", second, StringComparison.Ordinal);
        // The second receipt is for the ₹500 received since the first, never the ₹500 already receipted again.
        // SAFETY: mutation check: set receivedBefore to 0 -> this test failed (the second receipt said Paid (INR) 1,000.00 and listed the card again)
        foreach (var expected in new[] { "Received earlier 500.00", "Paid (INR) 500.00", "Rupees five hundred only", "Cash · 500.00" })
            Assert.Contains(expected, second, StringComparison.Ordinal);
        Assert.DoesNotContain("TID 4471", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Balance due", second, StringComparison.Ordinal);
        Assert.Equal(["RCT/26-27/00001", "RCT/26-27/00001", "RCT/26-27/00002"], (await RowsAsync()).Select(r => r.Number));
    }

    // SAFETY: mutation check: removed the hash check in BillingDocuments.ReprintAsync -> this test failed (the altered blob was handed out)
    [Fact]
    public async Task ARePrintIsTheStoredPaperByteForByteAndAChangedBlobIsRefused()
    {
        var bill = await IssuedAsync(("Consultation (synthetic)", 50_000, TaxTreatment.Exempt));
        var printed = (await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5))!;
        var row = Assert.Single(await RowsAsync());
        var blob = new BlobContainerClient(blobs.ConnectionString, BlobDocumentStore.Live).GetBlobClient(row.BlobKey);
        Assert.Equal(printed.Content, (await blob.DownloadContentAsync()).Value.Content.ToArray());

        await blob.UploadAsync(new BinaryData([.. printed.Content, (byte)' ']), overwrite: true); // someone with storage access changes the stored copy
        await Assert.ThrowsAsync<InvalidOperationException>(() => Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5));
    }

    /// <summary>The application's database role may not change or delete a stored paper (table-rights.sql).</summary>
    // SAFETY: mutation check: removed the BillingDocuments REVOKE from infra/postgres/table-rights.sql -> this test failed (the UPDATE went through)
    [Fact]
    public async Task AStoredPaperCannotBeChangedOrDeleted()
    {
        var bill = await IssuedAsync(("Consultation (synthetic)", 50_000, TaxTreatment.Exempt));
        await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5);
        foreach (var statement in new[] { "UPDATE billing.\"BillingDocuments\" SET \"Sha256\" = repeat('0', 64)", "DELETE FROM billing.\"BillingDocuments\"" })
        {
            await using var conn = await sql.OpenAs("app_user", h.Tenant);
            await using var cmd = new NpgsqlCommand(statement, conn);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        }
    }

    // SAFETY: mutation check: removed migrationBuilder.Tenant("billing", "BillingDocuments") -> this test failed (the IgnoreQueryFilters read saw tenant A's paper)
    [Fact]
    public async Task AnotherTenantSeesNoneOfThePapersAndCannotPrintTheBill()
    {
        var bill = await IssuedAsync(("Consultation (synthetic)", 50_000, TaxTreatment.Exempt));
        await Printer().PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5);
        var other = Guid.NewGuid();
        await using var inOther = sql.Billing(other);
        Assert.Empty(await inOther.BillingDocuments.ToListAsync()); // layer 1: the EF filter
        Assert.Empty(await inOther.BillingDocuments.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security
        Assert.Null(await Printer(other).PdfAsync(bill.Id, BillingDocumentKind.Bill, DocumentLayout.A5));
        Assert.Single(await RowsAsync());
    }

    HttpClient Client(Guid user)
    {
        host ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Features:InternalOrganisations:0", h.Tenant.ToString()) // billing is at the Internal release stage
            .UseSetting("Storage:ConnectionString", blobs.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, h.Tenant)));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    /// <summary>The desk downloads the PDF (ids only in the path, not cached); roles without "Invoices and payments" are refused; billing off is 404.</summary>
    // SAFETY: mutation check: the endpoint sits in InvoiceEndpoints' group; moved it out of the group onto app -> this test failed (the nurse got the PDF)
    [Fact]
    public async Task TheDeskDownloadsThePdfAndOtherRolesAreRefused()
    {
        await h.StepsAsync(nurse: false, billing: true);
        var bill = await IssuedAsync(("Consultation (synthetic)", 50_000, TaxTreatment.Exempt));
        var path = new Uri($"/v1/billing/invoices/{bill.Id}/documents/bill/a5", UriKind.Relative);
        using (var r = await Client(h.Reception).PostAsync(path, null))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await r.Content.ReadAsByteArrayAsync())), r.Headers.GetValues("X-Content-SHA256").Single());
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        }
        using (var r = await Client(h.Reception).PostAsync(new Uri($"/v1/billing/invoices/{bill.Id}/documents/statement/a5", UriKind.Relative), null))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await Client(h.Reception).PostAsync(new Uri($"/v1/billing/invoices/{bill.Id}/documents/receipt/thermal", UriKind.Relative), null))
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // nothing paid yet
        foreach (var role in new[] { Medvya.Tenancy.Domain.Role.Nurse, Medvya.Tenancy.Domain.Role.HospitalDoctor })
            using (var r = await Client(h.ByRole[role]).PostAsync(path, null))
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        await h.Get<IFeatureFlags>(h.Admin).SetAsync(Medvya.Tenancy.Domain.Features.Billing, false, "practice", "Billing (synthetic test)");
        using (var r = await Client(h.Reception).PostAsync(path, null)) Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
