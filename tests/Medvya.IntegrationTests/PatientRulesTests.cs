using Medvya.Patients.Application;
using Medvya.Patients.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-013/014/015 validation at the application boundary: country rules, no database. Synthetic values only.</summary>
public class PatientRulesTests
{
    static readonly DateOnly Today = new(2026, 10, 21);

    /// <summary>Treatment granted, the one decision a registration cannot do without.</summary>
    internal static readonly ConsentSet TreatmentConsent = new(ConsentPolicy.CurrentVersion, [new(ConsentType.Treatment, null, true)]);

    /// <summary>The same consent as an HTTP body.</summary>
    internal static readonly object TreatmentConsentBody = new { policyVersion = ConsentPolicy.CurrentVersion, decisions = new object[] { new { type = "Treatment", granted = true } } };

    internal static readonly RegisterPatient IndiaBase = new()
    {
        Consents = TreatmentConsent,
        FacilityId = Guid.NewGuid(), CountryCode = "IN", FullName = "Test Patient (synthetic)", DateOfBirth = new DateOnly(1980, 1, 1),
        SexAtBirth = SexAtBirth.Female, Mobile = "+91 90000 00002",
        EmergencyContact = new("Lakshmi Nair (synthetic)", RelationshipKind.Spouse, "+91 90000 00009"),
    };

    internal static readonly ContactInput UaeContact = new("Ahmed Al Mansoori (synthetic)", RelationshipKind.Spouse, "050 000 0009");

    internal static readonly RegisterPatient UaeBase = IndiaBase with
    {
        CountryCode = "AE", Mobile = "050 000 0001", EmergencyContact = UaeContact, Identifiers = [new(IdentifierScheme.EmiratesId, "784-1990-1234567-1", new DateOnly(2028, 1, 1))],
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
    // #880: nothing visible is empty; NUL and bidi overrides are refused, not saved (NUL used to fail as a 500 in PostgreSQL).
    [InlineData("\u200b\u200b", "required")]
    [InlineData("Qa\u0000Tester", "format")]
    [InlineData("Qa \u202eTester", "format")]
    [InlineData("Qa \u2067Tester", "format")]
    [InlineData("1234567", "format")]
    public void ANameMustHaveVisibleLettersAndNoHiddenControls(string name, string code) =>
        Assert.Contains(new ValidationError("fullName", code), Errors(IndiaBase with { FullName = name }));

    [Fact]
    public void ZeroWidthJoinersInsideAnIndicNameAreKept() => // Malayalam chillu forms are written with ZWJ
        Assert.Equal("Lakshmi \u0d2e\u0d4d\u200d (synthetic)",
            CountryRules.India.Validate(IndiaBase with { FullName = "Lakshmi \u0d2e\u0d4d\u200d (synthetic)" }, Today).Command.FullName);

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("ABCDEF")]
    public void IndiaPinMustBeSixDigits(string pin) =>
        Assert.Contains(new ValidationError("address.postalCode", "format"),
            Errors((IndiaBase with { Address = new PatientAddress { Line1 = "12 Test Street", City = "Kochi", Region = "Kerala", PostalCode = pin } })));

    [Fact]
    public void IndiaAddressNeedsAKnownStateOrUnionTerritory()
    {
        PatientAddress At(string region) => new() { Line1 = "12 Test Street", City = "Kochi", Region = region, PostalCode = "682001" };
        Assert.Equal("Kerala", CountryRules.India.Validate((IndiaBase with { Address = At(" Kerala ") }), Today).Command.Address!.Region);
        Assert.Equal(36, IndiaRules.StatesAndUnionTerritories.Count);
        Assert.Contains(new ValidationError("address.region", "not_allowed"), Errors((IndiaBase with { Address = At("Kerela") })));
        Assert.Contains(new ValidationError("address.region", "not_allowed"), Errors((IndiaBase with { Address = At("AUH") })));
    }

    [Fact]
    public void FullRegistrationNeedsAnEmergencyContactQuickModeDoesNot()
    {
        Assert.Contains(new ValidationError("emergencyContact", "required"), Errors(IndiaBase with { EmergencyContact = null }));
        CountryRules.India.Validate(IndiaBase with { EmergencyContact = null, Quick = true }, Today);
        Assert.Equal([new ValidationError("emergencyContact.name", "required"), new ValidationError("emergencyContact.phone", "format")],
            Errors(IndiaBase with { EmergencyContact = new(" ", RelationshipKind.Parent, "12345") }));
        Assert.Equal("+919000000009", CountryRules.India.Validate(IndiaBase, Today).Command.EmergencyContact!.Phone);
    }

    /// <summary>SAFETY (MDV-P1-020): under 18 on the registration day means a guardian, in quick mode too; the 18th birthday counts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMinorIsNeverRegisteredWithoutAGuardian(bool quick)
    {
        var seventeen = IndiaBase with { Quick = quick, DateOfBirth = Today.AddYears(-18).AddDays(1) }; // 18 tomorrow
        Assert.Equal([new ValidationError("guardian", "required_for_minor")], Errors(seventeen));
        CountryRules.India.Validate(seventeen with { Guardian = new("Ravi Nair (synthetic)", RelationshipKind.Parent, "+91 90000 00010") }, Today);
        CountryRules.India.Validate(IndiaBase with { Quick = quick, DateOfBirth = Today.AddYears(-18) }, Today); // 18 today: an adult
        Assert.Equal([new ValidationError("guardian", "required_for_minor")], Errors(UaeBase with { Quick = quick, DateOfBirth = Today.AddYears(-5) }));
    }

    /// <summary>An estimated date of birth is never treated as known: the stated age decides, and 17 is a minor.</summary>
    [Fact]
    public void WithAnEstimatedDateOfBirthTheStatedAgeDecides()
    {
        Assert.Equal([new ValidationError("guardian", "required_for_minor")], Errors(IndiaBase with { DateOfBirth = null, AgeYears = 17 }));
        Assert.True(CountryRules.India.Validate(IndiaBase with { DateOfBirth = null, AgeYears = 18 }, Today).DateOfBirthEstimated);
        Assert.True(CountryRules.IsMinor(Today, Today.AddYears(-40), estimated: true, statedAge: null)); // estimated without an age: unknown, so a guardian is asked for
        Assert.False(CountryRules.IsMinor(Today, Today.AddYears(-18), estimated: false, statedAge: null));
    }

    [Fact]
    public void RelationshipValuesAreCountrySpecific()
    {
        var sponsor = new ContactInput("Sponsor (synthetic)", RelationshipKind.Sponsor, "+91 90000 00011");
        Assert.Contains(new ValidationError("emergencyContact.relationship", "not_allowed"), Errors(IndiaBase with { EmergencyContact = sponsor }));
        CountryRules.Uae.Validate(UaeBase with { DateOfBirth = Today.AddYears(-10), Guardian = sponsor with { Phone = "050 000 0011" } }, Today);
        Assert.Contains(new ValidationError("guardian.relationship", "required"), Errors(IndiaBase with { Guardian = new("A (synthetic)", null, "+91 90000 00012") }));
    }

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
    [InlineData("IN", "0091 98470 21345", SearchKind.Phone, "+919847021345")] // 14 digits like an ABHA, but an international prefix
    [InlineData("IN", "deepak.nair.synthetic@sbx", SearchKind.Identifier, "deepak.nair.synthetic@sbx")]
    [InlineData("IN", "42", SearchKind.Mrn, "000042")]
    [InlineData("IN", "dq-10-4482", SearchKind.Mrn, "DQ-10-4482")]
    [InlineData("AE", "050 000 0201", SearchKind.Phone, "+971500000201")]
    [InlineData("AE", "فاطمة", SearchKind.Name, "فاطمة")]
    [InlineData("IN", "98470 213", SearchKind.Number, "98470213")]      // part of a mobile number
    [InlineData("IN", "000123", SearchKind.Number, "000123")]           // six digits: the registry tries it as an MRN first, then as a phone fragment
    [InlineData("IN", "1234", SearchKind.Number, "1234")]               // four digits: a phone fragment, an MRN or an Aadhaar last 4
    [InlineData("IN", "9100000000010", SearchKind.Number, "9100000000010")] // 13 of the 14 ABHA digits: only a phone fragment
    [InlineData("IN", "0000 1111 2222", SearchKind.Identifier, "2222")] // a typed Aadhaar is cut to its last 4 here
    [InlineData("AE", "784-1990-1234567", SearchKind.Number, "78419901234567")]
    public void DeskSearchReadsNamesPhonesMrnsAndIdentifiers(string country, string query, SearchKind kind, string? value)
    {
        var term = CountryRules.For(country).ParseSearch(query);
        Assert.Equal((kind, value), (term.Kind, term.Value));
    }

    [Fact]
    public void ATypedAadhaarIsSearchedByItsLastFourOnly()
    {
        var term = CountryRules.India.ParseSearch("0000 1111 2222");
        Assert.Equal(new SearchTerm(SearchKind.Identifier, "2222", IdentifierScheme.AadhaarLast4), term);
        Assert.DoesNotContain("0000", term.Value, StringComparison.Ordinal); // the rest is dropped, never carried further
    }

    [Theory]
    [InlineData("IN", "1234 5678 9012 3456", "full_number_required")] // 16 digits: no phone fragment or ID this long
    [InlineData("AE", "1234567890123456", "full_number_required")]
    [InlineData("IN", "M", "too_short")]
    [InlineData("IN", "-", "too_short")]   // separators only: nothing to look up (was a 500)
    [InlineData("AE", "( )", "too_short")]
    [InlineData("IN", "+", "too_short")]
    public void TooLongNumbersAndEmptySearchesAreRefused(string country, string query, string code) =>
        Assert.Equal([new ValidationError("query", code)],
            Assert.Throws<PatientValidationException>(() => CountryRules.For(country).ParseSearch(query)).Errors);

    [Theory]
    [InlineData("Deepak Nair", "Deepak K. Nair", true)]                 // the design's example: middle initial
    [InlineData("Fatima Ahmed Al Mansoori", "Fatima Al Mansouri", true)] // the UAE example: spelling and a dropped name
    [InlineData("NAIR, Deepak", "deepak nair", true)]                    // order, case and punctuation
    [InlineData("Zoë Mathew", "Zoe Mathew", true)]                       // accents
    [InlineData("Meera", "Meera Iyer", true)]                            // one-word name
    [InlineData("Meera Iyer", "Meera Rao", false)]                       // shared first name only
    [InlineData("Kavya Iyer", "Divya Iyer", false)]                      // twins share a surname and birthday
    [InlineData("A. B.", "A. B.", false)]                                // initials alone are not a name
    public void NameSimilarityToleratesSpellingButNotADifferentPerson(string a, string b, bool similar) =>
        Assert.Equal(similar, NameMatch.Similar(a, b));

    [Fact]
    public void OnlyCompleteExactMatchIdentifiersAreUsedToFindDuplicates()
    {
        var keys = CountryRules.India.MatchKeys([
            new(IdentifierScheme.Abha, "91-0000-0000-0001"), new(IdentifierScheme.AadhaarLast4, "0001"),
            new(IdentifierScheme.Passport, " synp123456 "), new(IdentifierScheme.AbhaAddress, "half.typed@"),
            new(IdentifierScheme.EmiratesId, "784-1990-1234567-1")]); // not an Indian scheme
        Assert.Equal([new IdentifierInput(IdentifierScheme.Abha, "91000000000001"), new IdentifierInput(IdentifierScheme.Passport, "SYNP123456")], keys);
    }

    [Fact]
    public void TodayIsTakenInTheCountryTimeZone() =>
        // 20:00 UTC on 20 Oct is already 21 Oct in India: a baby born "today" there must not be a future date.
        Assert.Equal(new DateOnly(2026, 10, 21), CountryRules.India.Today(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
}
