using System.Net.Mail;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Practice settings (MDV-P1-005). Every change saves, records its before/after (SettingsChange) and writes its audit event in one
/// transaction (<c>settings.*</c>, resource <c>settings</c>, purpose operations), so an unaudited change never commits.
/// shortcut: the logo is checked by type (file signature) and size only; no malware scanner exists in the repo yet, so wire
/// Defender for Storage / a scan step before the logo is served, with MDV-P0-006 (Azure environments).
/// </summary>
public sealed class PracticeSettings(
    TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility currentFacility, IFacilityDirectory facilities,
    IAuditLog audit, ILogoStore logos, TimeProvider clock) : IPracticeSettings
{
    const string Resource = "settings", Purpose = "operations";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public async Task<ProfileView> GetProfileAsync(CancellationToken cancellationToken = default) =>
        ViewOf(await db.Tenants.AsNoTracking().SingleAsync(cancellationToken), await db.Profiles.AsNoTracking().SingleOrDefaultAsync(cancellationToken));

    public async Task<ProfileView> SaveProfileAsync(SaveProfile command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = Clean(command.Name);
        var errors = new List<ValidationError>();
        if (name is null) errors.Add(new("name", "required"));
        else if (name.Length > 200) errors.Add(new("name", "too_long"));
        var (address, registration, taxNumber, phone, email) = (Clean(command.Address), Clean(command.RegistrationNumber), Clean(command.TaxNumber), Clean(command.Phone), Clean(command.Email));
        Max(errors, "address", address, 500);
        Max(errors, "registrationNumber", registration, 50);
        Max(errors, "taxNumber", taxNumber, 30);
        if (phone is not null && !PhoneFormat(phone)) errors.Add(new("phone", "invalid"));
        if (email is not null && !(email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email)) errors.Add(new("email", "invalid"));
        SettingsValidationException.ThrowIfAny(errors);

        var clinic = await db.Tenants.SingleAsync(cancellationToken);
        var profile = await db.Profiles.SingleOrDefaultAsync(cancellationToken);
        var before = ViewOf(clinic, profile);
        if (profile is null) db.Profiles.Add(profile = new PracticeProfile { Id = Guid.CreateVersion7(), TenantId = TenantId });
        (clinic.Name, profile.Address, profile.RegistrationNumber, profile.TaxNumber, profile.Phone, profile.Email) = (name!, address, registration, taxNumber, phone, email);
        (profile.ChangedBy, profile.ChangedAt) = (user.UserId, clock.GetUtcNow());
        var after = ViewOf(clinic, profile);
        await SaveAuditedAsync("settings.profile.update", "profile", profile.Id, before, after, cancellationToken);
        return after;
    }

    public async Task<IReadOnlyList<LocationView>> ListLocationsAsync(CancellationToken cancellationToken = default) =>
        await db.Facilities.AsNoTracking().OrderBy(f => f.Name).Select(f => new LocationView(f.Id, f.Name, f.Address, f.TimeZone)).ToListAsync(cancellationToken);

    public async Task<LocationView> AddLocationAsync(SaveLocation command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (name, address, zone) = ValidLocation(command, requireZone: true);
        var site = new Facility { Id = Guid.CreateVersion7(), TenantId = TenantId, Name = name, Address = address, TimeZone = zone! };
        db.Facilities.Add(site);
        // The creator works at the new location with the role they hold at the current one (a membership is what grants access there).
        if (user.UserId is { } userId && currentFacility.FacilityId is { } here && await facilities.ActiveRoleAsync(userId, here, cancellationToken) is { } role)
            db.Memberships.Add(new Membership { Id = Guid.CreateVersion7(), TenantId = TenantId, UserId = userId, FacilityId = site.Id, Role = role, Status = MembershipStatus.Active });
        var view = new LocationView(site.Id, site.Name, site.Address, site.TimeZone);
        await SaveAuditedAsync("settings.location.create", "location", site.Id, null, view, cancellationToken);
        return view;
    }

    public async Task<LocationView?> UpdateLocationAsync(Guid facilityId, SaveLocation command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var site = await db.Facilities.SingleOrDefaultAsync(f => f.Id == facilityId, cancellationToken);
        if (site is null) return null;
        var (name, address, zone) = ValidLocation(command, requireZone: false);
        var before = new LocationView(site.Id, site.Name, site.Address, site.TimeZone);
        (site.Name, site.Address, site.TimeZone) = (name, address, zone ?? site.TimeZone);
        var after = new LocationView(site.Id, site.Name, site.Address, site.TimeZone);
        await SaveAuditedAsync("settings.location.update", "location", site.Id, before, after, cancellationToken);
        return after;
    }

    public async Task<IReadOnlyList<DayHours>?> GetHoursAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        await db.Facilities.AnyAsync(f => f.Id == facilityId, cancellationToken) ? await HoursAsync(facilityId, cancellationToken) : null;

    public async Task<IReadOnlyList<DayHours>?> SetHoursAsync(Guid facilityId, IReadOnlyList<DayHours> hours, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hours);
        if (!await db.Facilities.AnyAsync(f => f.Id == facilityId, cancellationToken)) return null;
        var errors = new List<ValidationError>();
        for (var i = 0; i < hours.Count; i++)
        {
            var h = hours[i];
            if (h is null || !Enum.IsDefined(h.Weekday) || h.Opens >= h.Closes) errors.Add(new($"hours[{i}]", "invalid"));
            else if (hours.Take(i).Any(o => o.Weekday == h.Weekday)) errors.Add(new($"hours[{i}]", "duplicate"));
        }
        SettingsValidationException.ThrowIfAny(errors);

        var before = await HoursAsync(facilityId, cancellationToken);
        db.OperatingHours.RemoveRange(await db.OperatingHours.Where(h => h.FacilityId == facilityId).ToListAsync(cancellationToken));
        db.OperatingHours.AddRange(hours.Select(h => new OperatingHours
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = facilityId, Weekday = h.Weekday, Opens = h.Opens, Closes = h.Closes,
        }));
        var after = hours.OrderBy(h => h.Weekday).ToList();
        await SaveAuditedAsync("settings.hours.update", "hours", facilityId, before, after, cancellationToken);
        return after;
    }

    public async Task<IReadOnlyList<ClosureView>?> ListClosuresAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        await db.Facilities.AnyAsync(f => f.Id == facilityId, cancellationToken)
            ? await db.Closures.AsNoTracking().Where(c => c.FacilityId == facilityId).OrderBy(c => c.Date)
                .Select(c => new ClosureView(c.Id, c.FacilityId, c.Date, c.Name)).ToListAsync(cancellationToken)
            : null;

    public async Task<ClosureView?> AddClosureAsync(AddClosure command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await db.Facilities.AnyAsync(f => f.Id == command.FacilityId, cancellationToken)) return null;
        var name = Clean(command.Name);
        var errors = new List<ValidationError>();
        if (name is null) errors.Add(new("name", "required"));
        else if (name.Length > 100) errors.Add(new("name", "too_long"));
        if (command.Date == default) errors.Add(new("date", "required"));
        else if (await db.Closures.AnyAsync(c => c.FacilityId == command.FacilityId && c.Date == command.Date, cancellationToken)) errors.Add(new("date", "duplicate"));
        SettingsValidationException.ThrowIfAny(errors);

        var closure = new Closure { Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = command.FacilityId, Date = command.Date, Name = name! };
        db.Closures.Add(closure);
        var view = new ClosureView(closure.Id, closure.FacilityId, closure.Date, closure.Name);
        await SaveAuditedAsync("settings.closure.create", "closure", closure.Id, null, view, cancellationToken);
        return view;
    }

    public async Task<bool> RemoveClosureAsync(Guid closureId, CancellationToken cancellationToken = default)
    {
        var closure = await db.Closures.SingleOrDefaultAsync(c => c.Id == closureId, cancellationToken);
        if (closure is null) return false;
        db.Closures.Remove(closure);
        await SaveAuditedAsync("settings.closure.delete", "closure", closure.Id, new ClosureView(closure.Id, closure.FacilityId, closure.Date, closure.Name), null, cancellationToken);
        return true;
    }

    public async Task SetLogoAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var type = LogoImage.ContentTypeOf(content);
        var errors = new List<ValidationError>();
        if (content.Length == 0 || content.Length > LogoImage.MaxBytes) errors.Add(new("logo", "too_large"));
        else if (type is null) errors.Add(new("logo", "type"));
        SettingsValidationException.ThrowIfAny(errors);

        var profile = await db.Profiles.SingleOrDefaultAsync(cancellationToken);
        if (profile is null) db.Profiles.Add(profile = new PracticeProfile { Id = Guid.CreateVersion7(), TenantId = TenantId });
        var old = profile.LogoKey;
        profile.LogoKey = $"{TenantId}/logo/{Guid.NewGuid()}"; // random, no names in blob paths
        (profile.ChangedBy, profile.ChangedAt) = (user.UserId, clock.GetUtcNow());
        await logos.PutAsync(profile.LogoKey, content, type!, cancellationToken);
        await SaveAuditedAsync("settings.logo.update", "logo", profile.Id, new { logo = old is not null }, new { logo = true, type, bytes = content.Length }, cancellationToken);
        if (old is not null) await logos.DeleteAsync(old, cancellationToken);
    }

    public async Task<bool> RemoveLogoAsync(CancellationToken cancellationToken = default)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(cancellationToken);
        if (profile?.LogoKey is not { } key) return false;
        profile.LogoKey = null;
        (profile.ChangedBy, profile.ChangedAt) = (user.UserId, clock.GetUtcNow());
        await SaveAuditedAsync("settings.logo.delete", "logo", profile.Id, new { logo = true }, new { logo = false }, cancellationToken);
        await logos.DeleteAsync(key, cancellationToken);
        return true;
    }

    public async Task<Uri?> LogoLinkAsync(TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        await db.Profiles.AsNoTracking().Select(p => p.LogoKey).SingleOrDefaultAsync(cancellationToken) is { } key ? logos.LinkFor(key, lifetime) : null;

    async Task<List<DayHours>> HoursAsync(Guid facilityId, CancellationToken cancellationToken) =>
        await db.OperatingHours.AsNoTracking().Where(h => h.FacilityId == facilityId).OrderBy(h => h.Weekday)
            .Select(h => new DayHours(h.Weekday, h.Opens, h.Closes)).ToListAsync(cancellationToken);

    /// <summary>
    /// Saves the pending changes, the before/after and the audit event in one transaction (audit first-class: a failed audit write
    /// rolls the change back).
    /// </summary>
    async Task SaveAuditedAsync(string action, string section, Guid resourceId, object? before, object? after, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        db.SettingsChanges.Add(new SettingsChange
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Section = section, ResourceId = resourceId, OccurredAt = now,
            Before = before is null ? null : JsonSerializer.Serialize(before, Json), After = after is null ? null : JsonSerializer.Serialize(after, Json),
        });
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = resourceId,
            Purpose = Purpose, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    static (string Name, string? Address, string? Zone) ValidLocation(SaveLocation command, bool requireZone)
    {
        var (name, address, zone) = (Clean(command.Name), Clean(command.Address), Clean(command.TimeZone));
        var errors = new List<ValidationError>();
        if (name is null) errors.Add(new("name", "required"));
        else if (name.Length > 200) errors.Add(new("name", "too_long"));
        Max(errors, "address", address, 500);
        if (zone is null ? requireZone : !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) errors.Add(new("timeZone", zone is null ? "required" : "invalid"));
        SettingsValidationException.ThrowIfAny(errors);
        return (name!, address, zone);
    }

    static ProfileView ViewOf(Tenant clinic, PracticeProfile? p) =>
        new(clinic.Name, p?.Address, p?.RegistrationNumber, p?.TaxNumber, p?.Phone, p?.Email, p?.LogoKey is not null);

    static void Max(List<ValidationError> errors, string field, string? value, int max)
    {
        if (value?.Length > max) errors.Add(new(field, "too_long"));
    }

    static bool PhoneFormat(string phone) => phone.Length is >= 6 and <= 30 && phone.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')');

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
