using System.Data;
using System.Globalization;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The only way to change what a calendar holds or has booked (MDV-P1-022, PRD v2 P1-07). Each attempt runs in a SERIALIZABLE
/// transaction that first takes sp_getapplock "doctor:{calendar}:{date}" (facility-local date) for every doctor-day it touches,
/// in a fixed order so two attempts never wait on each other in a circle. Deadlocks, lock timeouts and an ungranted app lock roll
/// back and are retried (nothing was written, so a retry cannot duplicate); after the last attempt the caller gets "busy".
/// A unique-index violation on SCHEDULE_SLOT means another booking won: "slot_unavailable". The unique index is the last line:
/// it holds even if the lock or the checks are bypassed.
/// </summary>
public sealed class DoctorDayTransaction(SchedulingDbContext db, IOptions<SchedulingOptions> options)
{
    const int Attempts = 3;

    public async Task<T> RunAsync<T>(IEnumerable<(Guid CalendarId, DateOnly Date)> doctorDays, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var locks = doctorDays.Distinct().OrderBy(l => l.CalendarId).ThenBy(l => l.Date).ToList();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                foreach (var (calendarId, date) in locks)
                    await LockAsync(db, string.Create(CultureInfo.InvariantCulture, $"doctor:{calendarId}:{date:yyyy-MM-dd}"), options.Value.LockTimeout, cancellationToken);
                var result = await work();
                await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception e) when (IsUniqueViolation(e))
            {
                db.ChangeTracker.Clear();
                throw new SlotUnavailableException("slot_unavailable");
            }
            catch (Exception e) when (IsTransient(e))
            {
                db.ChangeTracker.Clear(); // the transaction rolled back; the next attempt starts clean
                if (attempt == Attempts) throw new SlotUnavailableException("busy");
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    /// <summary>Exclusive app lock owned by the open transaction; released by commit or rollback. Throws when not granted in time.</summary>
    internal static async Task LockAsync(SchedulingDbContext db, string resource, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        var timeout = (int)lockTimeout.TotalMilliseconds;
        var granted = (await db.Database.SqlQuery<int>($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeout};
            SELECT @result AS Value;
            """).ToListAsync(cancellationToken)).Single();
        if (granted < 0) throw new LockNotGrantedException(); // -1 timeout, -2 cancelled, -3 chosen as deadlock victim
    }

    internal sealed class LockNotGrantedException() : Exception("Scheduling lock not granted.");

    static bool IsTransient(Exception e) => e is LockNotGrantedException || Sql(e) is { Number: 1205 or 1222 };

    static bool IsUniqueViolation(Exception e) => Sql(e) is { Number: 2601 or 2627 };

    // EF wraps SqlException in DbUpdateException, and that in InvalidOperationException from its execution strategy.
    static SqlException? Sql(Exception? e) => e is null ? null : e as SqlException ?? Sql(e.InnerException);
}
