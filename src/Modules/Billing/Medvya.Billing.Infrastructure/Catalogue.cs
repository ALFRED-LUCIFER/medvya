using System.Text.RegularExpressions;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// The services catalogue (MDV-P4-002). Validated here, never only in the screen; every change writes its audit event in the same transaction.
/// Prices are inserted, never changed: an issued invoice keeps the version it was priced from (MDV-P4-004).
/// </summary>
public sealed partial class Catalogue(BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff,
    IFacilityDirectory facilities, IAuditLog audit, TimeProvider clock) : ICatalogue
{
    public const string Resource = "service";

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public async Task<IReadOnlyList<ServiceView>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var services = await db.Services.AsNoTracking().Where(s => includeArchived || s.ArchivedAt == null)
            .OrderBy(s => s.ArchivedAt != null).ThenBy(s => s.Category).ThenBy(s => s.Name)
            .Select(s => new { s, Version = EF.Property<uint>(s, "xmin") }).ToListAsync(cancellationToken);
        var ids = services.Select(x => x.s.Id).ToList();
        var defaults = (await db.PriceVersions.AsNoTracking().Where(v => ids.Contains(v.ServiceId) && v.DoctorId == null).ToListAsync(cancellationToken)).ToLookup(v => v.ServiceId);
        var (currency, now) = (await CurrencyAsync(cancellationToken), clock.GetUtcNow());
        return [.. services.Select(x => View(x.s, x.Version, PriceVersion.Resolve(defaults[x.s.Id], null, now)?.AmountMinor, currency))];
    }

    public async Task<ServiceView> CreateAsync(SaveService command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var service = new Service { Id = Guid.CreateVersion7(), TenantId = TenantId, Name = "", CreatedBy = user.UserId, CreatedAt = now };
        await ApplyAsync(service, command, cancellationToken);
        await SaveAuditedAsync(() => db.Services.Add(service), "billing.service.create", service.Id, cancellationToken);
        return View(service, db.Entry(service).Property<uint>("xmin").CurrentValue, null, await CurrencyAsync(cancellationToken));
    }

    public async Task<ServiceView?> UpdateAsync(Guid id, SaveService command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Version is null) throw new BillingValidationException([("version", "required")]);
        if (await db.Services.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } service) return null;
        db.Entry(service).Property<uint>("xmin").OriginalValue = command.Version.Value; // refused at save if the row moved on (#889)
        await ApplyAsync(service, command, cancellationToken);
        await SaveAuditedAsync(() => { }, "billing.service.update", id, cancellationToken);
        return await ViewAsync(service, cancellationToken);
    }

    public async Task<ServiceView?> ArchiveAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        if (await db.Services.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } service) return null;
        if (archived == service.ArchivedAt is not null) return await ViewAsync(service, cancellationToken);
        if (!archived && service.ConsultationKind is { } kind && await KindTakenAsync(kind, id, cancellationToken))
            throw new BillingValidationException([("consultationKind", "taken")]);
        (service.ArchivedAt, service.ChangedBy, service.ChangedAt) = (archived ? clock.GetUtcNow() : null, user.UserId, clock.GetUtcNow());
        await SaveAuditedAsync(() => { }, archived ? "billing.service.archive" : "billing.service.unarchive", id, cancellationToken);
        return await ViewAsync(service, cancellationToken);
    }

    public async Task<IReadOnlyList<PriceVersionView>?> PricesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Services.AnyAsync(s => s.Id == id, cancellationToken)) return null;
        return await db.PriceVersions.AsNoTracking().Where(v => v.ServiceId == id).OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.CreatedAt)
            .Select(v => new PriceVersionView(v.Id, v.DoctorId, v.AmountMinor, v.Currency, v.EffectiveFrom, v.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<PriceVersionView?> AddPriceAsync(Guid id, AddPrice command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await db.Services.AnyAsync(s => s.Id == id, cancellationToken)) return null; // another tenant's service is not found (EF filter and row-level security)
        var errors = new List<(string, string)>();
        if (command.AmountMinor is null) errors.Add(("amountMinor", "required"));
        else if (command.AmountMinor is < 0 or > PriceVersion.MaxAmountMinor) errors.Add(("amountMinor", "range"));
        var now = clock.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById((facility.FacilityId is { } f ? await facilities.FindAsync(f, cancellationToken) : null)?.TimeZone
            ?? throw new InvalidOperationException("No facility context."));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (command.EffectiveOn < today) errors.Add(("effectiveOn", "past")); // a past start would reprice what was already billed
        BillingValidationException.ThrowIfAny(errors);

        // shortcut: DoctorId is not checked against Tenancy's doctors (the screen offers only the doctors list); an unknown id only adds a price nobody
        // resolves. Check it through a Tenancy contract when invoices take a doctor from the visit (MDV-P4-004).
        var effectiveFrom = command.EffectiveOn is { } on && on > today ? StartOfDay(on, zone) : now;
        var version = new PriceVersion
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ServiceId = id, DoctorId = command.DoctorId, AmountMinor = command.AmountMinor!.Value,
            Currency = await CurrencyAsync(cancellationToken), EffectiveFrom = effectiveFrom, CreatedBy = user.UserId, CreatedAt = now,
        };
        await SaveAuditedAsync(() => db.PriceVersions.Add(version), "billing.price.add", id, cancellationToken);
        return new(version.Id, version.DoctorId, version.AmountMinor, version.Currency, version.EffectiveFrom, version.CreatedAt);
    }

    public async Task<ResolvedPrice?> ResolvePriceAsync(Guid serviceId, Guid? doctorId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var versions = await db.PriceVersions.AsNoTracking()
            .Where(v => v.ServiceId == serviceId && (v.DoctorId == null || v.DoctorId == doctorId) && v.EffectiveFrom <= at).ToListAsync(cancellationToken);
        return PriceVersion.Resolve(versions, doctorId, at) is { } v ? new(v.Id, v.ServiceId, v.DoctorId, v.AmountMinor, v.Currency, v.EffectiveFrom) : null;
    }

    static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    async Task ApplyAsync(Service service, SaveService c, CancellationToken cancellationToken)
    {
        var errors = new List<(string, string)>();
        var name = Clean(c.Name);
        var code = Clean(c.Code);
        var sac = Clean(c.Sac);
        if (name is null) errors.Add(("name", "required"));
        else if (name.Length > Service.MaxName) errors.Add(("name", "too_long"));
        if (c.Category is not { } category || !Enum.IsDefined(category)) errors.Add(("category", "required"));
        if (code?.Length > Service.MaxCode) errors.Add(("code", "too_long"));
        if (sac is not null && !Sac().IsMatch(sac)) errors.Add(("sac", "format"));
        var tax = c.TaxTreatment ?? TaxTreatment.Exempt;
        if (!Enum.IsDefined(tax)) errors.Add(("taxTreatment", "invalid"));
        // The rate only means something for a taxable item; an exempt one stores none.
        if (tax == TaxTreatment.Taxable && c.GstRateBasisPoints is null) errors.Add(("gstRateBasisPoints", "required"));
        else if (tax == TaxTreatment.Taxable && c.GstRateBasisPoints is < 0 or > Service.MaxGstRateBasisPoints) errors.Add(("gstRateBasisPoints", "range"));
        if (c.DurationMinutes is < 1 or > Service.MaxDurationMinutes) errors.Add(("durationMinutes", "range"));
        if (c.ConsultationKind is { } kind && (!Enum.IsDefined(kind) || await KindTakenAsync(kind, service.Id, cancellationToken)))
            errors.Add(("consultationKind", "taken"));
        BillingValidationException.ThrowIfAny(errors);
        (service.Name, service.Category, service.Code, service.Sac, service.TaxTreatment) = (name!, c.Category!.Value, code, sac, tax);
        (service.GstRateBasisPoints, service.DurationMinutes, service.ConsultationKind) = (tax == TaxTreatment.Taxable ? c.GstRateBasisPoints : null, c.DurationMinutes, c.ConsultationKind);
        (service.ChangedBy, service.ChangedAt) = (user.UserId, clock.GetUtcNow());
    }

    Task<bool> KindTakenAsync(ConsultationKind kind, Guid exceptId, CancellationToken cancellationToken) =>
        db.Services.AnyAsync(s => s.ConsultationKind == kind && s.ArchivedAt == null && s.Id != exceptId, cancellationToken);

    async Task SaveAuditedAsync(Action stage, string action, Guid id, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        stage();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new BillingValidationException([("consultationKind", "taken")]); // two saves of the same kind at once; the index is the only unique one
        }
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = id,
            Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = facility.FacilityId,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    async Task<ServiceView> ViewAsync(Service s, CancellationToken cancellationToken)
    {
        var defaults = await db.PriceVersions.AsNoTracking().Where(v => v.ServiceId == s.Id && v.DoctorId == null).ToListAsync(cancellationToken);
        return View(s, db.Entry(s).Property<uint>("xmin").CurrentValue, PriceVersion.Resolve(defaults, null, clock.GetUtcNow())?.AmountMinor, await CurrencyAsync(cancellationToken));
    }

    static ServiceView View(Service s, uint version, long? clinicPrice, string currency) =>
        new(s.Id, s.Name, s.Category, s.Code, s.Sac, s.TaxTreatment, s.GstRateBasisPoints, s.DurationMinutes, s.ConsultationKind, s.ArchivedAt, clinicPrice, currency, version);

    async Task<string> CurrencyAsync(CancellationToken cancellationToken) =>
        BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.")).Currency.Code;

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[0-9]{6}$")] private static partial Regex Sac();
}
