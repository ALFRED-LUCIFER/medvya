using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The country formulary and order sets (MDV-P2-014). Country rules decide what is valid (<see cref="FormularyRules"/>); this class never
/// looks at a country code itself except to pick them. An add is validated here, never only in the screen, and is audited in the same transaction.
/// </summary>
public sealed class FormularyService(PrescribingDbContext db, IStaffAccess staff, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IFormulary
{
    public const string AddAction = "formulary.add", OrderSetAction = "orderset.create", Resource = "formulary";
    public const int MaxBrands = 10, MaxBrand = 100, MaxItems = 50, MaxItem = 200, MaxLimit = 50;

    public async Task<IReadOnlyList<FormularyItemView>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default)
    {
        var country = await CountryAsync(cancellationToken);
        var rules = FormularyRules.For(country);
        IQueryable<FormularyItem> items = db.FormularyItems.AsNoTracking().Where(i => i.Country == country && i.Active);
        var q = Escape(query?.Trim() ?? "");
        var (contains, starts) = ($"%{q}%", $"{q}%"); // blank matches everything; a generic that starts with the text comes first
        items = items.Where(i => EF.Functions.ILike(i.Generic, contains) || i.Brands.Any(b => EF.Functions.ILike(b, contains)));
        var rows = await items.OrderByDescending(i => EF.Functions.ILike(i.Generic, starts)).ThenBy(i => i.Generic).ThenBy(i => i.Strength).ThenBy(i => i.Form)
            .Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        return rows.Select(i => View(i, rules)).ToList();
    }

    public async Task<FormularyItemView> AddAsync(AddMedicine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var country = await CountryAsync(cancellationToken);
        var rules = FormularyRules.For(country);
        var errors = new List<(string, string)>();
        var generic = Required(command.Generic, "generic", 200, errors);
        var strength = Required(command.Strength, "strength", 50, errors);
        var form = command.Form?.Trim();
        if (string.IsNullOrEmpty(form)) errors.Add(("form", "required"));
        else if (!FormularyRules.Forms.Contains(form)) errors.Add(("form", "unknown"));
        var brands = (command.Brands ?? []).Select(b => b?.Trim() ?? "").Where(b => b.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (brands.Length > MaxBrands) errors.Add(("brands", "too_many"));
        else if (brands.Any(b => b.Length > MaxBrand)) errors.Add(("brands", "too_long"));
        // Blank is allowed and stays blank; a value must have the country's shape.
        var code = Optional(command.NationalCode);
        if (code is not null && !rules.IsNationalCode(code)) errors.Add(("nationalCode", "invalid"));
        var atc = Optional(command.Atc)?.ToUpperInvariant();
        if (atc is not null && !FormularyRules.IsAtc(atc)) errors.Add(("atc", "invalid"));
        var sig = Optional(command.DefaultSig);
        if (sig?.Length > 200) errors.Add(("defaultSig", "too_long"));
        // The class is chosen on purpose: an ordinary medicine is "None", never the default of a missing answer.
        if (command.RegClass is not { } regClass) errors.Add(("regClass", "required"));
        else if (!rules.Classes().Contains(regClass)) errors.Add(("regClass", "not_in_country"));
        if (errors.Count > 0) throw new FormularyValidationException(errors);

        var (sameGeneric, sameStrength) = (Escape(generic!), Escape(strength!));
        if (await db.FormularyItems.AnyAsync(i => i.Country == country && EF.Functions.ILike(i.Generic, sameGeneric) && EF.Functions.ILike(i.Strength, sameStrength) && i.Form == form, cancellationToken))
            throw new FormularyConflictException("already_listed");

        var item = new FormularyItem
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, Country = country, Generic = generic!, Strength = strength!, Form = form!, Brands = brands,
            NationalCode = code, RegClass = command.RegClass!.Value, Atc = atc, DefaultSig = sig, Source = FormularySource.ClinicAdded, Active = true,
            AddedBy = user.UserId, AddedAt = clock.GetUtcNow(),
        };
        await SaveAuditedAsync(item, null, AddAction, item.Id, $"class:{item.RegClass}", cancellationToken);
        return View(item, rules);
    }

    public async Task<IReadOnlyList<OrderSetView>> OrderSetsAsync(CancellationToken cancellationToken = default) =>
        (await db.OrderSets.AsNoTracking().OrderBy(o => o.Name).ToListAsync(cancellationToken)).Select(o => new OrderSetView(o.Id, o.Name, o.Items)).ToList();

    public async Task<OrderSetView> CreateOrderSetAsync(CreateOrderSet command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<(string, string)>();
        var name = Required(command.Name, "name", 100, errors);
        var items = (command.Items ?? []).Select(i => i?.Trim() ?? "").Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (items.Length == 0) errors.Add(("items", "required"));
        else if (items.Length > MaxItems) errors.Add(("items", "too_many"));
        else if (items.Any(i => i.Length > MaxItem)) errors.Add(("items", "too_long"));
        if (errors.Count > 0) throw new FormularyValidationException(errors);
        var sameName = Escape(name!);
        if (await db.OrderSets.AnyAsync(o => EF.Functions.ILike(o.Name, sameName), cancellationToken)) throw new FormularyConflictException("already_listed");

        var set = new OrderSet { Id = Guid.CreateVersion7(), TenantId = TenantId, Name = name!, Items = items, CreatedBy = user.UserId ?? throw new InvalidOperationException("No user."), CreatedAt = clock.GetUtcNow() };
        await SaveAuditedAsync(null, set, OrderSetAction, set.Id, $"items:{items.Length}", cancellationToken);
        return new OrderSetView(set.Id, set.Name, set.Items);
    }

    async Task SaveAuditedAsync(FormularyItem? item, OrderSet? set, string action, Guid id, string detail, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (item is not null) db.FormularyItems.Add(item);
        if (set is not null) db.OrderSets.Add(set);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new FormularyConflictException("already_listed"); // two simultaneous adds of the same entry
        }
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = id,
            Purpose = AuditPurpose.Operations, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), Detail = detail,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    static FormularyItemView View(FormularyItem i, FormularyRules rules) =>
        new(i.Id, i.Generic, i.Strength, i.Form, i.Brands, i.NationalCode, i.RegClass, rules.PrintRequirements(i.RegClass), i.Atc, i.DefaultSig, i.Source, i.Active);

    static string? Required(string? value, string field, int max, List<(string, string)> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) errors.Add((field, "required"));
        else if (v.Length > max) errors.Add((field, "too_long"));
        return v;
    }

    static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string Escape(string q) => q.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    async Task<string> CountryAsync(CancellationToken cancellationToken) =>
        await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}
