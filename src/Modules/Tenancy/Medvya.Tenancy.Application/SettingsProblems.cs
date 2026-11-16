using Microsoft.AspNetCore.Http;

namespace Medvya.Tenancy.Application;

/// <summary>The settings and invitation use-case errors as problem details, for every module's settings endpoints.</summary>
public static class SettingsProblems
{
    /// <summary>Use-case errors as problem details: field/code pairs for 400, and stable codes for invitation and plan limits.</summary>
    public static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SettingsValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(x => x.Key, x => x.Select(y => y.Code).ToArray()));
        }
        catch (InvitationEmailException)
        {
            return EmailFailed;
        }
        catch (InvitationLimitException)
        {
            return TooManyInvitations;
        }
        catch (PlanLimitException e)
        {
            return PlanLimit(e);
        }
    }

    /// <summary>429 with a stable code when the practice has sent its hourly allowance of invitations (#886).</summary>
    public static IResult TooManyInvitations => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many invitations in the last hour.", extensions: new Dictionary<string, object?> { ["code"] = "invitation_limit" });

    /// <summary>409 with a stable code and the plan's limits when the plan has no free doctor or staff seat (MDV-P1-069).</summary>
    public static IResult PlanLimit(PlanLimitException e) => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: e.Message,
        extensions: new Dictionary<string, object?> { ["code"] = "plan_limit", ["doctors"] = e.Doctors, ["staff"] = e.Staff });

    /// <summary>502 with a stable code: the screen says the invitation email could not be sent and nothing was saved, not a generic failure.</summary>
    public static IResult EmailFailed => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The invitation email could not be sent.", extensions: new Dictionary<string, object?> { ["code"] = "email_failed" });
}
