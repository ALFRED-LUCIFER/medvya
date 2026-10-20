# ADR-004 · BFF session: no tokens in the browser

- **Status:** Proposed (P0)

## Decision

The SPA and API share one origin. Host.Api acts as the backend-for-frontend:
- It runs OIDC Authorization Code + PKCE with Entra External ID and keeps tokens server-side.
- It issues an HTTP-only, Secure, SameSite=Strict cookie. Antiforgery tokens are required on state-changing requests.
- Idle timeout is configurable per tenant (shared clinic PCs). Sessions can be revoked per device.
- Facility selection is part of the session context (`/v1/me/context`).

## Consequences

Removes token theft through XSS. CSP can be strict. The portal uses the same pattern with patient OTP / UAE PASS.
