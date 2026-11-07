# ADR-004 · BFF session: no tokens in the browser

- **Status:** Accepted 6 Nov 2026 (founder). (P0); local half implemented (MDV-P0-011)

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

## Implementation notes (MDV-P0-013, invitations)

- An admin with `StaffRolesIntegrations` at Config (policy `staff.manage`, from the session's `tenant_id` and `role` claims, which MDV-P0-012 sets) calls `POST /v1/invitations` (email, role, facility). This creates an `Invited` membership with no user plus a `tenancy.Invitations` row (tenant RLS like every tenancy table) and emails the link `/invite/{token}`. Revoke: `DELETE /v1/invitations/{id}`. Create, revoke and accept are audited.
- **Token:** base64url of the tenant id (16 bytes) plus 32 random bytes. Only its SHA-256 is stored. It is single-use, expires after `Invitations:Lifetime` (default 7 days) and can be revoked. The tenant part lets a link be looked up inside that tenant's row-level security: `RequestContext.EnterLinkTenant` opens a fresh DI scope bound to that tenant, so the request's own tenant never changes.
- `POST /v1/invitations/preview` is public and rate-limited. The token travels in the body, never in an API URL. Unknown, expired, revoked and used tokens all answer 404.
- **Join:** `GET /bff/login?invitation=` puts the token in the protected OIDC state. After MFA, `InvitationSignIn` checks the invitation, creates the Medvya user if the provider account is new, then in one tenancy transaction marks the invitation used and activates the membership (a conditional update, so only one acceptance can win). If the invitation cannot be used, the sign-in is refused.
- **Email:** `IEmailSender` (BuildingBlocks) uses System.Net.Mail over SMTP: smtp4dev locally (`localhost:2525`), SendGrid's SMTP relay in the cloud (`smtp.sendgrid.net:587`, STARTTLS, user `apikey`, the API key in Key Vault as `Email__Smtp__Password`). The email holds the clinic, role, facility, link and expiry only, never patient data. Links use the configured `Invitations:LinkBaseUrl`, never the request's Host header.
- Known limits: the link is a bearer credential (the provider's verified email is not yet matched to the invited address; revisit with Entra), invitations go by email only (SMS: MDV-P1-033), and the email is in English only (MDV-P1-032).

## Implementation notes (MDV-P5-033, native staff app)

The staff app has no browser to hold a cookie, so it gets bearer tokens. They are backed by the **same server-side session row**. As a result revocation, "My devices", the facility picker and offboarding (`RevokeInTenantAsync`) apply unchanged, and the next request after a revoke is refused. The code is in `Host.Api/Auth/NativeAuthentication.cs`.

- **Sign-in:** the app opens the system browser at `GET /native/login?redirect_uri=&code_challenge=&code_challenge_method=S256`. The redirect URI must be listed in `Native:RedirectUris` (`medvya-clinician://auth`), and only S256 is accepted. The normal OIDC sign-in then runs with `prompt=login`, so a browser that is still signed in never hands a phone a session silently. MFA, the account check and invitations stay as above. `/native/complete` marks the session native, clears the cookie, and redirects to the app with a one-time code. The code is data-protected, lasts 2 minutes, and is bound to the challenge and redirect URI.
- **Tokens:** `POST /native/token` takes OAuth form fields: `authorization_code` with `code_verifier` and `redirect_uri`, or `refresh_token`. It answers `{access_token, token_type, expires_in: 900, refresh_token}` with `Cache-Control: no-store`.
  - The access token is data-protected, lasts 15 minutes, and carries the same claims as the cookie.
  - The refresh token is base64url(session id ‖ user id ‖ 32 random bytes). Only its SHA-256 is stored, in `identity.Sessions.RefreshTokenHash`, so it survives restarts and key rotation. A session with that hash is native.
  - The bearer scheme is selected through the cookie's `ForwardDefaultSelector`, so every staff endpoint accepts it without per-endpoint changes. Bearer requests carry no cookie, so no CSRF header is needed.
- **Rotation and replay:** each refresh swaps the stored hash with a conditional update, so of two uses of one token only one wins. Presenting an old refresh token, or a used code, ends the whole session (RFC 9700) and audits `session.refresh.replay` when the session has a tenant. The app must therefore run one refresh at a time; two parallel refreshes with the same token end the session.
- **One kind per session:** a native session is refused by cookie and a browser session by bearer. A cookie copied during sign-in is therefore useless.
- **Lifetimes (accepted by the founder, 7 Nov 2026; ADR-011):** native sessions last at most 30 days and end after 7 days unused. The app's own 15-minute biometric/PIN lock (MDV-P5-034) covers the shared or lost phone in between. Browser sessions keep 8 h and the tenant's idle timeout.
- **Facility switch:** `POST /v1/me/facility` with a bearer re-scopes the row and returns 204. The old access token then names the wrong membership and is refused, so the app refreshes. A refresh whose membership is no longer active fails.
- Known limits:
  - Custom-scheme redirects can be claimed by another app (PKCE keeps the code useless to it). Move to Universal Links and App Links before store release (MDV-P5-039).
  - Patient SMS sign-in, the device key and the push-token registry are later slices of MDV-P5-033, because no patient accounts exist yet.
  - SignalR from the app is not wired (the board is web only).

## Consequences

Removes token theft through XSS. CSP can be strict. The portal uses the same pattern with patient OTP / UAE PASS.
