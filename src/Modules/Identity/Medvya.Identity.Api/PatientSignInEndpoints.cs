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
    /// <param name="Platform">"ios" or "android", and <paramref name="AppVersion"/>: the device registration shown as "Medvya app 1.2.0 on iOS".</param>
    public sealed record VerifyRequest(string? CountryCode, string? Mobile, string? Code, string? Platform = null, string? AppVersion = null);

    public static WebApplication MapPatientSignIn(this WebApplication app)
    {
        var group = app.MapGroup("/v1/patient-sign-in").AllowAnonymous().RequireRateLimiting(InvitationEndpoints.SignInRateLimit);

        group.MapPost("/code", async (CodeRequest request, [FromServices] IPatientSmsSignIn signIn, CancellationToken ct) =>
            CountryRules.Mobile(request.CountryCode, request.Mobile) is not { } mobile ? InvalidNumber
                : Answer(await signIn.RequestCodeAsync(mobile, request.CountryCode!, request.Language, ct)));

        // A right code starts a patient session and answers its token pair (ADR-020); the session reaches no clinic or patient data.
        group.MapPost("/verify", async (VerifyRequest request, HttpContext http, [FromServices] IPatientSmsSignIn signIn, CancellationToken ct) =>
        {
            if (CountryRules.Mobile(request.CountryCode, request.Mobile) is not { } mobile) return InvalidNumber;
            var result = await signIn.VerifyAsync(mobile, request.Code, ct);
            return result.Outcome == SmsSignInOutcome.Verified ? await PatientAuthentication.StartAsync(http, mobile, request.Platform, request.AppVersion) : Answer(result);
        });

        return app;
    }

    static IResult InvalidNumber => Results.ValidationProblem(new Dictionary<string, string[]> { ["mobile"] = ["format"] });

    /// <summary>Codes the app turns into words; "locked" and "too_many_codes" carry the minutes to wait.</summary>
    static IResult Answer(SmsSignInResult result) => result.Outcome switch
    {
        SmsSignInOutcome.Sent => Results.Accepted(),
        SmsSignInOutcome.WrongCode => Results.BadRequest(new { error = "wrong_code" }),
        SmsSignInOutcome.NotSent => Results.Json(new { error = "send_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        SmsSignInOutcome.Locked => Results.Json(new { error = "locked", retryAfterMinutes = result.RetryAfterMinutes }, statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Json(new { error = "too_many_codes", retryAfterMinutes = result.RetryAfterMinutes }, statusCode: StatusCodes.Status429TooManyRequests),
    };
}
