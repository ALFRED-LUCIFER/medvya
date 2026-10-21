# ADR-004 · BFF session: no tokens in the browser

- **Status:** Proposed (P0); local half implemented (MDV-P0-011)

## Decision

The SPA and API share one origin. Host.Api acts as the backend-for-frontend:
- It runs OIDC Authorization Code + PKCE with Entra External ID and keeps tokens server-side.
- It issues an HTTP-only, Secure, SameSite=Strict cookie. Antiforgery tokens are required on state-changing requests.
- Idle timeout is configurable per tenant (shared clinic PCs). Sessions can be revoked per device.
- Facility selection is part of the session context (`/v1/me/context`).

## Implementation notes (MDV-P0-011, local half)

- `Host.Api/Auth/StaffAuthentication.cs`: cookie `__Host-medvya` (HttpOnly, Secure, SameSite=Strict, 8 h maximum; session control below) plus the ASP.NET Core OpenID Connect handler (code + PKCE, query response mode, `SaveTokens = false`, so no token is stored anywhere). A sign-in is accepted only if the ID token's `amr` contains `mfa` and the provider account (issuer + subject) maps to a Medvya user in `identity.Users`; accounts come from invitations (MDV-P0-013) or the synthetic seed, never from first sign-in.
- **CSRF:** instead of antiforgery tokens, every state-changing request that carries the session cookie must send the `X-Medvya-Csrf` header. A cross-site page cannot add a custom header without a CORS preflight, which this API never grants; together with SameSite=Strict this covers CSRF without a token round-trip.
- Endpoints: `GET /bff/login?returnUrl=` (same-site paths only, rate-limited), `POST /bff/logout`, `GET /v1/me/context`. Failed sign-ins return to `/?signin=failed`; the reason is logged as a code, never with account details.
- **Development provider** (`Host.Api/Auth/DevIdentityProvider.cs`): a minimal OIDC provider listing seeded synthetic users, mapped only in Development (throws otherwise); production refuses a non-https authority. To be validated in the Entra External ID spike: the MFA claim, passkeys, the F-03 custom UI, lockout copy and recovery.

## Implementation notes (MDV-P0-012, session control)

- **Server-side sessions** (`identity.Sessions`, `Host.Api/Auth/SessionControl.cs`): sign-in creates a row; the cookie carries only its id (`sid`). Every cookie-authenticated request checks the row (`OnValidatePrincipal`, one primary-key read, no cache), so revocation, idle timeout and the 8 h cap take effect on the next request. A cookie whose tenant/membership no longer matches the row (a copy from before a facility switch) is refused. The device label is coarse ("Chrome on Windows"); no IP or raw User-Agent is stored.
- **Idle timeout:** 15 minutes by default, per tenant in `tenancy.Tenants.IdleTimeoutMinutes` (1–480; no admin UI yet), applied when the facility is chosen. Last-seen is written at most once a minute. Requests with `X-Medvya-Passive: 1` (the client's one-minute status poll of `/v1/me/context`) do not count as activity, so an unattended screen reaches its timeout and the client shows F-05a within about a minute.
- **Facility picker:** `GET /v1/me/context` returns `{ user, memberships, active }` (active = membership, role, capabilities from `RoleMatrix` without `None`, enabled tenant flags). `POST /v1/me/facility { membershipId }` accepts only the caller's own `Active` memberships, re-issues the cookie with `tenant_id`, `membership_id`, `facility_id` and `role` claims (keeping the original expiry) and needs no re-authentication. Memberships are listed across tenants through the `user_id` row-level security rule (ADR-005 note).
- **Revocation:** `GET /v1/me/sessions`, `DELETE /v1/me/sessions/{id}` (own sessions), `DELETE /v1/staff/{userId}/sessions` (policy `staff-admin`: Config on "Staff, roles, integrations"; ends that person's sessions in the admin's tenant only). `POST /bff/logout` revokes the row too.
- **Audit** (tenant-scoped, written before the change, fail closed): `auth.signin` on the first facility choice, `auth.facility.switch`, `auth.signout`, `session.revoke`. A session that never chose a facility has no tenant, so its sign-out is not audited.

## Consequences

Removes token theft through XSS. CSP can be strict. The portal uses the same pattern with patient OTP / UAE PASS.
