using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

/// <summary>
/// Open while the desk takes payments; AwaitingConfirmation when the count differs from what is expected (a second, different staff user confirms);
/// Closed once the count matches or the variance is confirmed. A closed session is never changed again (TR_CashSessions_ClosedImmutable).
/// </summary>
public enum CashSessionState { Open, AwaitingConfirmation, Closed }

/// <summary>
/// One location's cash drawer for a shift (P-07, P-13, MDV-P4-012): the opening float, then at day end the counted notes and coins against the cash the
/// desk took, with the takings by method. At most one session per location is Open (unique index); a counted one may wait for confirmation while the
/// next is open. Each succeeded payment is stamped with the Open session (Payment.CashSessionId), so its takings are exact. Amounts are minor units.
/// </summary>
public sealed class CashSession
{
    public const int MaxNote = 200, MaxCount = 100_000;
    public const long MaxFloatMinor = 100_000_000; // ₹10 lakh: well past any desk float, short of an overflow

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public CashSessionState State { get; set; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public long OpeningFloatMinor { get; init; }
    [Classification(DataClass.Internal)] public Guid? OpenedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OpenedAt { get; init; }
    /// <summary>The counted notes and coins, from the last count.</summary>
    [Classification(DataClass.Internal)] public List<CashCount> Counts { get; set; } = [];
    /// <summary>What the desk took in the session by method, as it stood at the last count.</summary>
    [Classification(DataClass.Internal)] public List<MethodTotal> Totals { get; set; } = [];
    [Classification(DataClass.Internal)] public long? ExpectedMinor { get; set; }
    [Classification(DataClass.Internal)] public long? CountedMinor { get; set; }
    /// <summary>Counted − expected: negative when the drawer is short.</summary>
    [Classification(DataClass.Internal)] public long? VarianceMinor { get; set; }
    /// <summary>Why the drawer differs, required with any variance. No patient detail belongs here.</summary>
    [Classification(DataClass.Personal)] public string? Note { get; set; }
    [Classification(DataClass.Internal)] public Guid? CountedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? CountedAt { get; set; }
    /// <summary>The second, different staff user who confirmed the variance; null when the count matched.</summary>
    [Classification(DataClass.Internal)] public Guid? ConfirmedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Expected cash in the drawer: the float plus cash receipts minus cash refunds.</summary>
    public static long Expected(long floatMinor, long cashReceiptsMinor, long cashRefundsMinor) => floatMinor + cashReceiptsMinor - cashRefundsMinor;

    /// <summary><see cref="Expected(long, long, long)"/> from the session's takings by method.</summary>
    public static long Expected(long floatMinor, IEnumerable<MethodTotal> totals)
    {
        var cash = totals.Where(t => t.Method == PaymentMethod.Cash).ToList();
        return Expected(floatMinor, cash.Sum(t => t.AmountMinor), cash.Sum(t => t.RefundsMinor));
    }

    /// <summary>
    /// Records a count: its totals, expected cash and variance. A count that matches closes the session; any variance waits for a second person's
    /// confirmation. Only an open or awaiting session is counted (a recount replaces the last one).
    /// </summary>
    public void Count(IReadOnlyList<CashCount> counts, IReadOnlyList<MethodTotal> totals, string? note, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(totals);
        if (State == CashSessionState.Closed) throw new InvalidOperationException("A closed session is not counted again.");
        Counts = [.. counts];
        Totals = [.. totals];
        CountedMinor = counts.Sum(c => c.ValueMinor * c.Count);
        ExpectedMinor = Expected(OpeningFloatMinor, totals);
        VarianceMinor = CountedMinor - ExpectedMinor;
        (Note, CountedBy, CountedAt) = (note, by, at);
        (State, ClosedAt) = VarianceMinor == 0 ? (CashSessionState.Closed, at) : (CashSessionState.AwaitingConfirmation, (DateTimeOffset?)null);
    }

    /// <summary>True when <paramref name="user"/> may confirm this variance: the session awaits it and the user is not the one who counted.</summary>
    // shortcut: a location with one billing user can never confirm a variance; who may confirm in a one-person clinic is an owner decision (PR #1429).
    public bool MayConfirm(Guid? user) => State == CashSessionState.AwaitingConfirmation && user is not null && user != CountedBy;
}

/// <summary>How many of one note or coin were counted (₹500 notes: 50 000 paise × count).</summary>
public sealed record CashCount
{
    [Classification(DataClass.Internal)] public long ValueMinor { get; init; }
    [Classification(DataClass.Internal)] public int Count { get; init; }
}

/// <summary>Succeeded payments of one method in a session, how many and how much, and the refunds (credit notes) made that way in it.</summary>
public sealed record MethodTotal
{
    [Classification(DataClass.Internal)] public PaymentMethod Method { get; init; }
    [Classification(DataClass.Internal)] public int Payments { get; init; }
    [Classification(DataClass.Internal)] public long AmountMinor { get; init; }
    [Classification(DataClass.Internal)] public int Refunds { get; init; }
    [Classification(DataClass.Internal)] public long RefundsMinor { get; init; }
}

/// <summary>A correction to a closed session (MDV-P4-012): the session stays as it closed; the adjustment says what changed and why. Append-only.</summary>
public sealed class CashAdjustment
{
    public const int MaxReason = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid SessionId { get; init; }
    /// <summary>Signed: positive when cash was found, negative when it went out; never 0.</summary>
    [Classification(DataClass.Internal)] public long AmountMinor { get; init; }
    [Classification(DataClass.Personal)] public required string Reason { get; init; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}
