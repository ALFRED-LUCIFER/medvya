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
    public const string AddAction = "formulary.add", BrandsAction = "formulary.brands.add", OrderSetAction = "orderset.create", Resource = "formulary";
    public const int MaxBrands = 10, MaxBrand = 100, MaxItems = 50, MaxItem = 200, MaxLimit = 50, MaxBrandRows = 500;
    /// <summary>What a brand row says for a medicine in no allergy class: stated, never the default of a missing answer.</summary>
    public const string NoAllergyClass = "none";

    public async Task<IReadOnlyList<FormularyItemView>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default)
    {
        var country = await CountryAsync(cancellationToken);
        var rules = FormularyRules.For(country);
        IQueryable<FormularyItem> items = db.FormularyItems.AsNoTracking().Where(i => i.Country == country && i.Active);
        var text = query?.Trim() ?? "";
        var q = Escape(text);
        var (starts, word) = ($"{q}%", $"% {q}%"); // blank matches everything
        // MDV-P1-088 (and MDV-AUDIT-1093): a term matches the start of a word, never inside one, so look-alikes (gliclazide for "az") stay out.
        // shortcut: a word starts at the name's start or after a space, so one after a hyphen or a slash is not found; a word index when a register is loaded.
        items = items.Where(i => EF.Functions.ILike(i.Generic, starts) || EF.Functions.ILike(i.Generic, word)
            || i.Brands.Any(b => EF.Functions.ILike(b, starts) || EF.Functions.ILike(b, word))
            || db.FormularyBrands.Any(b => b.FormularyItemId == i.Id && (EF.Functions.ILike(b.Name, starts) || EF.Functions.ILike(b.Name, word))));
        // A generic or a brand that starts with the text comes first.
        var rows = await items.OrderByDescending(i => EF.Functions.ILike(i.Generic, starts) || i.Brands.Any(b => EF.Functions.ILike(b, starts))
                || db.FormularyBrands.Any(b => b.FormularyItemId == i.Id && EF.Functions.ILike(b.Name, starts)))
            .ThenBy(i => i.Generic).ThenBy(i => i.Strength).ThenBy(i => i.Form)
            .Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var clinicBrands = (await db.FormularyBrands.AsNoTracking().Where(b => ids.Contains(b.FormularyItemId)).OrderBy(b => b.Name).ToListAsync(cancellationToken)).ToLookup(b => b.FormularyItemId, b => b.Name);
        return rows.Select(i => View(i, rules, clinicBrands[i.Id])).ToList();
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
        await SaveAuditedAsync(() => db.FormularyItems.Add(item), AddAction, item.Id, $"class:{item.RegClass}", cancellationToken);
        return View(item, rules, []);
    }

    public async Task<BrandsAdded> AddBrandsAsync(AddBrands command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rows = command.Rows ?? [];
        if (rows.Count is 0 or > MaxBrandRows) throw new FormularyValidationException([("rows", rows.Count == 0 ? "required" : "too_many")]);
        var country = await CountryAsync(cancellationToken);
        // The clinic's whole list, read once: a few thousand rows at most, against one query a row.
        var listed = (await db.FormularyItems.AsNoTracking().Where(i => i.Country == country && i.Active).ToListAsync(cancellationToken)).ToLookup(i => Key(i.Generic, i.Strength, i.Form));
        var generics = listed.SelectMany(g => g).Select(i => i.Generic).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = (await db.FormularyBrands.AsNoTracking().ToListAsync(cancellationToken)).Select(b => (b.FormularyItemId, b.Name.ToUpperInvariant())).ToHashSet();
        var errors = new List<(string, string)>();
        var brands = new List<FormularyBrand>();
        for (var n = 0; n < rows.Count; n++)
        {
            var (row, at, before) = (rows[n], $"rows[{n}]", errors.Count);
            var brand = Required(row?.Brand, $"{at}.brand", MaxBrand, errors);
            var generic = Required(row?.Generic, $"{at}.generic", 200, errors);
            var strength = Required(row?.Strength, $"{at}.strength", 50, errors);
            var form = Required(row?.Form, $"{at}.form", 40, errors);
            var allergyClass = Required(row?.AllergyClass, $"{at}.allergyClass", 40, errors);
            if (errors.Count > before) continue;
            // Exactly one listed medicine, or the row is refused: a brand is never attached to a guess.
            if (listed[Key(generic!, strength!, form!)].ToList() is not [var item]) errors.Add(($"{at}.generic", "not_listed"));
            // No ingredient code means no allergy check for the line (NotCheckedReason.NoIngredientCode), which is what a brand row must not be.
            else if (item.Atc is null) errors.Add(($"{at}.generic", "not_checked"));
            else if (!string.Equals(allergyClass, AllergyClassOf(item.Atc), StringComparison.OrdinalIgnoreCase)) errors.Add(($"{at}.allergyClass", "mismatch"));
            // A brand that is a listed generic's name (a column slipped in the paste) would show one medicine under another's name.
            else if (generics.Contains(brand!)) errors.Add(($"{at}.brand", "is_generic"));
            else if (item.Brands.Contains(brand!, StringComparer.OrdinalIgnoreCase) || !taken.Add((item.Id, brand!.ToUpperInvariant()))) errors.Add(($"{at}.brand", "already_listed"));
            else brands.Add(new FormularyBrand { Id = Guid.CreateVersion7(), TenantId = TenantId, FormularyItemId = item.Id, Name = brand, AddedBy = user.UserId, AddedAt = clock.GetUtcNow() });
        }
        if (errors.Count > 0) throw new FormularyValidationException(errors);
        await SaveAuditedAsync(() => db.FormularyBrands.AddRange(brands), BrandsAction, Guid.CreateVersion7(), $"brands:{brands.Count}", cancellationToken);
        return new BrandsAdded(brands.Count);
    }

    /// <summary>The allergy class the safety rules give an ingredient code, or "none": the same table the prescription check reads.</summary>
    public static string AllergyClassOf(string atc) =>
        SafetyRules.Classes.FirstOrDefault(c => c.Atc.Any(p => atc.StartsWith(p, StringComparison.Ordinal)))?.Id ?? NoAllergyClass;

    static string Key(string generic, string strength, string form) => $"{generic}|{strength}|{form}".ToUpperInvariant();

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
        await SaveAuditedAsync(() => db.OrderSets.Add(set), OrderSetAction, set.Id, $"items:{items.Length}", cancellationToken);
        return new OrderSetView(set.Id, set.Name, set.Items);
    }

    async Task SaveAuditedAsync(Action add, string action, Guid id, string detail, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        add();
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

    static FormularyItemView View(FormularyItem i, FormularyRules rules, IEnumerable<string> clinicBrands) =>
        new(i.Id, i.Generic, i.Strength, i.Form, [.. i.Brands, .. clinicBrands], i.NationalCode, i.RegClass, rules.PrintRequirements(i.RegClass), i.Atc, i.DefaultSig, i.Source, i.Active);

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
