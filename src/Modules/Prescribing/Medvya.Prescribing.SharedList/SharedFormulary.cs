using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Prescribing.SharedList;

/// <summary>
/// The formulary table seen as the country's shared list only (MDV-P5-064). Tenant isolation layer 1 of 2: the query filter keeps every row with a
/// tenant out; layer 2 is row-level security, which limits the medvya_formulary login to the same rows whatever the session sets (migration SharedFormularyLogin).
/// </summary>
public sealed class SharedListDbContext(DbContextOptions<SharedListDbContext> options) : DbContext(options)
{
    public DbSet<FormularyItem> Items => Set<FormularyItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prescribing");
        modelBuilder.Entity<FormularyItem>(e =>
        {
            e.ToTable("FormularyItems");
            e.Property(x => x.RegClass).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.HasQueryFilter(x => x.TenantId == null);
        });
    }
}

/// <summary>The console's shared-list service (<see cref="ISharedFormulary"/>): the clinic add's rules, the clinic search's matching, no tenant.</summary>
sealed class SharedFormulary(SharedListDbContext db, TimeProvider clock) : ISharedFormulary
{
    const int MaxLimit = 100;

    public async Task<IReadOnlyList<SharedMedicineView>> SearchAsync(string? country, string? query, int limit, CancellationToken cancellationToken = default)
    {
        Rules(country);
        var q = MedicineInput.LikeLiteral(query?.Trim() ?? "");
        var (starts, word) = ($"{q}%", $"% {q}%"); // blank matches everything
        // As the clinic search matches (FormularyService.SearchAsync, and its shortcut on speed): the start of a word of the generic or a brand.
        var rows = await db.Items.AsNoTracking()
            .Where(i => i.Country == country && (EF.Functions.ILike(i.Generic, starts) || EF.Functions.ILike(i.Generic, word)
                || i.Brands.Any(b => EF.Functions.ILike(b, starts) || EF.Functions.ILike(b, word))))
            .OrderByDescending(i => EF.Functions.ILike(i.Generic, starts) || i.Brands.Any(b => EF.Functions.ILike(b, starts)))
            .ThenBy(i => i.Generic).ThenBy(i => i.Strength).ThenBy(i => i.Form)
            .Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(cancellationToken);
        return [.. rows.Select(View)];
    }

    public async Task<SharedMedicineView> AddAsync(string? country, AddMedicine command, CancellationToken cancellationToken = default)
    {
        var m = MedicineInput.Validate(command, Rules(country));
        await RefuseListedAsync(country!, m, null, cancellationToken);
        var item = new FormularyItem
        {
            Id = Guid.CreateVersion7(), TenantId = null, Country = country!, Generic = m.Generic, Strength = m.Strength, Form = m.Form, Brands = m.Brands,
            NationalCode = m.NationalCode, RegClass = m.RegClass, Atc = m.Atc, DefaultSig = m.DefaultSig, Source = FormularySource.Medvya, Active = true,
            AddedAt = clock.GetUtcNow(), // the platform audit entry names the member; AddedBy holds clinic user ids only
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return View(item);
    }

    public async Task<SharedMedicineView?> CorrectAsync(Guid id, AddMedicine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(id, cancellationToken) is not { } row) return null;
        // The class is not part of a correction: it changes only through SetClassAsync, after an advisor's review.
        var m = MedicineInput.Validate(command with { RegClass = row.RegClass }, Rules(row.Country));
        await RefuseListedAsync(row.Country, m, id, cancellationToken);
        await db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Generic, m.Generic).SetProperty(i => i.Strength, m.Strength)
            .SetProperty(i => i.Form, m.Form).SetProperty(i => i.Brands, m.Brands).SetProperty(i => i.NationalCode, m.NationalCode)
            .SetProperty(i => i.Atc, m.Atc).SetProperty(i => i.DefaultSig, m.DefaultSig), cancellationToken);
        return View((await FindAsync(id, cancellationToken))!);
    }

    public async Task<SharedMedicineView?> SetClassAsync(Guid id, RegulatoryClass? regClass, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(id, cancellationToken) is not { } row) return null;
        if (regClass is not { } c) throw new FormularyValidationException([("regClass", "required")]);
        if (!Rules(row.Country).Classes().Contains(c)) throw new FormularyValidationException([("regClass", "not_in_country")]);
        await db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.RegClass, c), cancellationToken);
        return View((await FindAsync(id, cancellationToken))!);
    }

    public async Task<SharedMedicineView?> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(id, cancellationToken) is null) return null;
        await db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Active, false), cancellationToken);
        return View((await FindAsync(id, cancellationToken))!);
    }

    Task<FormularyItem?> FindAsync(Guid id, CancellationToken cancellationToken) => db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <summary>The country lists this medicine already (the clinic add's comparison, ignoring case).</summary>
    // shortcut: checked, not a unique index (shared rows have no tenant, and the index treats each NULL as distinct), so two console members adding
    // the same medicine in the same instant both succeed; add a partial unique index if the console's team grows beyond a few people.
    async Task RefuseListedAsync(string country, ValidMedicine m, Guid? self, CancellationToken cancellationToken)
    {
        var (generic, strength) = (MedicineInput.LikeLiteral(m.Generic), MedicineInput.LikeLiteral(m.Strength));
        if (await db.Items.AnyAsync(i => i.Id != self && i.Country == country && EF.Functions.ILike(i.Generic, generic) && EF.Functions.ILike(i.Strength, strength) && i.Form == m.Form, cancellationToken))
            throw new FormularyConflictException("already_listed");
    }

    static FormularyRules Rules(string? country) => FormularyRules.Find(country) ?? throw new FormularyValidationException([("country", "unknown")]);

    static SharedMedicineView View(FormularyItem i) =>
        new(i.Id, i.Country, i.Generic, i.Strength, i.Form, i.Brands, i.NationalCode, i.RegClass, i.Atc, i.DefaultSig, i.Source, i.RegisterCode, i.RegisterRelease, i.Active);
}

public static class SharedListModule
{
    /// <summary>The console's medicine list (MDV-P5-064) on the narrow login medvya_formulary (infra/postgres/table-rights.sql), and nothing else of this module.</summary>
    public static IServiceCollection AddSharedFormularyPath(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<SharedListDbContext>(o => o.UseNpgsql(PostgresDataSources.For(connectionString)))
            .AddScoped<ISharedFormulary, SharedFormulary>();
    }
}
