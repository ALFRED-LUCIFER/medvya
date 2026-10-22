using Host.Api.Auth;
using Host.Api.Context;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.Invitations;

/// <summary>Invite a colleague (admin) and preview an invitation from its link (public, token-based). MDV-P0-013.</summary>
public static class InvitationEndpoints
{
    public sealed record InviteRequest(string Email, Role Role, Guid FacilityId);
    public sealed record PreviewRequest(string Token);
    public sealed record CapabilityAccess(Capability Capability, Access Access);

    public static WebApplication MapInvitations(this WebApplication app)
    {
        var admin = app.MapGroup("/v1/invitations").RequireAuthorization(StaffPolicies.ManageStaff);

        admin.MapGet("/facilities", ([FromServices] IInvitations invitations, CancellationToken ct) => invitations.FacilitiesAsync(ct));

        admin.MapPost("/", async (InviteRequest request, [FromServices] IInvitations invitations, CancellationToken ct) =>
        {
            try
            {
                var created = await invitations.CreateAsync(request.Email, request.Role, request.FacilityId, ct);
                return created is null ? Results.NotFound() : Results.Created($"/v1/invitations/{created.Id}", created);
            }
            catch (ArgumentException ex) when (ex.ParamName is { } field)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [ex.Message] });
            }
        });

        admin.MapDelete("/{id:guid}", async (Guid id, [FromServices] IInvitations invitations, CancellationToken ct) =>
            await invitations.RevokeAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // Public: the token is the credential. POST keeps it out of URLs and access logs. Unknown, expired, revoked and used
        // tokens all answer 404, so the page shows one "ask your admin to resend" state and reveals nothing more.
        app.MapPost("/v1/invitations/preview", async (PreviewRequest request, HttpContext http, [FromServices] IStaffUsers users, CancellationToken ct) =>
        {
            if (InvitationToken.Read(request.Token) is not { } token) return Results.NotFound();
            await using var scope = http.RequestServices.EnterLinkTenant(token.TenantId);
            var preview = await scope.ServiceProvider.GetRequiredService<IInvitations>().PreviewAsync(request.Token, ct);
            if (preview is null) return Results.NotFound();
            var inviter = await users.GetAsync(preview.InvitedBy, ct);
            return Results.Ok(new
            {
                clinic = preview.ClinicName,
                facility = preview.FacilityName,
                inviter = inviter?.DisplayName,
                role = preview.Role,
                expiresAt = preview.ExpiresAt,
                licenceVerificationRequired = preview.LicenceVerificationRequired,
                // Exactly the role's column of the role-permission matrix.
                capabilities = RoleMatrix.Rows.Select(r => new CapabilityAccess(r.Capability, RoleMatrix.Of(preview.Role, r.Capability))),
            });
        }).AllowAnonymous().RequireRateLimiting(StaffAuthentication.SignInRateLimit);

        return app;
    }
}
