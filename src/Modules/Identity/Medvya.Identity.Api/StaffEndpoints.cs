using Medvya.Tenancy.Api;
using System.Security.Claims;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Identity.Api;

/// <summary>
/// Settings → Staff (F-10, MDV-P1-008): the staff list, adding staff through the P0 invitation flow, resending an invitation,
/// and practice-defined roles. Every endpoint needs the staff capability (<see cref="StaffPolicies.ManageStaff"/>), so any other
/// role gets 403 whatever the screen shows. Revoking an invitation reuses DELETE /v1/invitations/{id}.
/// </summary>
public static class StaffEndpoints
{
    public sealed record AddStaff(string Email, Role Role, Guid? CustomRoleId, Guid FacilityId, IReadOnlyList<Guid>? AlsoFacilityIds = null);
    /// <param name="OfferRebooking">True: the doctor's moved patients get the rebooking message; false: reception calls them (#225).</param>
    public sealed record Offboard(string? Reason, bool OfferRebooking = false);
    public sealed record Reactivate(string? Reason);
    /// <param name="CustomRoleId">A practice-defined role on <paramref name="Role"/>; null for the built-in role.</param>
    public sealed record ChangeRole(Role? Role, Guid? CustomRoleId);

    /// <summary>How long an identity step-up counts for an offboarding (MDV-P1-107): the same five minutes as signing a note.</summary>
    public static readonly TimeSpan StepUpWindow = TimeSpan.FromMinutes(5);
    /// <param name="FacilityIds">Every location the member works at afterwards; one left out is taken away and needs <paramref name="Reason"/>.</param>
    public sealed record SetLocations(IReadOnlyList<Guid>? FacilityIds, string? Reason);
    public sealed record Row(Capability Capability, Access Access, Access? Extra);
    public sealed record BuiltInRole(Role Role, IReadOnlyList<Row> Rows);

    public static WebApplication MapStaff(this WebApplication app)
    {
        var g = app.MapGroup("/v1/staff").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(MapErrors);

        g.MapGet("/", async ([FromServices] IStaffRoster roster, [FromServices] IStaffUsers users, CancellationToken ct) =>
        {
            var rows = await roster.ListAsync(ct);
            var names = await users.DisplayNamesAsync([.. rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value)], ct);
            return rows.Select(r => new
            {
                r.MembershipId, r.UserId,
                Name = r.UserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                r.Email, r.Role, r.CustomRoleId, r.CustomRoleName, r.FacilityId, r.FacilityName,
                Status = r.Status.ToString(),
                // Sign-in refuses an account without MFA (StaffAuthentication), so anyone who has signed in is enrolled.
                Mfa = r.UserId is null ? "pending" : "enrolled",
                r.InvitationId, r.InvitationState, r.InvitationExpiresAt,
                r.IsOwner, // MDV-P1-107: the screen offers Offboard on an owner's or a doctor's row only to an owner; the server decides again
            });
        });

        g.MapPost("/", async (AddStaff body, [FromServices] IInvitations invitations, CancellationToken ct) =>
        {
            // Doctors are added with licence verification (Settings → Doctors); no membership is created here.
            if (body.Role is Role.SoloDoctor or Role.HospitalDoctor) return Problem("role", "doctor_redirect");
            if (!CustomRoleRules.Assignable.Contains(body.Role)) return Problem("role", "invalid");
            try
            {
                var created = await invitations.CreateAsync(body.Email, body.Role, body.FacilityId, body.CustomRoleId, body.AlsoFacilityIds, ct);
                return created is null ? Results.NotFound() : Results.Created($"/v1/invitations/{created.Id}", created);
            }
            catch (ArgumentException ex) when (ex.ParamName is { } field)
            {
                return Problem(field, "invalid");
            }
        });

        g.MapPost("/invitations/{id:guid}/resend", async (Guid id, [FromServices] IInvitations invitations, CancellationToken ct) =>
            await invitations.ResendAsync(id, ct) is { } created ? Results.Ok(created) : Results.NotFound());

        // Quarterly access review (MDV-P1-010): every active member with role and last sign-in; confirm each, or offboard (the endpoint below).
        g.MapGet("/access-review", async (ClaimsPrincipal principal, [FromServices] IAccessReviews reviews, [FromServices] IStaffUsers users, [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            var review = await reviews.CurrentAsync(ct);
            var ids = review.Members.Select(m => m.UserId).Distinct().ToList();
            var names = await users.DisplayNamesAsync(ids, ct);
            var signIns = await sessions.LastSignInAsync(ids, Guid.Parse(principal.FindFirstValue(StaffPolicies.TenantClaim)!), ct);
            return new
            {
                review.Quarter, review.CompletedAt, review.LastCompletedAt,
                Members = review.Members.Select(m => new
                {
                    m.MembershipId, Name = names.GetValueOrDefault(m.UserId), m.Role, m.FacilityName,
                    LastSignIn = signIns.TryGetValue(m.UserId, out var at) ? at : (DateTimeOffset?)null,
                    Decision = m.Decision?.ToString(),
                }),
            };
        });
        g.MapPost("/access-review/{membershipId:guid}/confirm", async (Guid membershipId, [FromServices] IAccessReviews reviews, CancellationToken ct) =>
            await reviews.ConfirmAsync(membershipId, ct) ? Results.NoContent() : Results.NotFound());

        // Offboarding (MDV-P1-009): access ends in the roster transaction (the membership is no longer active, so every endpoint refuses it
        // and the facility picker no longer offers it); the person's sessions on that membership end right after. Their other facilities and
        // other tenants are untouched (#877).
        g.MapPost("/{membershipId:guid}/offboard", async (Guid membershipId, Offboard body, ClaimsPrincipal principal, [FromServices] IStaffRoster roster,
            [FromServices] IStaffSessions sessions, [FromServices] IAccessReviews reviews, [FromServices] Medvya.BuildingBlocks.Domain.ICurrentUser me,
            [FromServices] TimeProvider clock, CancellationToken ct) =>
        {
            // MDV-P1-107: ending someone's access asks for the identity step-up first, as signing does. SAFETY: mutation-checked in SessionControlTests.
            var now = clock.GetUtcNow();
            if (me.ReauthenticatedAt is not { } at || now - at > StepUpWindow || at > now.AddMinutes(1))
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Confirm your identity to offboard", extensions: new Dictionary<string, object?> { ["code"] = "reauth_required" });
            // A doctor's future visits go to reception's "needs rebooking" lane inside the same transaction (IStaffOffboardingParticipant).
            var result = await roster.OffboardAsync(membershipId, body.Reason, body.OfferRebooking, ct);
            // shortcut: recorded after the offboarding commits (not one transaction); if it fails the offboarding stands and the owner is asked about that person only if they were still active.
            if (result.Outcome == OffboardOutcome.NotFound) return Results.NotFound();
            if (result.Outcome == OffboardOutcome.LastAdmin) return Problem("membershipId", "last_admin");
            if (result.Outcome == OffboardOutcome.LastOwner) return Problem("membershipId", "last_owner");
            if (result.Outcome == OffboardOutcome.NotOwner) return OwnersOnly;
            if (result.UserId is { } userId)
            {
                var tenantId = Guid.Parse(principal.FindFirstValue(StaffPolicies.TenantClaim)!);
                // Every location the person leaves (MDV-P1-071).
                foreach (var id in result.MembershipIds ?? [membershipId])
                {
                    await reviews.RecordOffboardedAsync(id, ct); // part of this quarter's access review (MDV-P1-010)
                    // Browser sessions and the person's phones alike: a native refresh token is a session row, so it stops on its next refresh.
                    await sessions.RevokeForMembershipAsync(userId, tenantId, id, ct);
                }
            }
            return Results.Ok(new { result.OpenVisits }); // patients already checked in for them, for reception to reassign on the board
        });

        // Locations (MDV-P1-071): the owner sets where a member works. Taking one away ends their access there and their sessions on it.
        g.MapPut("/{membershipId:guid}/locations", async (Guid membershipId, SetLocations body, ClaimsPrincipal principal, [FromServices] IStaffRoster roster,
            [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            var result = await roster.SetLocationsAsync(membershipId, body.FacilityIds ?? [], body.Reason, ct);
            // Also when a later removal was refused: the ones already taken away must not keep a session.
            if (result.UserId is { } user)
                foreach (var gone in result.Removed ?? [])
                    await sessions.RevokeForMembershipAsync(user, Guid.Parse(principal.FindFirstValue(StaffPolicies.TenantClaim)!), gone, ct);
            return result.Outcome switch
            {
                LocationsOutcome.Done => Results.NoContent(),
                LocationsOutcome.AboveGranter => Problem("membershipId", "above_granter"),
                LocationsOutcome.LastAdmin => Problem("membershipId", "last_admin"),
                LocationsOutcome.NotOwner => OwnersOnly,
                _ => Results.NotFound(),
            };
        });

        // Re-activation (MDV-P1-009, #225): an offboarded member works again with the same role; they sign in again (no session is restored).
        g.MapPost("/{membershipId:guid}/reactivate", async (Guid membershipId, Reactivate body, [FromServices] IStaffRoster roster, CancellationToken ct) =>
            await roster.ReactivateAsync(membershipId, body.Reason, ct) switch
            {
                ReactivateOutcome.Done => Results.NoContent(),
                ReactivateOutcome.AboveGranter => Problem("membershipId", "above_granter"),
                _ => Results.NotFound(),
            });

        // Change role in place (MDV-P1-107): the next request has the new role's access (roles are read live), and the person's sessions on that
        // membership end so no screen keeps showing the old one.
        g.MapPut("/{membershipId:guid}/role", async (Guid membershipId, ChangeRole body, ClaimsPrincipal principal, [FromServices] IStaffRoster roster,
            [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            if (body.Role is not { } role || !Enum.IsDefined(role)) return Problem("role", "required");
            var result = await roster.ChangeRoleAsync(membershipId, role, body.CustomRoleId, ct);
            if (result.Outcome == ChangeRoleOutcome.Done && result.UserId is { } user)
                await sessions.RevokeForMembershipAsync(user, Guid.Parse(principal.FindFirstValue(StaffPolicies.TenantClaim)!), membershipId, ct);
            return result.Outcome switch
            {
                ChangeRoleOutcome.Done => Results.NoContent(),
                ChangeRoleOutcome.NotAssignable => Problem("role", "not_assignable"),
                ChangeRoleOutcome.AboveGranter => Problem("role", "above_granter"),
                ChangeRoleOutcome.LastAdmin => Problem("membershipId", "last_admin"),
                _ => Results.NotFound(),
            };
        });

        // What each assignable role holds (locked: from the role-permission matrix) and which rows a custom role may add.
        g.MapGet("/roles", async ([FromServices] IStaffRoster roster, CancellationToken ct) => Results.Ok(new
        {
            builtIn = CustomRoleRules.Assignable.Select(role => new BuiltInRole(role, [.. RoleMatrix.Rows.Select(r => new Row(r.Capability, RoleMatrix.Of(role, r.Capability),
                CustomRoleRules.Extras(role).Contains(r.Capability) ? RoleMatrix.Of(Role.PracticeAdmin, r.Capability) : null))])),
            custom = await roster.RolesAsync(ct),
        }));
        g.MapPost("/roles", async ([FromBody] SaveCustomRole body, [FromServices] IStaffRoster roster, CancellationToken ct) =>
            Results.Created("/v1/staff/roles", await roster.CreateRoleAsync(body, ct)));
        g.MapPut("/roles/{id:guid}", async (Guid id, [FromBody] SaveCustomRole body, [FromServices] IStaffRoster roster, CancellationToken ct) =>
            await roster.UpdateRoleAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        return app;
    }

    static readonly IResult OwnersOnly = Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an owner can do this",
        extensions: new Dictionary<string, object?> { ["code"] = "owner_only" });

    static IResult Problem(string field, string code) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [code] });

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
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
            return SettingsEndpoints.EmailFailed;
        }
        catch (InvitationLimitException)
        {
            return SettingsEndpoints.TooManyInvitations;
        }
        catch (PlanLimitException)
        {
            return SettingsEndpoints.PlanLimit;
        }
    }
}
