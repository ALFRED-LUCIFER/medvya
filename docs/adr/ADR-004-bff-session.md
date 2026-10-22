# ADR-004 · BFF session: no tokens in the browser

- **Status:** Proposed (P0); local half implemented (MDV-P0-011)

## Decision

The SPA and API share one origin. Host.Api acts as the backend-for-frontend:
- It runs OIDC Authorization Code + PKCE with Entra External ID and keeps tokens server-side.
- It issues an HTTP-only, Secure, SameSite=Strict cookie. Antiforgery tokens are required on state-changing requests.
- Idle timeout is configurable per tenant (shared clinic PCs). Sessions can be revoked per device.
- Facility selection is part of the session context (`/v1/me/context`).

## Implementation notes (MDV-P0-011, local half)

- `Host.Api/Auth/StaffAuthentication.cs`: cookie `__Host-medvya` (HttpOnly, Secure, SameSite=Strict, fixed 8 h until MDV-P0-012) plus the ASP.NET Core OpenID Connect handler (code + PKCE, query response mode, `SaveTokens = false`, so no token is stored anywhere). A sign-in is accepted only if the ID token's `amr` contains `mfa` and the provider account (issuer + subject) maps to a Medvya user in `identity.Users`; accounts come from invitations (MDV-P0-013) or the synthetic seed, never from first sign-in.
- **CSRF:** instead of antiforgery tokens, every state-changing request that carries the session cookie must send the `X-Medvya-Csrf` header. A cross-site page cannot add a custom header without a CORS preflight, which this API never grants; together with SameSite=Strict this covers CSRF without a token round-trip.
- Endpoints: `GET /bff/login?returnUrl=` (same-site paths only, rate-limited), `POST /bff/logout`, `GET /v1/me/context`. Failed sign-ins return to `/?signin=failed`; the reason is logged as a code, never with account details.
- **Development provider** (`Host.Api/Auth/DevIdentityProvider.cs`): a minimal OIDC provider listing seeded synthetic users, mapped only in Development (throws otherwise); production refuses a non-https authority. To be validated in the Entra External ID spike: the MFA claim, passkeys, the F-03 custom UI, lockout copy and recovery.

## Implementation notes (MDV-P0-013, invitations)

- An admin with `StaffRolesIntegrations` at Config (policy `staff.manage`, from the session's `tenant_id` and `role` claims, which MDV-P0-012 sets) calls `POST /v1/invitations` (email, role, facility). This creates an `Invited` membership with no user plus a `tenancy.Invitations` row (tenant RLS like every tenancy table) and emails the link `/invite/{token}`. Revoke: `DELETE /v1/invitations/{id}`. Create, revoke and accept are audited.
- **Token:** base64url of the tenant id (16 bytes) plus 32 random bytes. Only its SHA-256 is stored. It is single-use, expires after `Invitations:Lifetime` (default 7 days) and can be revoked. The tenant part lets a link be looked up inside that tenant's row-level security: `RequestContext.EnterLinkTenant` opens a fresh DI scope bound to that tenant, so the request's own tenant never changes.
- `POST /v1/invitations/preview` is public and rate-limited. The token travels in the body, never in an API URL. Unknown, expired, revoked and used tokens all answer 404.
- **Join:** `GET /bff/login?invitation=` puts the token in the protected OIDC state. After MFA, `InvitationSignIn` checks the invitation, creates the Medvya user if the provider account is new, then in one tenancy transaction marks the invitation used and activates the membership (a conditional update, so only one acceptance can win). If the invitation cannot be used, the sign-in is refused.
- **Email:** `IEmailSender` (BuildingBlocks) uses System.Net.Mail over SMTP: smtp4dev locally (`localhost:2525`), SendGrid's SMTP relay in the cloud (`smtp.sendgrid.net:587`, STARTTLS, user `apikey`, the API key in Key Vault as `Email__Smtp__Password`). The email holds the clinic, role, facility, link and expiry only, never patient data. Links use the configured `Invitations:LinkBaseUrl`, never the request's Host header.
- Known limits: the link is a bearer credential (the provider's verified email is not yet matched to the invited address; revisit with Entra), invitations go by email only (SMS: MDV-P1-033), and the email is in English only (MDV-P1-032).

## Consequences

Removes token theft through XSS. CSP can be strict. The portal uses the same pattern with patient OTP / UAE PASS.
