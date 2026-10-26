using Medvya.Audit.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-016 against real SQL Server: possible matches on name similarity + identical date of birth or an exact identifier
/// (deterministic Always Encrypted), never on phone; nothing is saved until every match is resolved, and decisions are audited.
/// Synthetic data only (names from the cast, without the "(synthetic)" seed marker, which would be a shared word); IDs fail their
/// checksums by construction.
/// </summary>
public sealed class PatientDuplicateTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), actor = Guid.NewGuid();
    static readonly DateOnly Dob = new(1979, 8, 14);

    PatientRegistry Registry(Guid? at = null) =>
        new(sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(at ?? facility),
            new User(actor), new AuditLog(sql.Audit(tenant)), TimeProvider.System, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));

    sealed class User(Guid id) : Medvya.BuildingBlocks.Domain.ICurrentUser { public Guid? UserId => id; }

    Task<RegistrationResult> Register(RegisterPatient c) => Registry(c.FacilityId).RegisterAsync(c);

    RegisterPatient India(string name, DateOnly? dob = null, string mobile = "+91 90000 00201", params IdentifierInput[] ids) =>
        IndiaBase with { FacilityId = facility, FullName = name, DateOfBirth = dob ?? Dob, SexAtBirth = SexAtBirth.Male, Mobile = mobile, Identifiers = ids };

    Task<IReadOnlyList<DuplicateCandidate>> Check(string country, string name, DateOnly? dob, params IdentifierInput[] ids) =>
        Registry().FindDuplicatesAsync(new(country, name, dob, ids));

    [Fact]
    public async Task ASimilarNameWithTheSameDateOfBirthIsShownOnlyAsInitialsAgeAndMaskedIds()
    {
        var existing = await Register(India("Deepak K. Nair", ids: new IdentifierInput(IdentifierScheme.Abha, "91-0000-0000-0301")));

        var candidate = Assert.Single(await Check("IN", "Deepak Nair", Dob));
        Assert.Equal((existing.PatientId, "DN", true), (candidate.PatientId, candidate.Initials, candidate.MatchedOnNameAndDateOfBirth));
        Assert.Equal([new MaskedIdentifier(IdentifierScheme.Abha, "0301")], candidate.Identifiers);
        Assert.Equal(existing.Mrn[^4..], candidate.MrnTail);
        // The row type has no name, phone, date of birth or full identifier to leak before "Use existing record".
        Assert.Equal(["AgeYears", "Identifiers", "Initials", "MatchedIdentifiers", "MatchedOnNameAndDateOfBirth", "MrnTail", "PatientId", "SexAtBirth"],
            typeof(DuplicateCandidate).GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task AnExactAbhaEmiratesIdOrPassportMatchesWhateverTheName()
    {
        var abha = await Register(India("Lakshmi Pillai", ids: new IdentifierInput(IdentifierScheme.Abha, "91000000000302")));
        var uaeEid = await Register(UaeBase with { FacilityId = facility, FullName = "Fatima Ahmed Al Mansoori", Identifiers = [new(IdentifierScheme.EmiratesId, "784-1988-1234567-1")] });
        var uaePassport = await Register(UaeBase with { FacilityId = facility, FullName = "Omar Haddad", Identifiers = [new(IdentifierScheme.Passport, "SYNP1234567")] });

        Assert.Equal(abha.PatientId, Assert.Single(await Check("IN", "Someone Else", null, new IdentifierInput(IdentifierScheme.Abha, "91-0000-0000-0302"))).PatientId);
        var eid = Assert.Single(await Check("AE", "Other Name", null, new IdentifierInput(IdentifierScheme.EmiratesId, "784198812345671")));
        Assert.Equal((uaeEid.PatientId, false), (eid.PatientId, eid.MatchedOnNameAndDateOfBirth));
        Assert.Equal([IdentifierScheme.EmiratesId], eid.MatchedIdentifiers);
        Assert.Equal(uaePassport.PatientId, Assert.Single(await Check("AE", "Other Name", null, new IdentifierInput(IdentifierScheme.Passport, "synp1234567"))).PatientId);
        // Half-typed identifiers are skipped while the desk types, never partially matched.
        Assert.Empty(await Check("IN", "Someone Else", null, new IdentifierInput(IdentifierScheme.Abha, "91-0000-0000")));
    }

    [Fact]
    public async Task ASharedPhoneNumberAloneNeverProducesASuggestion()
    {
        await Register(India("Lakshmi Nair", new DateOnly(1981, 2, 3), mobile: "+91 90000 00209"));
        Assert.Empty(await Check("IN", "Kavya Menon", Dob)); // same phone in the family, different person
        // Even the duplicate check's input has no phone field to match on.
        Assert.DoesNotContain(typeof(DuplicateQuery).GetProperties(), p => p.Name.Contains("Phone", StringComparison.Ordinal) || p.Name.Contains("Mobile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADifferentDateOfBirthADissimilarNameOrAnEstimatedDateIsNotAMatch()
    {
        await Register(India("Meera Iyer"));
        await Register(India("Rahul Menon") with { DateOfBirth = null, AgeYears = 61 });

        Assert.Empty(await Check("IN", "Meera Iyer", Dob.AddDays(1)));
        Assert.Empty(await Check("IN", "Meera Rao", Dob));
        // A stated age only gives an estimated date: unknowns are never guessed into a match.
        var estimated = CountryRules.India.Today(DateTimeOffset.UtcNow).AddYears(-61);
        Assert.Empty(await Check("IN", "Rahul Menon", estimated));
        Assert.Empty(await Check("IN", "Rahul Menon", null));
    }

    [Fact]
    public async Task AnotherFacilitysPatientIsNotACandidate()
    {
        await Register(India("Suresh Reddy") with { FacilityId = Guid.NewGuid() });
        Assert.Empty(await Check("IN", "Suresh Reddy", Dob));
    }

    [Fact]
    public async Task NothingIsSavedUntilEveryPossibleMatchIsResolvedAndTheDecisionIsAudited()
    {
        var existing = await Register(India("Priya Shah"));
        var again = India("Priya Shah", mobile: "+91 90000 00299");

        var refused = await Assert.ThrowsAsync<PatientValidationException>(() => Register(again));
        Assert.Equal([new ValidationError("duplicates", "unresolved")], refused.Errors);
        // Quick mode runs the same check (MDV-P1-015).
        await Assert.ThrowsAsync<PatientValidationException>(() => Register(again with { Quick = true }));
        // A decision about some other record does not cover this one.
        await Assert.ThrowsAsync<PatientValidationException>(() => Register(again with { Duplicates = new(DuplicateDecision.NotSamePerson, [Guid.NewGuid()]) }));
        await using (var db = sql.Patients(tenant)) Assert.Equal(1, await db.Patients.CountAsync(p => p.FullName == "Priya Shah"));

        var notSame = await Register(again with { Duplicates = new(DuplicateDecision.NotSamePerson, [existing.PatientId]) });
        var review = await Register(again with { Duplicates = new(DuplicateDecision.MergeReview, [existing.PatientId, notSame.PatientId]) });

        await using (var db = sql.Patients(tenant))
        {
            var decisions = await db.MergeRequests.OrderBy(m => m.DecidedAt).ThenBy(m => m.CandidatePatientId == existing.PatientId ? 0 : 1)
                .Select(m => new { m.PatientId, m.CandidatePatientId, m.Status, m.DecidedBy }).ToListAsync();
            Assert.Equal([
                new { PatientId = notSame.PatientId, CandidatePatientId = existing.PatientId, Status = MergeRequestStatus.NotSame, DecidedBy = (Guid?)actor },
                new { PatientId = review.PatientId, CandidatePatientId = existing.PatientId, Status = MergeRequestStatus.Proposed, DecidedBy = (Guid?)actor },
                new { PatientId = review.PatientId, CandidatePatientId = notSame.PatientId, Status = MergeRequestStatus.Proposed, DecidedBy = (Guid?)actor },
            ], decisions);
        }
        await using var audit = sql.Audit(tenant);
        var trail = await audit.Events.Where(e => e.PatientId == notSame.PatientId || e.PatientId == review.PatientId)
            .Select(e => new { e.Action, e.ActorUserId }).ToListAsync();
        Assert.Equal(["patient.duplicate.merge_review", "patient.duplicate.merge_review", "patient.duplicate.not_same", "patient.register", "patient.register"],
            trail.Select(e => e.Action).Order());
        Assert.All(trail, e => Assert.Equal(actor, e.ActorUserId));

        // Row-level security covers the decisions too: another tenant sees none, even with the EF filter off.
        await using var other = sql.Patients(Guid.NewGuid());
        Assert.Empty(await other.MergeRequests.IgnoreQueryFilters().ToListAsync());
    }
}
