namespace Medvya.Identity.Application;

/// <summary>Sent: a code went out. TooManyCodes: wait before asking again. NotSent: no SMS provider took it. Locked: five wrong codes.</summary>
public enum SmsSignInOutcome { Sent, TooManyCodes, NotSent, Verified, WrongCode, Locked }

/// <param name="RetryAfterMinutes">For Locked and TooManyCodes: whole minutes until the number may try again.</param>
public sealed record SmsSignInResult(SmsSignInOutcome Outcome, int? RetryAfterMinutes = null);

/// <summary>
/// Patient sign-in by mobile number and SMS code (MDV-P5-033). Numbers are E.164, already checked against the country's rules. Every answer is
/// the same whether or not anyone with the number is known.
/// </summary>
public interface IPatientSmsSignIn
{
    Task<SmsSignInResult> RequestCodeAsync(string mobile, string countryCode, string? languageTag, CancellationToken cancellationToken = default);

    Task<SmsSignInResult> VerifyAsync(string mobile, string? code, CancellationToken cancellationToken = default);
}
