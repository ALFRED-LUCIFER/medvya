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
        Assert.Equal("91000000000001", CountryRules.India.NormalizeLookup(IdentifierScheme.Abha, "91 0000 0000 0001"));
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
        Assert.Equal(stored, CountryRules.Uae.NormalizeLookup(IdentifierScheme.EmiratesId, entered));

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

    [Fact]
    public void TodayIsTakenInTheCountryTimeZone() =>
        // 20:00 UTC on 20 Oct is already 21 Oct in India: a baby born "today" there must not be a future date.
        Assert.Equal(new DateOnly(2026, 10, 21), CountryRules.India.Today(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
}
