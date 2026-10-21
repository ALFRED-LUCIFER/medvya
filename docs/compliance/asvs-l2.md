# OWASP ASVS Level 2 checklist (MDV-P0-026)

Medvya uses **OWASP ASVS 4.0.3 Level 2** as its security checklist from the first commit (PRD v2 P0 NFR 1). This file is tracked at chapter level, with the controls that matter most for Medvya called out. Statuses:

- **Done:** in code with an automated test.
- **Planned:** the ticket that delivers it.
- **N/A:** with the reason.

Review this file at every release gate (R0 onwards). A control moves to Done only when its test exists.

| Chapter | Key controls for Medvya | Status | Evidence / ticket |
|---|---|---|---|
| **V1 Architecture & threat modelling** | Threat model per trust boundary; data classification; server-side enforcement of all controls | Partly done | Classification register + test (MDV-P0-016). Threat model MDV-P0-024 |
| **V2 Authentication** | No passwords stored by Medvya (Entra External ID); MFA for all staff; passkeys; lockout after failed OTP; recovery without account enumeration | Planned | MDV-P0-011 |
| **V3 Session management** | HTTP-only, Secure, SameSite=Strict BFF cookie; no tokens in the browser; idle timeout; revocation within 60 s | Planned | MDV-P0-011, MDV-P0-012 (ADR-004) |
| **V4 Access control** | Deny by default; every endpoint has an explicit policy; tenant + facility + role + relationship + purpose; RLS defence in depth; IDOR tests | Planned | MDV-P0-010, MDV-P0-014, MDV-P1-038 |
| **V5 Validation & encoding** | FluentValidation on every command; parameterised queries only (EF Core/Dapper); output encoding by React | Planned | Architecture test "every command has a validator" lands with the first command pipeline (MDV-P0-010/014) |
| **V6 Cryptography** | TLS 1.2+; TDE with CMK; Always Encrypted for national/insurance IDs; keys only in Key Vault | Planned | MDV-P0-006, MDV-P0-009 |
| **V7 Errors & logging** | No PHI in logs or telemetry; structured logs; security events logged; generic error responses (Problem Details) | Planned | MDV-P0-018, MDV-P0-017 |
| **V8 Data protection** | Sensitive data not cached client-side; no PHI in localStorage; masking for support roles; retention | Partly done | Clinic stores only the UI language locally (`Root.tsx`). Masking MDV-P0-009, retention MDV-P5-12 |
| **V9 Communications** | HTTPS only; HSTS (1 year, includeSubDomains); private endpoints for data | **Done** (HSTS) / Planned | `SecurityBaselineTests.ResponsesCarrySecurityHeaders`. Private networking MDV-P0-006 |
| **V10 Malicious code** | Dependency and secret scanning; no unreviewed security-sensitive AI-written code | Partly done | `tools/check-synthetic`, gitleaks + vulnerable-package checks (MDV-P0-004, waiting on `workflow` scope), review rules in `docs/compliance/secure-development.md` |
| **V11 Business logic** | Rate limits on abuse-prone flows; anti-automation on booking/OTP; idempotent money and messaging | **Done** (global limit) / Planned | `ExcessRequestsFromOneClientGet429` (per-IP limit, default 300/min). Stricter sign-in/OTP limits MDV-P0-011. Idempotency P1/P4 |
| **V12 Files & resources** | Size limits; malware scan; private storage; time-limited links; no user-controlled paths | **Done** (body limit) / Planned | `OversizedBodiesAreRejected` (1 MiB API body limit; files go direct to Blob). Scanning and links MDV-P2-006 |
| **V13 API** | Same-origin BFF; antiforgery on state-changing calls; OpenAPI contract; no GET side effects | Planned | MDV-P0-011 (BFF/antiforgery) |
| **V14 Configuration** | Security headers; server banner removed; no secrets in config; dependency pinning | **Done** (headers) | `SecurityBaselineTests`: CSP (no inline/eval scripts), X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, COOP, no `Server` header. Central package versions + Dependabot |

## Content Security Policy

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:;
font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
```

`style-src 'unsafe-inline'` is a known exception: MUI/Emotion injects `<style>` tags at runtime. Scripts stay strict. Removing the exception means an Emotion nonce per response, which is worth doing once the SPA is served by Host.Api (MDV-P0-011). Fonts are self-hosted (MDV-P0-021), so `font-src 'self'` needs no third-party domains.
