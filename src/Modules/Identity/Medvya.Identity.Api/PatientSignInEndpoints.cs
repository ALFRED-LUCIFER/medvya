using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Identity.Api;

/// <summary>
/// Patient app sign-in by mobile number and SMS code (MDV-P5-033, ADR-011 §2). Public and limited per IP (the sign-in policy) and per number
/// (<see cref="IPatientSmsSignIn"/>). The number travels in the body only, never in a URL. Every valid number gets the same answers, known or not.
/// </summary>
public static class PatientSignInEndpoints
{
    public sealed record CodeRequest(string? CountryCode, string? Mobile, string? Language);
    public sealed record VerifyRequest(string? CountryCode, string? Mobile, string? Code);

    public static WebApplication MapPatientSignIn(this WebApplication app)
    {
        var group = app.MapGroup("/v1/patient-sign-in").AllowAnonymous().RequireRateLimiting(InvitationEndpoints.SignInRateLimit);

        group.MapPost("/code", async (CodeRequest request, [FromServices] IPatientSmsSignIn signIn, CancellationToken ct) =>
            CountryRules.Mobile(request.CountryCode, request.Mobile) is not { } mobile ? InvalidNumber
                : Answer(await signIn.RequestCodeAsync(mobile, request.CountryCode!, request.Language, ct)));

        // shortcut: a right code answers { verified: true } and starts no session, because patient accounts and a patient principal are not
        // designed yet (ADR-011 §2 waits for them); the app's sign-in stays on Development data until that ADR lands (MDV-P5-033, MDV-P5-036).
        group.MapPost("/verify", async (VerifyRequest request, [FromServices] IPatientSmsSignIn signIn, CancellationToken ct) =>
            CountryRules.Mobile(request.CountryCode, request.Mobile) is not { } mobile ? InvalidNumber
                : Answer(await signIn.VerifyAsync(mobile, request.Code, ct)));

        return app;
    }

    static IResult InvalidNumber => Results.ValidationProblem(new Dictionary<string, string[]> { ["mobile"] = ["format"] });

    /// <summary>Codes the app turns into words; "locked" and "too_many_codes" carry the minutes to wait.</summary>
    static IResult Answer(SmsSignInResult result) => result.Outcome switch
    {
        SmsSignInOutcome.Sent => Results.Accepted(),
        SmsSignInOutcome.Verified => Results.Ok(new { verified = true }),
        SmsSignInOutcome.WrongCode => Results.BadRequest(new { error = "wrong_code" }),
        SmsSignInOutcome.NotSent => Results.Json(new { error = "send_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        SmsSignInOutcome.Locked => Results.Json(new { error = "locked", retryAfterMinutes = result.RetryAfterMinutes }, statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Json(new { error = "too_many_codes", retryAfterMinutes = result.RetryAfterMinutes }, statusCode: StatusCodes.Status429TooManyRequests),
    };
}
