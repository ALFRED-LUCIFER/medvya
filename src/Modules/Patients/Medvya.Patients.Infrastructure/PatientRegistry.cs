using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Patient registration, directory and search. Each write and its audit events run in one database transaction (fail closed): the
/// events commit with the change, and a failed audit write rolls the change back. Reads do not audit here: the calling endpoint's
/// <c>.Audited()</c> filter writes one event before the answer is sent, so each request leaves exactly one event.
/// Reads and writes reach the active facility's patients only: a registration command must name the active facility (the HTTP
/// endpoint fills it from the session, never from the request body), and another facility's patient is not found for completion.
/// shortcut: reads see the active facility's patients only; sharing patients across one tenant's facilities waits for multi-site rules (P7).
/// </summary>
public sealed class PatientRegistry(PatientsDbContext db, ICurrentTenant tenant, ICurrentFacility facility, ICurrentUser user, IAuditLog audit, TimeProvider clock,
    IFacilityDirectory facilities) : IPatientRegistry
{
    const string Purpose = "registration";

    public async Task<RegistrationResult> RegisterAsync(RegisterPatient command, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = IdempotencyHashes.Of(idempotencyKey, command);
        var tenantId = TenantId();
        // Only at the facility the desk is working at: the use case never trusts a facility its caller did not sign in to.
        if (command.FacilityId != FacilityId()) throw new InvalidOperationException("A registration must be at the active facility.");
        var now = clock.GetUtcNow();
        var rules = CountryRules.For(command.CountryCode);
        var (c, dob, estimated, warnings) = rules.Validate(command, await FacilityTodayAsync(command.FacilityId, now, cancellationToken));
        var consents = ConsentRules.ForRegistration(c.Consents); // no consent, no registration

        // A repeat comes before the duplicate check: the first attempt's patient would otherwise show up as an unresolved duplicate of itself.
        if (key is not null && await ReplayAsync(key, now, warnings, cancellationToken) is { } first) return first;

        // Checked here as well as while typing: the save is refused until the desk has decided about every possible match.
        var duplicates = await DuplicatesAsync(c.FacilityId, c.FullName, estimated ? null : dob, rules.MatchKeys(c.Identifiers), cancellationToken);
        var resolved = c.Duplicates?.CandidateIds ?? [];
        if (duplicates.Any(d => !resolved.Contains(d.PatientId)))
        {
            // An identical request that raced this one may have registered since the replay check above, and now shows up as a duplicate of itself.
            if (key is not null && await ReplayAsync(key, now, warnings, cancellationToken) is { } raced) return raced;
            throw new PatientValidationException([new("duplicates", "unresolved")]);
        }

        try
        {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var patient = new Patient
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = c.FacilityId, CountryCode = c.CountryCode,
            Mrn = await NextMrnAsync(tenantId, c.FacilityId, cancellationToken),
            FullName = c.FullName, ArabicName = c.ArabicName, DateOfBirth = dob, DateOfBirthEstimated = estimated, SexAtBirth = c.SexAtBirth,
            GenderIdentity = c.GenderIdentity, PreferredLanguage = c.PreferredLanguage, Nationality = c.Nationality,
            Mobile = c.Mobile, AlternatePhone = c.AlternatePhone, Email = c.Email, Address = c.Address,
            Status = StatusFor(c.Address, c.Payer is not null, c.EmergencyContact is not null),
            RegisteredBy = user.UserId, RegisteredAt = now, ChangedBy = user.UserId, ChangedAt = now,
            IdempotencyKeyHash = key?.Key, IdempotencyRequestHash = key?.Request,
        };
        db.Patients.Add(patient);
        db.Identifiers.AddRange(c.Identifiers.Select(i => new PatientIdentifier
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patient.Id, Scheme = i.Scheme, ExpiresOn = i.ExpiresOn,
            LookupValue = PatientIdentifier.IsLookup(i.Scheme) ? i.Value : null,
            DisplayValue = PatientIdentifier.IsLookup(i.Scheme) ? null : i.Value,
        }));
        if (c.Payer is not null) db.PayerCoverages.Add(Coverage(tenantId, patient.Id, c.Payer));
        // Names to call, never users: no account, membership or portal access is created for a contact (MDV-P1-020).
        if (c.EmergencyContact is { } emergency) db.Relationships.Add(Relationship(tenantId, patient.Id, emergency, isEmergencyContact: true));
        if (c.Guardian is { } guardian) db.Relationships.Add(Relationship(tenantId, patient.Id, guardian, isEmergencyContact: false));
        db.ConsentReceipts.AddRange(PatientConsents.Receipts(tenantId, patient.Id, consents, ConsentRules.Jurisdiction(c.CountryCode, c.Address?.Region), now, user.UserId));
        var decisions = duplicates.Select(d => new MergeRequest
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patient.Id, CandidatePatientId = d.PatientId,
            Status = c.Duplicates!.Decision == DuplicateDecision.MergeReview ? MergeRequestStatus.Proposed : MergeRequestStatus.NotSame,
            DecidedBy = user.UserId, DecidedAt = now,
        }).ToList();
        db.MergeRequests.AddRange(decisions);
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync(tx, "patient.register", patient.Id, now, cancellationToken); // PatientRegistered.v1: opaque ids only
        foreach (var d in decisions)
            await AuditAsync(tx, d.Status == MergeRequestStatus.Proposed ? "patient.duplicate.merge_review" : "patient.duplicate.not_same",
                patient.Id, now, cancellationToken, "merge_request", d.Id);
        await tx.CommitAsync(cancellationToken);
        return new(patient.Id, patient.Mrn, now, user.UserId, patient.Status, warnings);
        }
        // An identical request that raced this one committed first (unique index on the key): answer with its outcome.
        catch (DbUpdateException e) when (key is not null && e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            if (await ReplayAsync(key, now, warnings, cancellationToken) is { } raced) return raced;
            throw;
        }
    }

    /// <summary>
    /// The patient this user already registered with this key. Keeps only hashes with the key (no patient data); the answer is built
    /// from the patient record. Clears expired keys of the tenant first. Another request under the same key is refused.
    /// </summary>
    async Task<RegistrationResult?> ReplayAsync(IdempotencyHashes key, DateTimeOffset now, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        // shortcut: expired keys are cleared by the next keyed registration of the tenant, not by a scheduled job; a quiet tenant keeps
        // some stale hashes (no patient data) until then. Upgrade to a nightly sweep with the retention jobs.
        await db.Patients.Where(p => p.IdempotencyKeyHash != null && p.RegisteredAt < now - IdempotencyHashes.Lifetime)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IdempotencyKeyHash, (string?)null).SetProperty(p => p.IdempotencyRequestHash, (string?)null), cancellationToken);
        var me = user.UserId;
        var first = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.RegisteredBy == me && p.IdempotencyKeyHash == key.Key, cancellationToken);
        if (first is null) return null;
        if (first.IdempotencyRequestHash != key.Request) throw new IdempotencyKeyException("reused");
        return new(first.Id, first.Mrn, first.RegisteredAt, first.RegisteredBy, first.Status, warnings);
    }

    public Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default) => LoadAsync(patientId, cancellationToken);

    IQueryable<Patient> IdentifierMatch(IQueryable<Patient> patients, IdentifierScheme scheme, string value)
    {
        var index = db.LookupIndexFor(value);
        return patients.Where(p => db.Identifiers.Any(i => i.PatientId == p.Id && i.Scheme == scheme && EF.Property<byte[]>(i, NationalIdColumns.LookupIndex) == index));
    }

    public async Task<PatientSearchResult> SearchAsync(PatientSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Page is < 1 or > PatientSearch.MaxPage) throw new PatientValidationException([new("page", "out_of_range")]);
        var term = CountryRules.For(search.CountryCode).ParseSearch(search.Query);
        var now = clock.GetUtcNow();

        var patients = FacilityPatients();
        // The survivors of merged records with this MRN, looked up first on the unique MRN index: a correlated EXISTS in the same OR made the MRN search read the whole facility (0.9 s at 1,000,000 patients).
        var survivors = term.Kind == SearchKind.Mrn
            ? await db.Patients.AsNoTracking().Where(m => m.FacilityId == FacilityId() && m.Mrn == term.Value && m.MergedIntoPatientId != null).Select(m => m.MergedIntoPatientId!.Value).ToListAsync(cancellationToken)
            : [];
        var tokens = term.Kind == SearchKind.Name ? await NameTokensAsync(term.Value!, cancellationToken) : [];
        patients = term.Kind switch
        {
            SearchKind.Name => Contains(patients, tokens),
            SearchKind.Phone => patients.Where(p => p.Mobile == term.Value || p.AlternatePhone == term.Value),
            // Either MRN of a merged pair finds the survivor (MDV-P1-017); the merged record itself is hidden below.
            SearchKind.Mrn => patients.Where(p => p.Mrn == term.Value || survivors.Contains(p.Id)),
            // Exact match on the HMAC blind index (MDV-P0-035); the value itself is ciphertext and never compared in SQL.
            SearchKind.Identifier => IdentifierMatch(patients, term.Scheme!.Value, term.Value!),
            _ => patients,
        };
        IQueryable<Patient> Filtered(IQueryable<Patient> q) => search.Filter switch
        {
            DirectoryFilter.Incomplete => q.Where(p => p.Status == RegistrationStatus.Incomplete),
            DirectoryFilter.Recent => q.Where(p => p.RegisteredAt >= now.AddDays(-PatientSearch.RecentDays)),
            _ => q,
        };
        patients = Filtered(patients);

        var total = await patients.CountAsync(cancellationToken);
        // Too few exact hits: forgive typos too (a missing or transposed letter, a near spelling). Exact hits still rank first.
        var fuzzy = term.Kind == SearchKind.Name && tokens.Count > 0 && total < FuzzyBelow;
        if (fuzzy) patients = Filtered(Contains(FacilityPatients(), tokens, await SwappedPatternsAsync(tokens, cancellationToken)));
        var phrase = string.Join(' ', tokens); // ranks: words together at a word start, then closest, then A to Z
        var ordered = term.Kind == SearchKind.Name
            ? patients.OrderByDescending(p => EF.Functions.Like(EF.Property<string>(p, SearchKey), phrase + "%") || EF.Functions.Like(EF.Property<string>(p, SearchKey), "% " + phrase + "%"))
                .ThenByDescending(p => EF.Functions.TrigramsSimilarity(EF.Property<string>(p, SearchKey), phrase)).ThenBy(p => p.FullName).ThenBy(p => p.Id)
            : patients.OrderByDescending(p => p.RegisteredAt).ThenBy(p => p.Id);
        var shown = ordered.Select(p => new { p.Id, p.FullName, p.DateOfBirth, p.DateOfBirthEstimated, p.SexAtBirth, p.Mrn, p.Status, p.CountryCode });
        List<(Guid Id, string FullName, DateOnly DateOfBirth, bool DateOfBirthEstimated, SexAtBirth SexAtBirth, string Mrn, RegistrationStatus Status, string CountryCode)> rows;
        if (fuzzy)
        {
            // One pass over the facility's rows instead of a count and a page: the filter is the cost. Matches beyond the paging limit are not counted.
            var all = await shown.Take(PatientSearch.MaxPage * PatientSearch.PageSize).ToListAsync(cancellationToken);
            total = all.Count;
            rows = [.. all.Skip((search.Page - 1) * PatientSearch.PageSize).Take(PatientSearch.PageSize).Select(p => (p.Id, p.FullName, p.DateOfBirth, p.DateOfBirthEstimated, p.SexAtBirth, p.Mrn, p.Status, p.CountryCode))];
        }
        else
            rows = [.. (await shown.Skip((search.Page - 1) * PatientSearch.PageSize).Take(PatientSearch.PageSize).ToListAsync(cancellationToken))
                .Select(p => (p.Id, p.FullName, p.DateOfBirth, p.DateOfBirthEstimated, p.SexAtBirth, p.Mrn, p.Status, p.CountryCode))];
        var items = rows.Select(p => new PatientSearchRow(p.Id, Initials(p.FullName), CountryRules.AgeOn(CountryRules.For(p.CountryCode).Today(now), p.DateOfBirth),
            p.DateOfBirthEstimated, p.SexAtBirth, Tail(p.Mrn), p.Status)).ToList();
        return new(term.Kind, items, search.Page, PatientSearch.PageSize, total);
    }

    // Why no trigram index: row-level security keeps pg_trgm's operators (LIKE, %, <%) out of every index, because they are not "leakproof"
    // and only a superuser can mark a function so. The tenant and facility index narrows to one facility, and these filters run on its rows;
    // docs/03-architecture/patient-search.md has the measurements and the ceiling.
    const string SearchKey = PatientsDbContext.SearchKeyColumn;
    const int FuzzyBelow = 3;
    const int MaxTokens = 5;
    const int MaxTokenLength = 30;
    const double WordSimilarity = 0.6;

    /// <summary>
    /// The query folded the way the stored search key is (the same SQL function, so spellings meet: Mohamed, Muhammad), as letter-and-digit words.
    /// Anything else is dropped, so a word can never carry a LIKE wildcard or a regex operator into a pattern.
    /// </summary>
    async Task<IReadOnlyList<string>> NameTokensAsync(string text, CancellationToken cancellationToken)
    {
        var folded = (await db.Database.SqlQuery<string>($"""SELECT patients.fold_name({text}) AS "Value" """).ToListAsync(cancellationToken)).Single();
        return [.. folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(c => char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark)]))
            .Where(w => w.Length > 0).Select(w => w.Length > MaxTokenLength ? w[..MaxTokenLength] : w).Take(MaxTokens)];
    }

    /// <summary>Every word occurs in the search key; with <paramref name="swaps"/> (the fuzzy pass), a word may instead be near one (trigram word similarity) or have two adjacent letters swapped.</summary>
    static IQueryable<Patient> Contains(IQueryable<Patient> patients, IReadOnlyList<string> tokens, IReadOnlyDictionary<string, string?>? swaps = null)
    {
        foreach (var t in tokens)
        {
            var like = "%" + t + "%";
            var swapped = swaps?.GetValueOrDefault(t);
            patients = swaps is null
                ? patients.Where(p => EF.Functions.Like(EF.Property<string>(p, SearchKey), like))
                : swapped is null
                    ? patients.Where(p => EF.Functions.Like(EF.Property<string>(p, SearchKey), like) || EF.Functions.TrigramsWordSimilarity(t, EF.Property<string>(p, SearchKey)) >= WordSimilarity)
                    : patients.Where(p => EF.Functions.Like(EF.Property<string>(p, SearchKey), like) || EF.Functions.TrigramsWordSimilarity(t, EF.Property<string>(p, SearchKey)) >= WordSimilarity
                        || System.Text.RegularExpressions.Regex.IsMatch(EF.Property<string>(p, SearchKey), swapped));
        }
        return patients;
    }

    /// <summary>
    /// Per word of 4 characters or more, an alternation of the word with each adjacent pair swapped (krsihnan finds krishnan), each variant folded by the
    /// same SQL function as the stored key; null for a shorter word. Words are letters and digits only, so the alternation holds no regex operator.
    /// </summary>
    async Task<IReadOnlyDictionary<string, string?>> SwappedPatternsAsync(IReadOnlyList<string> tokens, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string?>();
        foreach (var t in tokens.Distinct())
        {
            var variants = t.Length < 4 ? [] : Enumerable.Range(0, t.Length - 1).Where(i => t[i] != t[i + 1]).Select(i => string.Concat(t.AsSpan(0, i), t.AsSpan(i + 1, 1), t.AsSpan(i, 1), t.AsSpan(i + 2))).Distinct().ToArray();
            result[t] = variants.Length == 0 ? null : string.Join('|', await db.Database.SqlQuery<string>($"""SELECT patients.fold_name(v) AS "Value" FROM unnest({variants}) AS v""").ToListAsync(cancellationToken));
        }
        return result;
    }

    /// <summary>First letters of the first and last words (words that start with a letter, so "(synthetic)" is skipped).</summary>
    internal static string Initials(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetter(w[0])).ToList();
        var initials = words.Count switch { 0 => "", 1 => words[0][..1], _ => $"{words[0][0]}{words[^1][0]}" };
        return initials.ToUpperInvariant();
    }

    IQueryable<Patient> FacilityPatients() => FacilityPatients(FacilityId());

    Guid FacilityId() => facility.FacilityId ?? throw new InvalidOperationException("No facility context.");

    IQueryable<Patient> FacilityPatients(Guid facilityId) => db.Patients.AsNoTracking().Where(p => p.FacilityId == facilityId && p.MergedIntoPatientId == null);

    /// <summary>At most this many possible matches are shown: enough to decide, too few to browse the register through this door.</summary>
    const int MaxCandidates = 5;

    public Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(DuplicateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var facilityId = FacilityId();
        return DuplicatesAsync(facilityId, query.FullName, query.DateOfBirth, CountryRules.For(query.CountryCode).MatchKeys(query.Identifiers), cancellationToken);
    }

    /// <summary>Exact identifier matches, then a similar name with the identical recorded date of birth. Phone is never read.</summary>
    async Task<IReadOnlyList<DuplicateCandidate>> DuplicatesAsync(Guid facilityId, string fullName, DateOnly? dateOfBirth, IReadOnlyList<IdentifierInput> keys, CancellationToken cancellationToken)
    {
        var byIdentifier = new Dictionary<Guid, List<IdentifierScheme>>();
        foreach (var key in keys)
        {
            // Blind index equality (MDV-P0-035), never a decrypting scan.
            var index = db.LookupIndexFor(key.Value);
            var ids = await db.Identifiers.Where(i => i.Scheme == key.Scheme && EF.Property<byte[]>(i, NationalIdColumns.LookupIndex) == index).Select(i => i.PatientId).ToListAsync(cancellationToken);
            foreach (var id in ids) (byIdentifier.TryGetValue(id, out var schemes) ? schemes : byIdentifier[id] = []).Add(key.Scheme);
        }

        var byName = new HashSet<Guid>();
        if (dateOfBirth is { } dob)
        {
            // An estimated date of birth (from a stated age) is not a date of birth: it never counts as "identical".
            var sameDay = await FacilityPatients(facilityId).Where(p => p.DateOfBirth == dob && !p.DateOfBirthEstimated)
                .Select(p => new { p.Id, p.FullName }).ToListAsync(cancellationToken);
            byName = [.. sameDay.Where(p => NameMatch.Similar(fullName, p.FullName)).Select(p => p.Id)];
        }

        var matched = byIdentifier.Keys.Union(byName).ToList();
        if (matched.Count == 0) return [];
        var now = clock.GetUtcNow();
        var rows = await FacilityPatients(facilityId).Where(p => matched.Contains(p.Id)).OrderByDescending(p => p.RegisteredAt).Take(MaxCandidates)
            .Select(p => new { p.Id, p.FullName, p.DateOfBirth, p.SexAtBirth, p.Mrn, p.CountryCode }).ToListAsync(cancellationToken);
        var shown = rows.Select(r => r.Id).ToList();
        var identifiers = await db.Identifiers.AsNoTracking().Where(i => shown.Contains(i.PatientId)).ToListAsync(cancellationToken);
        return [.. rows.Select(p => new DuplicateCandidate(
            p.Id, Initials(p.FullName), CountryRules.AgeOn(CountryRules.For(p.CountryCode).Today(now), p.DateOfBirth), p.SexAtBirth, Tail(p.Mrn),
            byName.Contains(p.Id), byIdentifier.TryGetValue(p.Id, out var s) ? s : [],
            [.. identifiers.Where(i => i.PatientId == p.Id).OrderBy(i => i.Scheme).Select(i => new MaskedIdentifier(i.Scheme, Tail(i.LookupValue ?? i.DisplayValue ?? "")))]))];
    }

    static string Tail(string value) => value.Length <= 4 ? value : value[^4..];

    public async Task<RegistrationStatus?> CompleteRegistrationAsync(Guid patientId, PatientAddress? address, PayerInput? payer, ContactInput? emergencyContact = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        var facilityId = FacilityId();
        // Facility-scoped like every read here: another facility's patient is not found and nothing is changed.
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId && p.FacilityId == facilityId, cancellationToken);
        if (patient is null) return null;
        if (address is null && payer is null && emergencyContact is null) throw new PatientValidationException([new("completion", "required")]);
        var (validAddress, validPayer, emergency) = CountryRules.For(patient.CountryCode).ValidateCompletion(address, payer, emergencyContact);
        (address, payer) = (validAddress, validPayer);
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (address is not null) patient.Address = address;
        var current = await db.PayerCoverages.SingleOrDefaultAsync(p => p.PatientId == patientId, cancellationToken);
        if (payer is not null)
        {
            if (current is not null) db.PayerCoverages.Remove(current);
            db.PayerCoverages.Add(Coverage(tenantId, patientId, payer));
        }
        var hasEmergencyContact = await db.Relationships.AnyAsync(r => r.PatientId == patientId && r.IsEmergencyContact, cancellationToken);
        if (emergency is not null)
        {
            db.Relationships.RemoveRange(await db.Relationships.Where(r => r.PatientId == patientId && r.IsEmergencyContact).ToListAsync(cancellationToken));
            db.Relationships.Add(Relationship(tenantId, patientId, new(emergency.Name, emergency.Relationship, emergency.Phone), isEmergencyContact: true));
        }
        patient.Status = StatusFor(patient.Address, payer is not null || current is not null, emergency is not null || hasEmergencyContact);
        patient.ChangedBy = user.UserId;
        patient.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync(tx, "patient.update", patientId, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return patient.Status;
    }

    /// <summary>The desk's completion task is open while the address, the payer or the emergency contact is missing.</summary>
    /// <remarks>
    /// shortcut: the desk's completion task is this status (board card, record header, directory "Incomplete" filter and the
    /// record's "Complete registration" form); a CARE_TASK queue with owner and due date replaces it with care tasks (MDV-P4-016).
    /// </remarks>
    static RegistrationStatus StatusFor(PatientAddress? address, bool hasPayer, bool hasEmergencyContact) =>
        address is not null && hasPayer && hasEmergencyContact ? RegistrationStatus.Complete : RegistrationStatus.Incomplete;

    static PatientRelationship Relationship(Guid tenantId, Guid patientId, ContactInput c, bool isEmergencyContact) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patientId, Name = c.Name!, Relationship = c.Relationship!.Value, Phone = c.Phone!,
        IsEmergencyContact = isEmergencyContact, IsGuardian = !isEmergencyContact,
    };

    /// <summary>
    /// The registration day on the facility's calendar (its IANA time zone): ages, the under-18 guardian rule and date checks
    /// use it, so a patient turning 18 today in Kochi is an adult even while it is still yesterday in UTC.
    /// </summary>
    async Task<DateOnly> FacilityTodayAsync(Guid facilityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var site = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Unknown facility.");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(site.TimeZone)).DateTime);
    }

    static PayerCoverage Coverage(Guid tenantId, Guid patientId, PayerInput p) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patientId, Kind = p.Kind, PayerName = p.PayerName, Plan = p.Plan,
        MemberNumber = p.MemberNumber, ValidTo = p.ValidTo, Network = p.Network,
    };

    async Task<PatientRecord?> LoadAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var patient = await FacilityPatients().SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient is null) return null;
        var identifiers = await db.Identifiers.AsNoTracking().Where(i => i.PatientId == patientId).ToListAsync(cancellationToken);
        var payer = await db.PayerCoverages.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId, cancellationToken);
        var relationships = await db.Relationships.AsNoTracking().Where(r => r.PatientId == patientId).OrderBy(r => r.IsGuardian).ToListAsync(cancellationToken);
        return new(patient, identifiers, payer, relationships);
    }

    /// <summary>Next facility-scoped MRN, allocated inside the caller's transaction (the upsert's row lock serialises concurrent desks).</summary>
    async Task<string> NextMrnAsync(Guid tenantId, Guid facilityId, CancellationToken cancellationToken)
    {
        var next = (await db.Database.SqlQuery<int>($"""
            INSERT INTO patients."MrnCounters" ("TenantId", "FacilityId", "LastNumber") VALUES ({tenantId}, {facilityId}, 1)
            ON CONFLICT ("TenantId", "FacilityId") DO UPDATE SET "LastNumber" = patients."MrnCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken)).Single();
        return Mrn.Format(next);
    }

    // In the write's own transaction: the event commits or rolls back with the change it records.
    Task AuditAsync(IDbContextTransaction tx, string action, Guid? patientId, DateTimeOffset now, CancellationToken cancellationToken, string resourceType = "patient", Guid? resourceId = null) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), ActorUserId = user.UserId, Action = action, ResourceType = resourceType,
            ResourceId = resourceId ?? patientId, PatientId = patientId, Purpose = Purpose, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);

    Guid TenantId() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}
