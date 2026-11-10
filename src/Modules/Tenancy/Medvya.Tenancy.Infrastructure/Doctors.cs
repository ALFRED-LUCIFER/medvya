using System.Net.Mail;
using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Doctors added in clinic mode (MDV-P1-007). Adding creates the Invited membership, profile, licence (attested by the owner, ADR-016) and invitation in
/// one transaction through <see cref="IInvitations"/>. The signature image goes to the same private container as logos under its own key.
/// </summary>
public sealed class Doctors(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IInvitations invitations, IAuditLog audit, ILogoStore store, TimeProvider clock)
    : IDoctors
{
    const long MaxFeeMinor = 100_000_000;

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public async Task<DoctorList> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await RulesAsync(cancellationToken);
        var defaults = await db.PracticeDefaultsSet.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new(await QueryAsync(null, rules.Currency, cancellationToken), new(defaults?.NewVisitFeeMinor ?? 0, defaults?.FollowUpFeeMinor ?? 0, rules.Currency), rules.Authorities);
    }

    public async Task<DoctorView?> AddAsync(AddDoctor command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rules = await RulesAsync(cancellationToken);
        var (name, email, specialty) = (Clean(command.FullName), Clean(command.Email), Clean(command.Specialty));
        var (qualifications, languages) = (Clean(command.Qualifications), Clean(command.Languages));
        var errors = new List<ValidationError>();
        Text(errors, "fullName", name, 200, true);
        Text(errors, "specialty", specialty, 100, true);
        Text(errors, "qualifications", qualifications, 200, false);
        Text(errors, "languages", languages, 200, false);
        // A bare address only, as the invitation requires (MDV-P0-013).
        if (email is null) errors.Add(new("email", "required"));
        else if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) errors.Add(new("email", "format"));
        Licence(errors, rules, command.Authority, command.LicenceNumber, command.Category, command.ExpiresOn, command.CertificateName);
        if (!command.Attested) errors.Add(new("attested", "required"));
        Fee(errors, "newVisitFee", command.NewVisitFee);
        Fee(errors, "followUpFee", command.FollowUpFee);
        SettingsValidationException.ThrowIfAny(errors);
        if (!await db.Facilities.AnyAsync(f => f.Id == command.FacilityId, cancellationToken)) return null;

        var now = clock.GetUtcNow();
        Guid id = default;
        // Doctors join as hospital doctors: the owner-doctor role carries practice-configuration rights (RoleMatrix), which an added doctor must not get.
        var created = await invitations.CreateAsync(email!, Role.HospitalDoctor, command.FacilityId, m =>
        {
            id = m.Id;
            db.DoctorProfiles.Add(new DoctorProfile
            {
                MembershipId = m.Id, TenantId = TenantId, FullName = name!, Specialty = specialty, Qualifications = qualifications, Languages = languages,
                NewVisitFeeMinor = command.NewVisitFee!.Value, FollowUpFeeMinor = command.FollowUpFee!.Value, ChangedBy = user.UserId, ChangedAt = now,
            });
            db.DoctorLicences.Add(new DoctorLicence
            {
                MembershipId = m.Id, TenantId = TenantId, Authority = command.Authority!, Number = command.LicenceNumber!.Trim(), Category = Clean(command.Category),
                ExpiresOn = command.ExpiresOn, CertificateName = Clean(command.CertificateName), SubmittedAt = now, AttestedBy = user.UserId, AttestedAt = now,
            });
            // The before/after row saves with the membership; the invitation's own audit event covers the same transaction.
            db.SettingsChanges.Add(new SettingsChange
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Section = "doctor", ResourceId = m.Id, OccurredAt = now,
                After = $$"""{"facilityId":"{{command.FacilityId}}","authority":"{{command.Authority}}","number":"{{command.LicenceNumber!.Trim()}}","expiresOn":"{{command.ExpiresOn:yyyy-MM-dd}}","licence":"Attested"}""",
            });
        }, command.AlsoFacilityIds, cancellationToken);
        return created is null ? null : (await QueryAsync(id, rules.Currency, cancellationToken))[0];
    }

    public async Task<DoctorView?> ResubmitLicenceAsync(Guid doctorId, ResubmitLicence command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var licence = await db.DoctorLicences.SingleOrDefaultAsync(l => l.MembershipId == doctorId, cancellationToken);
        if (licence is null) return null;
        var rules = await RulesAsync(cancellationToken);
        var errors = new List<ValidationError>();
        Licence(errors, rules, command.Authority, command.Number, command.Category, command.ExpiresOn, command.CertificateName);
        if (!command.Attested) errors.Add(new("attested", "required"));
        SettingsValidationException.ThrowIfAny(errors);

        var before = new { authority = licence.Authority, number = licence.Number, expiresOn = licence.ExpiresOn };
        (licence.Authority, licence.Number, licence.Category, licence.ExpiresOn, licence.CertificateName) =
            (command.Authority!, command.Number!.Trim(), Clean(command.Category), command.ExpiresOn, Clean(command.CertificateName));
        (licence.SubmittedAt, licence.AttestedBy, licence.AttestedAt) = (clock.GetUtcNow(), user.UserId, clock.GetUtcNow());
        await SettingsAudit.SaveAsync(db, audit, TenantId, user.UserId, licence.SubmittedAt, "settings.doctor.licence.update", "doctor", doctorId,
            before, new { authority = licence.Authority, number = licence.Number, expiresOn = licence.ExpiresOn, licence = "Attested" }, cancellationToken);
        return (await QueryAsync(doctorId, rules.Currency, cancellationToken))[0];
    }

    public async Task<bool> SetSignatureAsync(Guid doctorId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var profile = await db.DoctorProfiles.SingleOrDefaultAsync(p => p.MembershipId == doctorId, cancellationToken);
        if (profile is null) return false;
        var type = LogoImage.ContentTypeOf(content);
        var errors = new List<ValidationError>();
        if (content.Length == 0 || content.Length > LogoImage.MaxBytes) errors.Add(new("signature", "too_large"));
        else if (type is null) errors.Add(new("signature", "type"));
        SettingsValidationException.ThrowIfAny(errors);
        content = CleanImage.ToPng(content) ?? throw new SettingsValidationException([new("signature", "type")]); // only the pixels are kept (#211)
        type = "image/png";

        var old = profile.SignatureKey;
        profile.SignatureKey = $"{TenantId}/signature/{Guid.NewGuid()}"; // random, no names in blob paths
        (profile.ChangedBy, profile.ChangedAt) = (user.UserId, clock.GetUtcNow());
        await store.PutAsync(profile.SignatureKey, content, type!, cancellationToken);
        await SettingsAudit.SaveAsync(db, audit, TenantId, user.UserId, profile.ChangedAt, "settings.doctor.signature.update", "doctor", doctorId,
            new { signature = old is not null }, new { signature = true, type, bytes = content.Length }, cancellationToken);
        if (old is not null) await store.DeleteAsync(old, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveSignatureAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profile = await db.DoctorProfiles.SingleOrDefaultAsync(p => p.MembershipId == doctorId, cancellationToken);
        if (profile?.SignatureKey is not { } key) return false;
        profile.SignatureKey = null;
        (profile.ChangedBy, profile.ChangedAt) = (user.UserId, clock.GetUtcNow());
        await SettingsAudit.SaveAsync(db, audit, TenantId, user.UserId, profile.ChangedAt, "settings.doctor.signature.delete", "doctor", doctorId,
            new { signature = true }, new { signature = false }, cancellationToken);
        await store.DeleteAsync(key, cancellationToken);
        return true;
    }

    async Task<OnboardingRules> RulesAsync(CancellationToken ct) =>
        OnboardingRules.For(await db.Tenants.Select(t => t.CountryCode).SingleAsync(ct)) ?? throw new InvalidOperationException("Unsupported country.");

    async Task<List<DoctorView>> QueryAsync(Guid? id, string currency, CancellationToken ct)
    {
        var rows = await (
            from l in db.DoctorLicences.AsNoTracking()
            join m in db.Memberships.AsNoTracking() on l.MembershipId equals m.Id
            join f in db.Facilities.AsNoTracking() on m.FacilityId equals f.Id
            join p in db.DoctorProfiles.AsNoTracking() on m.Id equals p.MembershipId
            where id == null || m.Id == id
            orderby p.FullName
            // Who attested, by the name of their own doctor profile; an owner who is not a doctor has none and the screen says "the owner".
            let attester = (from am in db.Memberships join ap in db.DoctorProfiles on am.Id equals ap.MembershipId where am.UserId == l.AttestedBy select ap.FullName).FirstOrDefault()
            select new { m.Id, p, m.FacilityId, Facility = f.Name, m.Status, Licence = l, attester }).ToListAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return [.. rows.Select(r => new DoctorView(
            r.Id, r.p.FullName, r.p.Specialty, r.p.Qualifications, r.p.Languages, r.FacilityId, r.Facility, r.Status,
            r.Licence.Authority, r.Licence.Number, r.Licence.Category, r.Licence.ExpiresOn, LicenceRules.IsExpired(r.Licence, today), r.Licence.AttestedBy, r.attester, r.Licence.AttestedAt,
            r.p.SignatureKey is not null, r.p.NewVisitFeeMinor, r.p.FollowUpFeeMinor, currency))];
    }

    void Licence(List<ValidationError> errors, OnboardingRules rules, string? authority, string? number, string? category, DateOnly? expiresOn, string? certificate) =>
        errors.AddRange(rules.ValidateLicence(authority, number, category, expiresOn, certificate, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            .Select(e => new ValidationError(e.Field, e.Code)));

    static void Fee(List<ValidationError> errors, string field, long? minor)
    {
        if (minor is null) errors.Add(new(field, "required"));
        else if (minor is < 0 or > MaxFeeMinor) errors.Add(new(field, "range"));
    }

    static void Text(List<ValidationError> errors, string field, string? value, int max, bool required)
    {
        if (value is null) { if (required) errors.Add(new(field, "required")); }
        else if (value.Length > max) errors.Add(new(field, "too_long"));
    }

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
