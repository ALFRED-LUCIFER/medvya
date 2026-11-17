using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Cash sessions and the day close (MDV-P4-012). A session's takings are the succeeded payments stamped with it (Invoices.OpenCashSessionAsync, share
/// lock); a count locks the session for update first, so no payment can commit between its totals and its state change. Each change locks the session
/// row, checks the move, saves, writes its audit row last and commits.
/// </summary>
// shortcut: a payment taken while no session is Open belongs to no session; refuse cash payments without an open session once every pilot desk opens one (MDV-P4-009).
public sealed class CashSessions(
    BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility facility, IStaffAccess staff, IFacilityDirectory facilities,
    IStaffUsers users, IAuditLog audit, TimeProvider clock) : ICashSessions
{
    public const string Resource = "cash_session";
    const int RecentClosed = 10;

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid FacilityId => facility.FacilityId ?? throw new InvalidOperationException("No facility context.");

    public async Task<CashDayView> DayAsync(CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId;
        var rules = await RulesAsync(cancellationToken);
        var current = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(s => s.FacilityId == facilityId && s.State == CashSessionState.Open, cancellationToken);
        var awaiting = await db.CashSessions.AsNoTracking().Where(s => s.FacilityId == facilityId && s.State == CashSessionState.AwaitingConfirmation)
            .OrderBy(s => s.CountedAt).ToListAsync(cancellationToken);
        var recent = await db.CashSessions.AsNoTracking().Where(s => s.FacilityId == facilityId && s.State == CashSessionState.Closed)
            .OrderByDescending(s => s.ClosedAt).Take(RecentClosed).ToListAsync(cancellationToken);
        var views = await ViewsAsync([.. current is null ? [] : new[] { current }, .. awaiting, .. recent], cancellationToken);
        var skip = current is null ? 0 : 1;
        return new CashDayView(current is null ? null : views[0], [.. views.Skip(skip).Take(awaiting.Count)], rules.CashDenominationsMinor, rules.Currency.Code,
            [.. views.Skip(skip + awaiting.Count)]);
    }

    public async Task<CashSessionView> OpenAsync(OpenCashSession command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OpeningFloatMinor is not (>= 0 and <= CashSession.MaxFloatMinor)) throw new BillingValidationException([("openingFloatMinor", "range")]);
        var now = clock.GetUtcNow();
        var session = new CashSession
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = FacilityId, State = CashSessionState.Open, Currency = (await RulesAsync(cancellationToken)).Currency.Code,
            OpeningFloatMinor = command.OpeningFloatMinor.Value, OpenedBy = user.UserId, OpenedAt = now,
        };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            db.CashSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "billing.cash.open", session, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        // SAFETY: mutation check: dropped the unique index IX_CashSessions_TenantId_FacilityId (Open) -> BillingInvoiceTests.OneLocationHasOneOpenSessionEvenWhenTwoDesksOpenAtOnce failed (two sessions opened)
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new BillingValidationException([("session", "already_open")]); // the location already has an Open session
        }
        return (await ViewsAsync([session], cancellationToken))[0];
    }

    public async Task<CashSessionView?> CountAsync(Guid id, CountCashSession command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Version is null) throw new BillingValidationException([("version", "required")]);
        var denominations = (await RulesAsync(cancellationToken)).CashDenominationsMinor;
        var given = command.Counts ?? [];
        // Every count is a known note or coin, once, with a sensible number; the stored count lists every denomination in order.
        if (given.Any(c => !denominations.Contains(c.ValueMinor) || c.Count is < 0 or > CashSession.MaxCount) || given.DistinctBy(c => c.ValueMinor).Count() != given.Count)
            throw new BillingValidationException([("counts", "invalid")]);
        List<CashCount> counts = [.. denominations.Select(d => new CashCount { ValueMinor = d, Count = given.FirstOrDefault(c => c.ValueMinor == d)?.Count ?? 0 })];
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note?.Length > CashSession.MaxNote) throw new BillingValidationException([("note", "too_long")]);

        return await ChangeAsync(id, command.Version, async session =>
        {
            if (session.State == CashSessionState.Closed) throw new BillingValidationException([("state", "invalid")]);
            // The session is locked for update (ChangeAsync): every payment stamped with it has committed, and no new one can be until this commits.
            session.Count(counts, await TotalsAsync(session.Id, cancellationToken), note, user.UserId, clock.GetUtcNow());
            // SAFETY: mutation check: removed this note rule -> BillingInvoiceTests.AVarianceNeedsANoteAndASecondDifferentUser failed (a short drawer awaited confirmation with no note)
            if (session.VarianceMinor != 0 && note is null) throw new BillingValidationException([("note", "required")]);
            return session.State == CashSessionState.Closed ? "billing.cash.close" : "billing.cash.count";
        }, cancellationToken);
    }

    public async Task<CashSessionView?> ConfirmAsync(Guid id, ConfirmCashSession command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Version is null) throw new BillingValidationException([("version", "required")]);
        return await ChangeAsync(id, command.Version, session =>
        {
            if (session.State != CashSessionState.AwaitingConfirmation) throw new BillingValidationException([("state", "invalid")]);
            // SAFETY: mutation check: dropped the different-user condition in CashSession.MayConfirm -> BillingInvoiceTests.AVarianceNeedsANoteAndASecondDifferentUser failed (the counter confirmed their own variance)
            if (!session.MayConfirm(user.UserId)) throw new BillingValidationException([("confirm", "same_user")]);
            (session.State, session.ConfirmedBy, session.ClosedAt) = (CashSessionState.Closed, user.UserId, clock.GetUtcNow());
            return Task.FromResult("billing.cash.close");
        }, cancellationToken);
    }

    public async Task<CashSessionView?> AdjustAsync(Guid id, AddCashAdjustment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        var errors = new List<(string, string)>();
        if (command.AmountMinor is not { } amount || amount == 0 || Math.Abs(amount) > CashSession.MaxFloatMinor) errors.Add(("amountMinor", "range"));
        if (reason is null) errors.Add(("reason", "required"));
        else if (reason.Length > CashAdjustment.MaxReason) errors.Add(("reason", "too_long"));
        BillingValidationException.ThrowIfAny(errors);
        return await ChangeAsync(id, null, session =>
        {
            if (session.State != CashSessionState.Closed) throw new BillingValidationException([("state", "invalid")]); // before closing, recount instead
            db.CashAdjustments.Add(new CashAdjustment
            {
                Id = Guid.CreateVersion7(), TenantId = session.TenantId, SessionId = session.Id, AmountMinor = command.AmountMinor!.Value, Reason = reason!,
                CreatedBy = user.UserId, CreatedAt = clock.GetUtcNow(),
            });
            return Task.FromResult("billing.cash.adjust");
        }, cancellationToken);
    }

    public async Task<ShiftReportPdf?> ReportAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId;
        var session = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.FacilityId == facilityId && s.State == CashSessionState.Closed, cancellationToken);
        if (session is null) return null;
        var view = (await ViewsAsync([session], cancellationToken))[0];
        var place = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("No facility context.");
        return new ShiftReportPdf(ShiftReportPdfDocument.Render(view, place, await facilities.LogoAsync(cancellationToken)));
    }

    /// <summary>Locks this facility's session (row-level security applies), checks its version, applies the change, saves, audits last and commits.</summary>
    async Task<CashSessionView?> ChangeAsync(Guid id, uint? version, Func<CashSession, Task<string>> change, CancellationToken cancellationToken)
    {
        var facilityId = FacilityId;
        CashSession? session;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM billing."CashSessions" WHERE "Id" = {id} AND "FacilityId" = {facilityId} FOR UPDATE""", cancellationToken);
            session = await db.CashSessions.SingleOrDefaultAsync(s => s.Id == id && s.FacilityId == facilityId, cancellationToken);
            if (session is null) return null;
            if (version is not null && db.Entry(session).Property<uint>("xmin").CurrentValue != version) throw new DbUpdateConcurrencyException();
            var action = await change(session);
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, action, session, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        return (await ViewsAsync([session], cancellationToken))[0];
    }

    /// <summary>The succeeded payments stamped with the session, by method.</summary>
    async Task<List<MethodTotal>> TotalsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var totals = await db.Payments.AsNoTracking().Where(p => p.CashSessionId == sessionId && p.State == PaymentState.Succeeded).GroupBy(p => p.Method)
            .Select(g => new MethodTotal { Method = g.Key, Payments = g.Count(), AmountMinor = g.Sum(x => x.AmountMinor) }).ToListAsync(cancellationToken);
        return [.. totals.OrderBy(t => t.Method)]; // the enum's order (cash first), not the stored name's
    }

    async Task<List<CashSessionView>> ViewsAsync(IReadOnlyList<CashSession> sessions, CancellationToken cancellationToken)
    {
        var ids = sessions.Select(s => s.Id).ToList();
        var adjustments = await db.CashAdjustments.AsNoTracking().Where(a => ids.Contains(a.SessionId)).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var versions = await db.CashSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, Version = EF.Property<uint>(s, "xmin") })
            .ToDictionaryAsync(s => s.Id, s => s.Version, cancellationToken);
        var people = await users.DisplayNamesAsync(
            [.. sessions.SelectMany(s => new[] { s.OpenedBy, s.CountedBy, s.ConfirmedBy }).Concat(adjustments.Select(a => a.CreatedBy)).OfType<Guid>().Distinct()], cancellationToken);
        string Name(Guid? id) => id is { } x ? people.GetValueOrDefault(x) ?? "" : "";
        var views = new List<CashSessionView>();
        foreach (var s in sessions)
        {
            // An open session shows its takings so far; a counted one what the count saw.
            var totals = s.CountedAt is null ? await TotalsAsync(s.Id, cancellationToken) : s.Totals;
            var expected = s.ExpectedMinor ?? CashSession.Expected(s.OpeningFloatMinor, totals.Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.AmountMinor));
            views.Add(new CashSessionView(s.Id, s.State, s.Currency, s.OpeningFloatMinor, Name(s.OpenedBy), s.OpenedAt, s.Counts, totals, expected, s.CountedMinor,
                s.VarianceMinor, s.Note, s.CountedBy is null ? null : Name(s.CountedBy), s.CountedAt, s.ConfirmedBy is null ? null : Name(s.ConfirmedBy), s.ClosedAt,
                s.MayConfirm(user.UserId),
                [.. adjustments.Where(a => a.SessionId == s.Id).Select(a => new CashAdjustmentView(a.Id, a.AmountMinor, a.Reason, Name(a.CreatedBy), a.CreatedAt))],
                versions.GetValueOrDefault(s.Id)));
        }
        return views;
    }

    /// <summary>The audit row, written last before the commit (AGENTS.md 3.1). No patient: a drawer is not about one.</summary>
    Task AuditAsync(IDbContextTransaction tx, string action, CashSession session, CancellationToken cancellationToken) => audit.WriteAsync(new AuditEvent
    {
        Id = Guid.CreateVersion7(), TenantId = session.TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = session.Id,
        Purpose = AuditPurpose.Billing, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), FacilityId = session.FacilityId,
    }, tx.GetDbTransaction(), cancellationToken);

    async Task<BillingCountryRules> RulesAsync(CancellationToken cancellationToken) =>
        BillingCountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country."));
}
