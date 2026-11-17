using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

public sealed record CashAdjustmentView(Guid Id, long AmountMinor, string Reason, string CreatedByName, DateTimeOffset CreatedAt);

/// <summary>
/// A cash session as the day close shows it (P-13). While open, <see cref="Totals"/> and <see cref="ExpectedMinor"/> are live (payments so far); once
/// counted they are what the count saw. <see cref="MayConfirm"/> is true only for a different user than the counter while it awaits confirmation.
/// Counts and amounts only: no patient names (billing never carries clinical data, and the drawer needs none).
/// </summary>
public sealed record CashSessionView(
    Guid Id, CashSessionState State, string Currency, long OpeningFloatMinor, string OpenedByName, DateTimeOffset OpenedAt,
    IReadOnlyList<CashCount> Counts, IReadOnlyList<MethodTotal> Totals, long ExpectedMinor, long? CountedMinor, long? VarianceMinor, string? Note,
    string? CountedByName, DateTimeOffset? CountedAt, string? ConfirmedByName, DateTimeOffset? ClosedAt, bool MayConfirm,
    IReadOnlyList<CashAdjustmentView> Adjustments, uint Version);

/// <summary>
/// The day close screen: the location's Open session (none: the desk opens one), the counted ones waiting for a second person's confirmation, its
/// denominations, and the latest closed ones.
/// </summary>
public sealed record CashDayView(
    CashSessionView? Current, IReadOnlyList<CashSessionView> Awaiting, IReadOnlyList<long> Denominations, string Currency, IReadOnlyList<CashSessionView> Recent);

public sealed record OpenCashSession
{
    public long? OpeningFloatMinor { get; init; }
}

/// <summary>The drawer count: how many of each note and coin (denominations not listed count 0), and the note any variance needs.</summary>
public sealed record CountCashSession
{
    public IReadOnlyList<CashCount>? Counts { get; init; }
    public string? Note { get; init; }
    public uint? Version { get; init; }
}

public sealed record ConfirmCashSession
{
    public uint? Version { get; init; }
}

public sealed record AddCashAdjustment
{
    public long? AmountMinor { get; init; }
    public string? Reason { get; init; }
}

/// <summary>A rendered shift report.</summary>
public sealed record ShiftReportPdf(byte[] Content);

/// <summary>
/// Cash sessions and the day close (MDV-P4-012) at the current facility. Expected cash = float + cash receipts − cash refunds; a count that differs needs a
/// note and a second, different staff user's confirmation (enforced here, not in the screen). A closed session is immutable; corrections are adjustments.
/// Each change locks the session row, is audited last before its commit, and refuses with <see cref="BillingValidationException"/>:
/// "session:already_open", "state:invalid", "note:required", "counts:invalid", "confirm:same_user", "amountMinor:range", "reason:required".
/// A stale <c>Version</c> throws EF's DbUpdateConcurrencyException (409).
/// </summary>
public interface ICashSessions
{
    Task<CashDayView> DayAsync(CancellationToken cancellationToken = default);
    Task<CashSessionView> OpenAsync(OpenCashSession command, CancellationToken cancellationToken = default);

    /// <summary>Null when there is no such session at this facility.</summary>
    Task<CashSessionView?> CountAsync(Guid id, CountCashSession command, CancellationToken cancellationToken = default);
    Task<CashSessionView?> ConfirmAsync(Guid id, ConfirmCashSession command, CancellationToken cancellationToken = default);
    Task<CashSessionView?> AdjustAsync(Guid id, AddCashAdjustment command, CancellationToken cancellationToken = default);

    /// <summary>The shift report of a closed session (A4); null when there is no closed session with this id at this facility.</summary>
    Task<ShiftReportPdf?> ReportAsync(Guid id, CancellationToken cancellationToken = default);
}
