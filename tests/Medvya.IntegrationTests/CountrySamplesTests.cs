using System.Text.Json;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// #1417: the country formats the clinic registration form and the patient app copy, checked on the server against the one shared sample file
/// (packages/i18n/country-samples.json). The web and mobile tests read the same file, so a rule that drifts on either side fails a test.
/// </summary>
public class CountrySamplesTests
{
    static readonly DateOnly Today = new(2026, 10, 21);

    public static TheoryData<string, string, string, bool> Samples()
    {
        var data = new TheoryData<string, string, string, bool>();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "packages", "i18n", "country-samples.json")));
        foreach (var country in json.RootElement.EnumerateObject().Where(c => !c.Name.StartsWith('$')))
            foreach (var kind in country.Value.EnumerateObject())
                foreach (var verdict in new[] { "valid", "invalid" })
                    foreach (var sample in kind.Value.GetProperty(verdict).EnumerateArray())
                        data.Add(country.Name, kind.Name, sample.GetString()!, verdict == "valid");
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheServerAgreesWithTheSharedSample(string country, string kind, string sample, bool valid)
    {
        var india = PatientRulesTests.IndiaBase;
        var uae = PatientRulesTests.UaeBase;
        var (command, field) = (country, kind) switch
        {
            ("IN", "mobile") => (india with { Mobile = sample }, "mobile"),
            ("IN", "abha") => (india with { Identifiers = [new(IdentifierScheme.Abha, sample)] }, "identifiers"),
            ("IN", "abhaAddress") => (india with { Identifiers = [new(IdentifierScheme.AbhaAddress, sample)] }, "identifiers"),
            ("IN", "aadhaarLast4") => (india with { Identifiers = [new(IdentifierScheme.AadhaarLast4, sample)] }, "identifiers"),
            ("IN", "pin") => (india with { Address = new PatientAddress { Line1 = "12 Test Street", City = "Kochi", Region = "Kerala", PostalCode = sample } }, "address.postalCode"),
            ("AE", "mobile") => (uae with { Mobile = sample }, "mobile"),
            ("AE", "emiratesId") => (uae with { Identifiers = [new(IdentifierScheme.EmiratesId, sample, new DateOnly(2028, 1, 1))] }, "identifiers"),
            ("AE", "makani") => (uae with { Address = new PatientAddress { Line1 = "Villa 1", Region = "DXB", Makani = sample } }, "address.makani"),
            _ => throw new InvalidOperationException($"No server check for {country}.{kind}: add it here."),
        };
        var rules = CountryRules.For(country);
        if (valid) rules.Validate(command, Today);
        else Assert.Contains(Assert.Throws<PatientValidationException>(() => rules.Validate(command, Today)).Errors, e => e.Field.StartsWith(field, StringComparison.Ordinal));
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Medvya.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
