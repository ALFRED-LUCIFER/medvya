using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Notifications.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Identity.Infrastructure;

/// <summary>
/// Patient SMS sign-in (MDV-P5-033). Each call locks the number's row, so parallel requests and guesses still count against the limits: at
/// most five codes an hour and one a minute, five wrong codes lock the number for <see cref="SmsSignInRules.LockPeriod"/>, and a lock also
/// ends the live code. All numbers together get at most <see cref="SmsOptions.SignInCodesPerHour"/> codes an hour (SMS pumping); above it the
/// answer is NotSent. Neither the number nor the code is logged.
/// </summary>
public sealed partial class PatientSmsSignIn(
    IdentityDbContext db, ISecureLinkMessages messages, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<PatientSmsSignIn> log) : IPatientSmsSignIn
{
    public async Task<SmsSignInResult> RequestCodeAsync(string mobile, string countryCode, string? languageTag, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await RowAsync(mobile, now, cancellationToken);
        if (Locked(row, now) is { } locked) return locked;
        if (row.WindowStartedAt + SmsSignInRules.Window <= now)
        {
            row.WindowStartedAt = now;
            row.CodesSent = 0;
        }
        if (row.CodesSent >= SmsSignInRules.MaxCodesPerWindow)
            return new(SmsSignInOutcome.TooManyCodes, SmsSignInRules.MinutesLeft(row.WindowStartedAt + SmsSignInRules.Window, now));
        if (row.LastSentAt + SmsSignInRules.ResendAfter is { } resend && resend > now)
            return new(SmsSignInOutcome.TooManyCodes, SmsSignInRules.MinutesLeft(resend, now));

        var hourAgo = now - SmsSignInRules.Window;
        // A number's sends before its window started count once (its last), so this is a floor of the hour's sends, enough for a cost cap.
        var sentLastHour = await db.SmsSignIns.Where(r => r.LastSentAt > hourAgo).SumAsync(r => r.WindowStartedAt > hourAgo ? r.CodesSent : 1, cancellationToken);
        if (sentLastHour >= options.Value.Sms.SignInCodesPerHour)
        {
            BudgetSpent(log);
            return new(SmsSignInOutcome.NotSent);
        }

        var code = SmsSignInRules.NewCode();
        row.CodeHash = SmsSignInRules.HashCode(row.PhoneHash, code);
        row.CodeExpiresAt = now + SmsSignInRules.CodeLifetime;
        row.LastSentAt = now;
        row.CodesSent++;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        // Counted before it is sent, and sent whether or not the caller hangs up, so no SMS goes out uncounted. A send that fails still
        // counts against the limits: the provider may have sent it, and an attacker must not get free tries by making sends fail.
        try
        {
            await messages.SendSignInCodeAsync(mobile, countryCode, languageTag, code, Guid.CreateVersion7(), CancellationToken.None);
        }
        catch (SecureLinkNotSentException)
        {
            return new(SmsSignInOutcome.NotSent);
        }
        return new(SmsSignInOutcome.Sent);
    }

    public async Task<SmsSignInResult> VerifyAsync(string mobile, string? code, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await RowAsync(mobile, now, cancellationToken);
        if (Locked(row, now) is { } locked) return locked;

        SmsSignInResult result;
        if (SmsSignInRules.CodeMatches(row, code, now))
        {
            row.WrongCodes = 0;
            result = new(SmsSignInOutcome.Verified);
        }
        else if (++row.WrongCodes >= SmsSignInRules.MaxWrongCodes)
        {
            row.LockedUntil = now + SmsSignInRules.LockPeriod;
            NumberLocked(log);
            result = new(SmsSignInOutcome.Locked, SmsSignInRules.MinutesLeft(row.LockedUntil.Value, now));
        }
        else result = new(SmsSignInOutcome.WrongCode);
        // A code works once, and a lock ends it.
        if (result.Outcome != SmsSignInOutcome.WrongCode)
        {
            row.CodeHash = null;
            row.CodeExpiresAt = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>Locked while the period runs; once it is over, the wrong tries start again from zero.</summary>
    static SmsSignInResult? Locked(SmsSignIn row, DateTimeOffset now)
    {
        if (row.LockedUntil is not { } until) return null;
        if (until > now) return new(SmsSignInOutcome.Locked, SmsSignInRules.MinutesLeft(until, now));
        row.LockedUntil = null;
        row.WrongCodes = 0;
        return null;
    }

    /// <summary>The number's row, created if new, locked until the transaction ends.</summary>
    async Task<SmsSignIn> RowAsync(string mobile, DateTimeOffset now, CancellationToken ct)
    {
        var hash = SmsSignInRules.HashPhone(mobile);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO identity."SmsSignIns" ("PhoneHash", "CodesSent", "WindowStartedAt", "WrongCodes") VALUES ({hash}, 0, {now}, 0) ON CONFLICT DO NOTHING
            """, ct);
        return await db.SmsSignIns.FromSql($"""SELECT * FROM identity."SmsSignIns" WHERE "PhoneHash" = {hash} FOR UPDATE""").SingleAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A mobile number was locked after too many wrong sign-in codes")]
    static partial void NumberLocked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The hourly budget of sign-in SMS codes is spent; codes are refused until it frees up")]
    static partial void BudgetSpent(ILogger logger);
}
