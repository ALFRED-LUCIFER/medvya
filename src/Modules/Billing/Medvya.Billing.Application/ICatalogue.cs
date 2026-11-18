using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

/// <summary>A catalogue item with its clinic default price in effect now (null: not priced yet). <see cref="Version"/> guards an edit (#889).</summary>
public sealed record ServiceView(
    Guid Id, string Name, ServiceCategory Category, string? Code, string? Sac, TaxTreatment TaxTreatment, int? GstRateBasisPoints, int? DurationMinutes,
    ConsultationKind? ConsultationKind, DateTimeOffset? ArchivedAt, long? ClinicPriceMinor, string Currency, uint Version);

/// <summary>What a starter list load did: services added, and rows skipped because the clinic already has their code.</summary>
public sealed record StarterLoadResult(int Added, int Skipped);

public sealed record PriceVersionView(Guid Id, Guid? DoctorId, long AmountMinor, string Currency, DateTimeOffset EffectiveFrom, DateTimeOffset CreatedAt);

/// <summary>What an invoice line takes from the catalogue (MDV-P4-004): the version it was priced from, kept on the line.</summary>
public sealed record ResolvedPrice(Guid PriceVersionId, Guid ServiceId, Guid? DoctorId, long AmountMinor, string Currency, DateTimeOffset EffectiveFrom);

public sealed record SaveService
{
    public string? Name { get; init; }
    public ServiceCategory? Category { get; init; }
    public string? Code { get; init; }
    public string? Sac { get; init; }
    public TaxTreatment? TaxTreatment { get; init; }
    public int? GstRateBasisPoints { get; init; }
    public int? DurationMinutes { get; init; }
    public ConsultationKind? ConsultationKind { get; init; }
    /// <summary>Required on an edit: the version the editor read.</summary>
    public uint? Version { get; init; }
}

public sealed record AddPrice
{
    /// <summary>The doctor's membership id; null for the clinic default.</summary>
    public Guid? DoctorId { get; init; }
    public long? AmountMinor { get; init; }
    /// <summary>The first day the price applies, in the current facility's time zone; today or later. Null or today: from now.</summary>
    public DateOnly? EffectiveOn { get; init; }
}

/// <summary>Field/code pairs (never the entered values), as the screen translates them.</summary>
public sealed class BillingValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Billing input is not valid.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;

    public static void ThrowIfAny(List<(string, string)> errors)
    {
        if (errors.Count > 0) throw new BillingValidationException(errors);
    }
}

/// <summary>
/// The services catalogue and its prices (MDV-P4-002) for the current tenant. The endpoints check the caller's capability and the billing flag;
/// tenant filters and row-level security scope every row. Each change is audited in its own transaction.
/// </summary>
public interface ICatalogue
{
    Task<IReadOnlyList<ServiceView>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <exception cref="BillingValidationException"/>
    Task<ServiceView> CreateAsync(SaveService command, CancellationToken cancellationToken = default);

    /// <summary>Edits everything but the price and a consultation kind once set ("consultationKind:fixed"). Null when there is no such service.</summary>
    /// <exception cref="BillingValidationException"/>
    /// <remarks>Throws EF's DbUpdateConcurrencyException when someone else saved it since <see cref="SaveService.Version"/> (the endpoint answers 409).</remarks>
    Task<ServiceView?> UpdateAsync(Guid id, SaveService command, CancellationToken cancellationToken = default);

    /// <summary>Archives (or brings back) a service; null when there is none.</summary>
    /// <exception cref="BillingValidationException">"consultationKind:fixed": archiving a consultation service (it holds every doctor's fee);
    /// "consultationKind:taken": bringing one back while another of its kind is active.</exception>
    Task<ServiceView?> ArchiveAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    /// <summary>Every version, newest first; null when there is no such service.</summary>
    Task<IReadOnlyList<PriceVersionView>?> PricesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inserts a price version; null when there is no such service.</summary>
    /// <exception cref="BillingValidationException"/>
    Task<PriceVersionView?> AddPriceAsync(Guid id, AddPrice command, CancellationToken cancellationToken = default);

    /// <summary>Loads the dental starter list (MDV-P8-010): the rows whose code the clinic does not use yet, each with its default price. Idempotent.</summary>
    /// <exception cref="BillingValidationException">"country:unsupported".</exception>
    Task<StarterLoadResult> LoadDentalStarterAsync(CancellationToken cancellationToken = default);

    /// <summary>The price of a service for a doctor at a moment (<see cref="PriceVersion.Resolve"/>); null when it has none, or there is no such service.</summary>
    Task<ResolvedPrice?> ResolvePriceAsync(Guid serviceId, Guid? doctorId, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary><see cref="Version"/>: the row version a save must send back (0 before the first save); a save from an older version is refused (409, #889).</summary>
public sealed record BillingSettingsView(
    string InvoicePrefix, string ReceiptPrefix, string CreditNotePrefix, IReadOnlyList<PaymentMethod> PaymentMethods, IReadOnlyList<string> DiscountReasons,
    int MaxDiscountPercent, int PaymentLinkExpiryMinutes, uint Version, long RefundApprovalAboveMinor);

public sealed record SaveBillingSettings
{
    public string? InvoicePrefix { get; init; }
    public string? ReceiptPrefix { get; init; }
    public string? CreditNotePrefix { get; init; }
    public IReadOnlyList<PaymentMethod>? PaymentMethods { get; init; }
    public IReadOnlyList<string>? DiscountReasons { get; init; }
    public int? MaxDiscountPercent { get; init; }
    public int? PaymentLinkExpiryMinutes { get; init; }
    /// <summary>A refund above this (minor units) needs practice configuration; 0 means every refund does. Left out: unchanged.</summary>
    public long? RefundApprovalAboveMinor { get; init; }
    /// <summary>Required: the version the editor read (<see cref="BillingSettingsView.Version"/>).</summary>
    public uint? Version { get; init; }
}

/// <summary>The tenant's billing settings (MDV-P4-002), with defaults until first saved.</summary>
public interface IBillingSettings
{
    Task<BillingSettingsView> GetAsync(CancellationToken cancellationToken = default);

    /// <exception cref="BillingValidationException"/>
    /// <remarks>Throws EF's DbUpdateConcurrencyException when the settings changed since <see cref="SaveBillingSettings.Version"/> (the endpoint answers 409).</remarks>
    Task<BillingSettingsView> SaveAsync(SaveBillingSettings command, CancellationToken cancellationToken = default);
}
