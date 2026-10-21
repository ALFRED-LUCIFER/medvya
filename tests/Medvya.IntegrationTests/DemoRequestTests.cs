using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Medvya.Leads.Infrastructure;
using Medvya.Leads.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>Demo requests on SQL Server (MDV-P1-003): stored, founder emailed, free text never emailed.</summary>
public class DemoRequestTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    static DemoRequest Request(string? email = "kavya.menon@example.com", string? phone = null, string? message = null, string name = "Kavya Menon") =>
        DemoRequestRulesTests.Request(email, phone, message, name);

    [Fact]
    public async Task ARequestIsStoredAndTheFounderIsEmailedWithoutTheFreeText()
    {
        var pickup = Directory.CreateTempSubdirectory("medvya-mail-");
        await using var services = new ServiceCollection().AddLogging()
            .AddLeadsModule(sql.ConnectionString, new LeadsEmailOptions { To = "founder@medvya.example", PickupDirectory = pickup.FullName })
            .BuildServiceProvider();
        var request = Request(message: "Synthetic note: please call after 4 pm.");
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IDemoRequests>().SubmitAsync(request);

        await using var db = new LeadsDbContext(new DbContextOptionsBuilder<LeadsDbContext>().UseSqlServer(sql.ConnectionString).Options);
        var stored = await db.DemoRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(("Kavya Menon", "Synthetic note: please call after 4 pm."), (stored.Name, stored.Message));

        var mail = File.ReadAllText(Assert.Single(pickup.GetFiles("*.eml")).FullName);
        Assert.Contains("To: founder@medvya.example", mail, StringComparison.Ordinal);
        Assert.Contains("kavya.menon@example.com", mail, StringComparison.Ordinal);
        Assert.DoesNotContain("4 pm", mail, StringComparison.Ordinal); // free text could hold patient details, so it is never emailed
        pickup.Delete(recursive: true);
    }

    [Fact]
    public async Task AFailedEmailDoesNotLoseTheRequest()
    {
        await using var services = new ServiceCollection().AddLogging()
            .AddLeadsModule(sql.ConnectionString, new LeadsEmailOptions { To = "founder@medvya.example", SmtpHost = "localhost", SmtpPort = 1, EnableSsl = false })
            .BuildServiceProvider();
        var request = Request(name: "Arjun Rao");
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IDemoRequests>().SubmitAsync(request);

        await using var db = new LeadsDbContext(new DbContextOptionsBuilder<LeadsDbContext>().UseSqlServer(sql.ConnectionString).Options);
        Assert.True(await db.DemoRequests.AnyAsync(r => r.Id == request.Id));
    }
}

/// <summary>Demo request validation (MDV-P1-003): field/code pairs only, never the entered values.</summary>
public class DemoRequestRulesTests
{
    internal static DemoRequest Request(string? email = "kavya.menon@example.com", string? phone = null, string? message = null, string name = "Kavya Menon") => new()
    {
        Name = name, Organisation = "Synthetic General Hospital", Role = "Chief medical officer", Country = "IN", Email = email, Phone = phone, Message = message,
    };

    [Theory]
    [InlineData("kavya.menon@example.com", null, null)]
    [InlineData(null, "+91 98450 12345", null)]
    [InlineData(null, null, "Contact")]
    [InlineData("not-an-email", null, "Email")]
    [InlineData("Kavya <kavya@example.com>", null, "Email")] // display-name form would let a name into the address
    [InlineData(null, "call me", "Phone")]
    [InlineData(null, "12", "Phone")]
    public void ContactNeedsAValidEmailOrPhone(string? email, string? phone, string? error)
    {
        var errors = DemoRequestRules.Validate(Request(email, phone));
        if (error is null) Assert.Empty(errors);
        else Assert.Equal(error, Assert.Single(errors).Key);
    }

    [Fact]
    public void LengthsAndCountryAreChecked()
    {
        var errors = DemoRequestRules.Validate(new DemoRequest
        {
            Name = new string('a', DemoRequest.NameMax + 1), Organisation = "", Role = "x", Country = "US", Email = "a@example.com",
            Message = new string('m', DemoRequest.MessageMax + 1),
        });
        Assert.Equal(new Dictionary<string, string> { ["Name"] = "tooLong", ["Organisation"] = "required", ["Country"] = "required", ["Message"] = "tooLong" }, errors);
    }
}
