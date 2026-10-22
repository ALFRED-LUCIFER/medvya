using Medvya.Patients.Application;
using Medvya.Patients.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-013/014/015 validation at the application boundary: country rules, no database. Synthetic values only.</summary>
public class PatientRulesTests
{
    static readonly DateOnly Today = new(2026, 10, 21);

    internal static readonly RegisterPatient IndiaBase = new()
    {
        FacilityId = Guid.NewGuid(), CountryCode = "IN", FullName = "Test Patient (synthetic)", DateOfBirth = new DateOnly(1980, 1, 1),
        SexAtBirth = SexAtBirth.Female, Mobile = "+91 90000 00002",
    };

    internal static readonly RegisterPatient UaeBase = IndiaBase with
    {
        CountryCode = "AE", Mobile = "050 000 0001", Identifiers = [new(IdentifierScheme.EmiratesId, "784-1990-1234567-1", new DateOnly(2028, 1, 1))],
    };

    static IReadOnlyList<ValidationError> Errors(RegisterPatient c) =>
        Assert.Throws<PatientValidationException>(() => CountryRules.For(c.CountryCode).Validate(c, Today)).Errors;

    [Fact]
    public void IndiaMinimumIsNameAgeSexAndMobile()
    {
        var v = CountryRules.India.Validate((IndiaBase with { DateOfBirth = null, AgeYears = 47 }), Today);
        Assert.Equal((new DateOnly(1979, 10, 21), true, "+919000000002"), (v.DateOfBirth, v.DateOfBirthEstimated, v.Command.Mobile));
        Assert.Contains(new ValidationError("dateOfBirth", "required"), Errors((IndiaBase with { DateOfBirth = null })));
        Assert.Contains(new ValidationError("mobile", "required"), Errors((IndiaBase with { Mobile = " " })));
        Assert.Contains(new ValidationError("fullName", "required"), Errors((IndiaBase with { FullName = "" })));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("ABCDEF")]
    public void IndiaPinMustBeSixDigits(string pin) =>
        Assert.Contains(new ValidationError("address.postalCode", "format"),
            Errors((IndiaBase with { Address = new PatientAddress { Line1 = "12 Test Street", City = "Kochi", Region = "Kerala", PostalCode = pin } })));

    [Fact]
    public void AadhaarKeepsOnlyTheLastFourDigits()
    {
        var full = (IndiaBase with { Identifiers = [new(IdentifierScheme.AadhaarLast4, "0000 1111 2222")] });
        Assert.Equal([new ValidationError("identifiers[0]", "aadhaar_full_number_not_allowed")], Errors(full));

        var last4 = CountryRules.India.Validate((IndiaBase with { Identifiers = [new(IdentifierScheme.AadhaarLast4, " 2222 ")] }), Today);
        Assert.Equal("2222", last4.Command.Identifiers.Single().Value);
    }

    [Fact]
    public void AbhaIsFourteenDigitsStoredWithoutSeparators()
    {
        var v = CountryRules.India.Validate((IndiaBase with { Identifiers = [new(IdentifierScheme.Abha, "91-0000-0000-0001")] }), Today);
        Assert.Equal("91000000000001", v.Command.Identifiers.Single().Value);
        Assert.Equal(new SearchTerm(SearchKind.Identifier, "91000000000001", IdentifierScheme.Abha), CountryRules.India.ParseSearch("91 0000 0000 0001"));
        Assert.Contains(new ValidationError("identifiers[0]", "format"),
            Errors((IndiaBase with { Identifiers = [new(IdentifierScheme.Abha, "9100000000")] })));
    }

    [Fact]
    public void EachCountryAcceptsOnlyItsOwnIdentifiersAndPayers()
    {
        Assert.Contains(new ValidationError("identifiers[0]", "scheme_not_allowed"),
            Errors((IndiaBase with { Identifiers = [new(IdentifierScheme.EmiratesId, "784-1990-1234567-1")] })));
        Assert.Contains(new ValidationError("payer.kind", "not_allowed"),
            Errors((UaeBase with { Payer = new(PayerKind.GovernmentScheme, "Scheme (synthetic)") })));
        Assert.Contains(new ValidationError("payer.memberNumber", "required"),
            Errors((UaeBase with { Payer = new(PayerKind.PrivateInsurance, "Insurer (synthetic)") })));
        Assert.Throws<PatientValidationException>(() => CountryRules.For("US"));
    }

    [Theory]
    [InlineData("784-1990-1234567-1", "784-1990-1234567-1")]
    [InlineData("784199012345671", "784-1990-1234567-1")]
    public void EmiratesIdIsNormalisedToThePrintedFormat(string entered, string stored) =>
        Assert.Equal(new SearchTerm(SearchKind.Identifier, stored, IdentifierScheme.EmiratesId), CountryRules.Uae.ParseSearch(entered));

    [Theory]
    [InlineData("784-90-1234567-1")]
    [InlineData("785-1990-1234567-1")]
    [InlineData("784-1990-123456-1")]
    public void InvalidEmiratesIdFormatIsRejected(string eid) =>
        Assert.Contains(new ValidationError("identifiers[0]", "format"),
            Errors((UaeBase with { Identifiers = [new(IdentifierScheme.EmiratesId, eid)] })));

    [Fact]
    public void ExpiredEmiratesIdWarnsButNeverBlocks()
    {
        var v = CountryRules.Uae.Validate((UaeBase with { Identifiers = [new(IdentifierScheme.EmiratesId, "784-1990-1234567-1", Today.AddDays(-1))] }), Today);
        Assert.Equal(["emirates_id.expired"], v.Warnings);
    }

    [Fact]
    public void UaeNeedsEmiratesIdOrPassportAndQuickModeNeedsTheEmiratesId()
    {
        var passportOnly = (UaeBase with { Identifiers = [new(IdentifierScheme.Passport, "SYN12345")] });
        CountryRules.Uae.Validate(passportOnly, Today);
        Assert.Equal([new ValidationError("identifiers", "emirates_id_required")], Errors(passportOnly with { Quick = true }));
        Assert.Equal([new ValidationError("identifiers", "emirates_id_or_passport_required")], Errors(passportOnly with { Identifiers = [] }));
        Assert.Contains(new ValidationError("dateOfBirth", "required"), Errors((UaeBase with { DateOfBirth = null, AgeYears = 38 })));
    }

    [Fact]
    public void UaeAddressNeedsAKnownEmirateWhichSelectsTheHie()
    {
        var v = CountryRules.Uae.Validate((UaeBase with { Address = new PatientAddress { Line1 = "Villa 1", Region = "auh", Makani = "1234567890" } }), Today);
        Assert.Equal("AUH", v.Command.Address!.Region);
        Assert.Contains(new ValidationError("address.region", "emirate_required"),
            Errors((UaeBase with { Address = new PatientAddress { Line1 = "Villa 1", Region = "Kerala" } })));
        Assert.Equal(("Malaffi", "NABIDH", "Riayati"), (UaeRules.HieFor("AUH"), UaeRules.HieFor("DXB"), UaeRules.HieFor("RAK")));
    }

    [Fact]
    public void ValidationMessagesNeverContainTheEnteredValues()
    {
        var ex = Assert.Throws<PatientValidationException>(() => CountryRules.India.Validate(
            (IndiaBase with { Mobile = "SECRET-MOBILE", Identifiers = [new(IdentifierScheme.AadhaarLast4, "000011112222")] }), Today));
        Assert.DoesNotContain("SECRET", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("000011112222", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IN", "", SearchKind.Directory, null)]
    [InlineData("IN", "  meera ", SearchKind.Name, "meera")]
    [InlineData("IN", "+91 90000 00101", SearchKind.Phone, "+919000000101")]
    [InlineData("IN", "09000000101", SearchKind.Phone, "+919000000101")]
    [InlineData("IN", "deepak.nair.synthetic@sbx", SearchKind.Identifier, "deepak.nair.synthetic@sbx")]
    [InlineData("IN", "42", SearchKind.Mrn, "000042")]
    [InlineData("IN", "dq-10-4482", SearchKind.Mrn, "DQ-10-4482")]
    [InlineData("AE", "050 000 0201", SearchKind.Phone, "+971500000201")]
    [InlineData("AE", "فاطمة", SearchKind.Name, "فاطمة")]
    public void DeskSearchReadsNamesPhonesMrnsAndIdentifiers(string country, string query, SearchKind kind, string? value)
    {
        var term = CountryRules.For(country).ParseSearch(query);
        Assert.Equal((kind, value), (term.Kind, term.Value));
    }

    [Theory]
    [InlineData("IN", "9100000000010", "full_number_required")] // 13 of the 14 ABHA digits
    [InlineData("IN", "98470 213", "full_number_required")]     // part of a mobile number
    [InlineData("IN", "0000 1111 2222", "full_number_required")] // a full Aadhaar is never a search key
    [InlineData("AE", "784-1990-1234567", "full_number_required")]
    [InlineData("IN", "M", "too_short")]
    public void PartialNumbersAreRefusedNotPartiallyMatched(string country, string query, string code) =>
        Assert.Equal([new ValidationError("query", code)],
            Assert.Throws<PatientValidationException>(() => CountryRules.For(country).ParseSearch(query)).Errors);

    [Fact]
    public void TodayIsTakenInTheCountryTimeZone() =>
        // 20:00 UTC on 20 Oct is already 21 Oct in India: a baby born "today" there must not be a future date.
        Assert.Equal(new DateOnly(2026, 10, 21), CountryRules.India.Today(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
}
