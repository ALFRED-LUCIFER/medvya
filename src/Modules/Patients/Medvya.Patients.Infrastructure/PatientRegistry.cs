using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Patient registration, directory and search. Each write runs in one database transaction that commits only after its audit event
/// is written (fail closed). Reads do not audit here: the calling endpoint's <c>.Audited()</c> filter writes one event before the
/// read runs, so a read that cannot be audited never happens and each request leaves exactly one event.
/// shortcut: the audit event is written on its own connection, so a commit that fails after the audit write leaves an audit row
/// for a change that did not land (as FeatureFlags); share the transaction when an outbox exists.
/// shortcut: registration still trusts the command's facility id; it moves to the active facility with the registration endpoint (MDV-P1-016).
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
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync("patient.register", patient.Id, now, cancellationToken); // PatientRegistered.v1: opaque ids only
        await tx.CommitAsync(cancellationToken);
        return new(patient.Id, patient.Mrn, now, user.UserId, patient.Status, warnings);
    }

    public Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default) => LoadAsync(patientId, cancellationToken);

    public async Task<PatientSearchResult> SearchAsync(PatientSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Page is < 1 or > 1000) throw new PatientValidationException([new("page", "out_of_range")]);
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
            p.DateOfBirthEstimated, p.SexAtBirth, p.Mrn.Length <= 4 ? p.Mrn : p.Mrn[^4..], p.Status)).ToList();
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

    IQueryable<Patient> FacilityPatients()
    {
        var facilityId = facility.FacilityId ?? throw new InvalidOperationException("No facility context.");
        return db.Patients.AsNoTracking().Where(p => p.FacilityId == facilityId);
    }

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

        await AuditAsync("patient.update", patientId, now, cancellationToken);
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

    Task AuditAsync(string action, Guid? patientId, DateTimeOffset now, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), ActorUserId = user.UserId, Action = action, ResourceType = "patient",
            ResourceId = patientId, PatientId = patientId, Purpose = Purpose, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, cancellationToken);

    Guid TenantId() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}
