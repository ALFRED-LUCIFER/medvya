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

## Consequences

Removes token theft through XSS. CSP can be strict. The portal uses the same pattern with patient OTP / UAE PASS.
