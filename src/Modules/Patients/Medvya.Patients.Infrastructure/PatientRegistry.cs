using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Patient registration, directory and search. Each write and its audit events run in one database transaction (fail closed): the
/// events commit with the change, and a failed audit write rolls the change back. Reads do not audit here: the calling endpoint's
/// <c>.Audited()</c> filter writes one event before the answer is sent, so each request leaves exactly one event.
/// The facility of a registration is the command's; the HTTP endpoint fills it from the active facility, never from the request body.
/// shortcut: reads see the active facility's patients only; sharing patients across one tenant's facilities waits for multi-site rules (P7).
/// </summary>
public sealed class PatientRegistry(PatientsDbContext db, ICurrentTenant tenant, ICurrentFacility facility, ICurrentUser user, IAuditLog audit, TimeProvider clock)
    : IPatientRegistry
{
    const string Purpose = "registration";

    public async Task<RegistrationResult> RegisterAsync(RegisterPatient command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = TenantId();
        var now = clock.GetUtcNow();
        var rules = CountryRules.For(command.CountryCode);
        var (c, dob, estimated, warnings) = rules.Validate(command, rules.Today(now));

        // Checked here as well as while typing: the save is refused until the desk has decided about every possible match.
        var duplicates = await DuplicatesAsync(c.FacilityId, c.FullName, estimated ? null : dob, rules.MatchKeys(c.Identifiers), cancellationToken);
        var resolved = c.Duplicates?.CandidateIds ?? [];
        if (duplicates.Any(d => !resolved.Contains(d.PatientId))) throw new PatientValidationException([new("duplicates", "unresolved")]);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var patient = new Patient
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = c.FacilityId, CountryCode = c.CountryCode,
            Mrn = await NextMrnAsync(tenantId, c.FacilityId, cancellationToken),
            FullName = c.FullName, ArabicName = c.ArabicName, DateOfBirth = dob, DateOfBirthEstimated = estimated, SexAtBirth = c.SexAtBirth,
            GenderIdentity = c.GenderIdentity, PreferredLanguage = c.PreferredLanguage, Nationality = c.Nationality,
            Mobile = c.Mobile, AlternatePhone = c.AlternatePhone, Email = c.Email, Address = c.Address,
            Status = StatusFor(c.Address, c.Payer is not null),
            RegisteredBy = user.UserId, RegisteredAt = now, ChangedBy = user.UserId, ChangedAt = now,
        };
        db.Patients.Add(patient);
        db.Identifiers.AddRange(c.Identifiers.Select(i => new PatientIdentifier
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patient.Id, Scheme = i.Scheme, ExpiresOn = i.ExpiresOn,
            LookupValue = PatientIdentifier.IsLookup(i.Scheme) ? i.Value : null,
            DisplayValue = PatientIdentifier.IsLookup(i.Scheme) ? null : i.Value,
        }));
        if (c.Payer is not null) db.PayerCoverages.Add(Coverage(tenantId, patient.Id, c.Payer));
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

    public Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default) => LoadAsync(patientId, cancellationToken);

    public async Task<PatientSearchResult> SearchAsync(PatientSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Page is < 1 or > PatientSearch.MaxPage) throw new PatientValidationException([new("page", "out_of_range")]);
        var term = CountryRules.For(search.CountryCode).ParseSearch(search.Query);
        var now = clock.GetUtcNow();

        var patients = FacilityPatients();
        var like = term.Kind == SearchKind.Name ? LikeContaining(term.Value!) : null;
        patients = term.Kind switch
        {
            SearchKind.Name => patients.Where(p => EF.Functions.Like(p.FullName, like!, Escape) || (p.ArabicName != null && EF.Functions.Like(p.ArabicName, like!, Escape))),
            SearchKind.Phone => patients.Where(p => p.Mobile == term.Value || p.AlternatePhone == term.Value),
            SearchKind.Mrn => patients.Where(p => p.Mrn == term.Value),
            // Deterministic Always Encrypted: SqlClient encrypts the parameter and SQL Server compares ciphertexts (equality only).
            SearchKind.Identifier => patients.Where(p => db.Identifiers.Any(i => i.PatientId == p.Id && i.Scheme == term.Scheme && i.LookupValue == term.Value)),
            _ => patients,
        };
        patients = search.Filter switch
        {
            DirectoryFilter.Incomplete => patients.Where(p => p.Status == RegistrationStatus.Incomplete),
            DirectoryFilter.Recent => patients.Where(p => p.RegisteredAt >= now.AddDays(-PatientSearch.RecentDays)),
            _ => patients,
        };

        var total = await patients.CountAsync(cancellationToken);
        var ordered = term.Kind == SearchKind.Name ? patients.OrderBy(p => p.FullName).ThenBy(p => p.Id) : patients.OrderByDescending(p => p.RegisteredAt).ThenBy(p => p.Id);
        var rows = await ordered.Skip((search.Page - 1) * PatientSearch.PageSize).Take(PatientSearch.PageSize)
            .Select(p => new { p.Id, p.FullName, p.DateOfBirth, p.DateOfBirthEstimated, p.SexAtBirth, p.Mrn, p.Status, p.CountryCode })
            .ToListAsync(cancellationToken);
        var items = rows.Select(p => new PatientSearchRow(p.Id, Initials(p.FullName), CountryRules.AgeOn(CountryRules.For(p.CountryCode).Today(now), p.DateOfBirth),
            p.DateOfBirthEstimated, p.SexAtBirth, Tail(p.Mrn), p.Status)).ToList();
        return new(term.Kind, items, search.Page, PatientSearch.PageSize, total);
    }

    const string Escape = "\\";
    static string LikeContaining(string text) => "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal) + "%";

    /// <summary>First letters of the first and last words (words that start with a letter, so "(synthetic)" is skipped).</summary>
    internal static string Initials(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetter(w[0])).ToList();
        var initials = words.Count switch { 0 => "", 1 => words[0][..1], _ => $"{words[0][0]}{words[^1][0]}" };
        return initials.ToUpperInvariant();
    }

    IQueryable<Patient> FacilityPatients() => FacilityPatients(facility.FacilityId ?? throw new InvalidOperationException("No facility context."));

    IQueryable<Patient> FacilityPatients(Guid facilityId) => db.Patients.AsNoTracking().Where(p => p.FacilityId == facilityId);

    /// <summary>At most this many possible matches are shown: enough to decide, too few to browse the register through this door.</summary>
    const int MaxCandidates = 5;

    public Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(DuplicateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var facilityId = facility.FacilityId ?? throw new InvalidOperationException("No facility context.");
        return DuplicatesAsync(facilityId, query.FullName, query.DateOfBirth, CountryRules.For(query.CountryCode).MatchKeys(query.Identifiers), cancellationToken);
    }

    /// <summary>Exact identifier matches, then a similar name with the identical recorded date of birth. Phone is never read.</summary>
    async Task<IReadOnlyList<DuplicateCandidate>> DuplicatesAsync(Guid facilityId, string fullName, DateOnly? dateOfBirth, IReadOnlyList<IdentifierInput> keys, CancellationToken cancellationToken)
    {
        var byIdentifier = new Dictionary<Guid, List<IdentifierScheme>>();
        foreach (var key in keys)
        {
            // Deterministic Always Encrypted: equality on ciphertext, never a decrypting scan.
            var ids = await db.Identifiers.Where(i => i.Scheme == key.Scheme && i.LookupValue == key.Value).Select(i => i.PatientId).ToListAsync(cancellationToken);
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

    public async Task<RegistrationStatus> CompleteRegistrationAsync(Guid patientId, PatientAddress? address, PayerInput? payer, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId();
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken)
            ?? throw new KeyNotFoundException("Patient not found.");
        (address, payer) = CountryRules.For(patient.CountryCode).ValidateCompletion(address, payer);
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (address is not null) patient.Address = address;
        var current = await db.PayerCoverages.SingleOrDefaultAsync(p => p.PatientId == patientId, cancellationToken);
        if (payer is not null)
        {
            if (current is not null) db.PayerCoverages.Remove(current);
            db.PayerCoverages.Add(Coverage(tenantId, patientId, payer));
        }
        patient.Status = StatusFor(patient.Address, payer is not null || current is not null);
        patient.ChangedBy = user.UserId;
        patient.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync(tx, "patient.update", patientId, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return patient.Status;
    }

    /// <summary>The desk's completion task is open while address or payer is missing.</summary>
    /// <remarks>shortcut: the emergency contact (MDV-P1-020) joins this check when relationships exist; the task is this status until CARE_TASK exists.</remarks>
    static RegistrationStatus StatusFor(PatientAddress? address, bool hasPayer) =>
        address is not null && hasPayer ? RegistrationStatus.Complete : RegistrationStatus.Incomplete;

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
        return new(patient, identifiers, payer);
    }

    /// <summary>Next facility-scoped MRN, allocated inside the caller's transaction (HOLDLOCK serialises concurrent desks).</summary>
    async Task<string> NextMrnAsync(Guid tenantId, Guid facilityId, CancellationToken cancellationToken)
    {
        var next = (await db.Database.SqlQuery<int>($"""
            MERGE patients.MrnCounters WITH (HOLDLOCK) AS c
            USING (SELECT {tenantId} AS TenantId, {facilityId} AS FacilityId) AS s ON c.TenantId = s.TenantId AND c.FacilityId = s.FacilityId
            WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
            WHEN NOT MATCHED THEN INSERT (TenantId, FacilityId, LastNumber) VALUES (s.TenantId, s.FacilityId, 1)
            OUTPUT inserted.LastNumber AS Value;
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
