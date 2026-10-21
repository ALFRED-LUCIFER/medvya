using System.Globalization;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Patient registration and lookup. Each write runs in one database transaction that commits only after its audit event is
/// written, and each read is audited before data is returned, so nothing happens unaudited (fail closed).
/// shortcut: the audit event is written on its own connection, so a commit that fails after the audit write leaves an audit row
/// for a change that did not land (as FeatureFlags); share the transaction when an outbox exists.
/// shortcut: the facility id is trusted from the caller; check it against the user's membership when endpoints exist (MDV-P0-011).
/// </summary>
public sealed class PatientRegistry(PatientsDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IPatientRegistry
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

    public async Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var record = await LoadAsync(patientId, cancellationToken);
        if (record is not null) await AuditAsync("patient.read", patientId, clock.GetUtcNow(), cancellationToken);
        return record;
    }

    public async Task<PatientRecord?> FindByIdentifierAsync(string countryCode, IdentifierScheme scheme, string value, CancellationToken cancellationToken = default)
    {
        var normalized = CountryRules.For(countryCode).NormalizeLookup(scheme, value);
        // Deterministic encryption: SqlClient encrypts the parameter, SQL Server compares ciphertexts. Equality only.
        // shortcut: returns the first match; several records with one ID are duplicates for MDV-P1-016 to resolve.
        var patientId = await db.Identifiers
            .Where(i => i.Scheme == scheme && i.LookupValue == normalized)
            .Select(i => (Guid?)i.PatientId)
            .FirstOrDefaultAsync(cancellationToken);
        var record = patientId is { } id ? await LoadAsync(id, cancellationToken) : null;
        // Audited even when nothing matches: a national-ID lookup is itself a disclosure of interest.
        await AuditAsync("patient.lookup", record?.Patient.Id, clock.GetUtcNow(), cancellationToken);
        return record;
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
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient is null) return null;
        var identifiers = await db.Identifiers.AsNoTracking().Where(i => i.PatientId == patientId).ToListAsync(cancellationToken);
        var payer = await db.PayerCoverages.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId, cancellationToken);
        return new(patient, identifiers, payer);
    }

    /// <summary>Next facility-scoped MRN, allocated inside the caller's transaction (HOLDLOCK serialises concurrent desks).</summary>
    /// <remarks>shortcut: plain zero-padded number; add the facility prefix (e.g. DQ-11-) when facilities have codes.</remarks>
    async Task<string> NextMrnAsync(Guid tenantId, Guid facilityId, CancellationToken cancellationToken)
    {
        var next = (await db.Database.SqlQuery<int>($"""
            MERGE patients.MrnCounters WITH (HOLDLOCK) AS c
            USING (SELECT {tenantId} AS TenantId, {facilityId} AS FacilityId) AS s ON c.TenantId = s.TenantId AND c.FacilityId = s.FacilityId
            WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
            WHEN NOT MATCHED THEN INSERT (TenantId, FacilityId, LastNumber) VALUES (s.TenantId, s.FacilityId, 1)
            OUTPUT inserted.LastNumber AS Value;
            """).ToListAsync(cancellationToken)).Single();
        return next.ToString("D6", CultureInfo.InvariantCulture);
    }

    Task AuditAsync(string action, Guid? patientId, DateTimeOffset now, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), ActorUserId = user.UserId, Action = action, ResourceType = "patient",
            ResourceId = patientId, PatientId = patientId, Purpose = Purpose, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, cancellationToken);

    Guid TenantId() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}
