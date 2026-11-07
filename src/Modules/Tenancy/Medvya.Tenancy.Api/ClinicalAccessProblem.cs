using Medvya.Tenancy.Application;

namespace Medvya.Tenancy.Api;

public static class ClinicalAccessProblem
{
    /// <summary>
    /// The 403 a clinical endpoint answers when <see cref="IClinicalAccess"/> refuses (MDV-P2-024): code <c>no_care_relationship</c> when
    /// asking for access can help (and whether a request is already waiting), else <c>no_access</c>. Never says anything about the patient.
    /// </summary>
    public static IResult For(ClinicalAccessDeniedException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access to this patient",
            extensions: new Dictionary<string, object?> { ["code"] = e.CanRequest ? "no_care_relationship" : "no_access", ["requestPending"] = e.RequestPending });
    }
}
