# Threat model (MDV-P0-024)

Method: STRIDE per trust boundary (Spoofing, Tampering, Repudiation, Information disclosure, Denial of service, Elevation of privilege). Scope: P0–P3 architecture (`docs/03-architecture`); B8 adds the P1 entry points (MDV-P1-038). Review at every release gate and whenever a new trust boundary appears (AI worker P3, integrations P5/P7, clip P6).

Status column: **Done** = implemented and tested; otherwise the ticket that delivers the mitigation.

## Trust boundaries

```
[Browser SPA] ──TLS──▶ [Host.Api BFF] ──private endpoint──▶ [Azure SQL]   [Blob (CMK)]   [Key Vault]
     │                    │      └──▶ [Entra External ID]
     │                    ├──▶ [Hangfire jobs] ──▶ [SendGrid / SMS]          (P1)
     │                    └──▶ [Service Bus] ──▶ [AI worker] ──▶ [Azure OpenAI / Speech, in region]   (P3)
[Phone/clip] ──TLS + scoped SAS──▶ [Blob]                                       (P3/P6)
[Queue TV] ──display token──▶ [Host.Api]                                        (P1)
[Staff / patient app] ──TLS + bearer (native session)──▶ [Host.Api]            (P5, ADR-011)
```

## B1 · Browser → Host.Api (BFF)

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Stolen password or OTP; shared clinic PC left signed in | Entra External ID with MFA (passkeys preferred); idle timeout; device/session revocation | MDV-P0-011, MDV-P0-012 |
| S | Token theft via XSS | BFF: tokens server-side, HTTP-only Secure SameSite=Strict cookie; strict CSP (no inline scripts) | CSP **Done** (P0-026); BFF MDV-P0-011 |
| T | CSRF on state-changing calls | Antiforgery tokens + SameSite=Strict | MDV-P0-011 |
| R | Staff deny opening a record or making a change | Append-only audit (ledger) of every patient read/write with actor, purpose, time and outcome (Allowed; Denied for 401/403; NotFound for 404; Rejected for 400/409/422, codes only), one rule for reads and writes; a write's event commits in the same database transaction as the change, so no committed change lacks its event and a failed audit write rolls the change back | MDV-P0-017 |
| I | IDOR: guessing patient/appointment ids; receptionist reading notes | Server-side policy on every endpoint (tenant, facility, role, relationship, purpose); RLS; IDOR test suite (`AuthorisationMatrixTests`, every route with an id) | **Done** (MDV-P0-014, MDV-P0-010, MDV-P1-038) |
| I | PHI in URLs, logs, telemetry, browser storage | No PHI in URLs (API rule); allowlist telemetry scrubbing; nothing but UI language in localStorage | Telemetry **Done** (P0-018); storage **Done** (P0-021) |
| I | Clickjacking, MIME sniffing, referrer leaks | `frame-ancestors 'none'`, X-Frame-Options DENY, nosniff, Referrer-Policy no-referrer | **Done** (P0-026) |
| D | Request floods, huge bodies | Per-IP rate limit (429; forwarded client address only from listed proxies); per-user limit on patient search; 1 MiB body limit | **Done** (P0-026, MDV-P1-018) |
| E | Role escalation via custom roles | Custom roles composed only from existing capabilities; locked role capabilities | MDV-P0-014, MDV-P1-008 |

## B2 · Host.Api → Azure SQL / Blob / Key Vault

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Stolen DB password | Entra-only auth, managed identity, no SQL logins | MDV-P0-008 |
| T | Tampering with audit, signed notes, consents, invoices | Ledger tables; digests to immutable Blob, verified on schedule | MDV-P0-017, MDV-P2-012 |
| I | Cross-tenant leakage through a missing filter | EF global filter **and** PostgreSQL FORCE row-level security on `medvya.tenant_id`; CI test proves RLS blocks even with filters off | MDV-P0-010 (P0 exit gate) |
| I | DBA or Medvya staff reading national IDs | Always Encrypted (CMK usable only by the API identity); DDM for support roles | MDV-P0-009 |
| I | Public network exposure | Private endpoints, public access disabled, deny-cross-region policy | MDV-P0-006 |
| E | Module reading another module's tables | Per-module DB users; architecture tests | MDV-P0-008 |
| D | Ransomware / accidental deletion | PITR 35 days, LTR in-country, soft delete, monthly restore drill | MDV-P0-019, MDV-P5-021 |

## B3 · Jobs and messaging → SendGrid / SMS / WhatsApp

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| I | Clinical details in SMS/email/WhatsApp previews | Templates without clinical fields (validation); generic push copy | MDV-P1-006, MDV-P1-032 |
| S | Forged delivery webhooks | Signature verification; idempotent processing | MDV-P1-033 |
| R | "We never got a reminder" | NOTIFICATION_ATTEMPT with provider reference and state | MDV-P1-032 |
| I | Processor data exposure | DPA with SendGrid / SMS provider; minimal data (name + time + clinic) | MDV-P1-044 |

## B4 · AI worker → Azure OpenAI / Speech (P3)

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| E | Worker with broad data access | No DB credentials; opaque job ids; scoped internal API; short-lived SAS | MDV-P3-007 |
| T | Prompt injection via transcripts or documents | Treat as untrusted data; JSON schema output; validators; no tools | MDV-P3-011 |
| I | Inference outside the country | In-region endpoints only; residency policy; vendor DPA | MDV-P3-001, MDV-P3-022 |
| — | Fabricated medicines/doses (clinical safety) | Evidence links required; validators; eval set CI gate; clinician signs | MDV-P3-011, MDV-P3-018 |

## B5 · Phone / clip → Blob (P3, P6)

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Recording without consent or for the wrong patient | Capture requires encounter + consent receipt; wrong-patient lock | MDV-P3-003, MDV-P3-017 |
| T | Replayed or altered chunks | Sequence numbers + SHA-256 manifest; nonce-bound sessions | MDV-P3-006, MDV-P6-003 |
| I | Lost device with buffered audio | Encrypted buffer; revoke wipes keys in < 1 min | MDV-P6-005 |

## B6 · Queue TV display

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| I | Patient names shown in the waiting room | TV payload contains tokens, rooms, doctor display names and waits only (exact-field contract test); the live push carries no data at all; the TV page loads outside the staff app and calls only its own endpoints (e2e) | **Done** (MDV-P1-031) |
| S | Anyone opening the TV URL | Revocable per-facility display token (tenant + 32 random bytes, only its SHA-256 stored), sent once in the link's fragment (never in a URL the server sees), exchanged for an HttpOnly Secure SameSite=Strict cookie limited to `/v1/queue`; checked on every request; pairing audited | **Done** (MDV-P1-031) |
| E | A display session used on staff endpoints, or a staff session on the TV | Separate authentication scheme and policy: staff endpoints take only the staff cookie, display endpoints only the display (tests both ways) | **Done** (MDV-P1-031) |
| I | A revoked TV still connected over WebSockets | It gets only empty pings until it reconnects; every refetch and reconnect is refused at once | Accepted (MDV-P1-031) |

## B7 · Staff and patient phone apps (ADR-011)

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Stolen or replayed tokens | Native session on the server-side row; 15-min access token in memory only; refresh token in secure-store, rotated on each use, a replay ends the session (audited); bearer-only native sessions, cookie-only browser sessions | **Done** for staff (MDV-P5-033, PR #844); patients MDV-P5-033 |
| S | Another app catching the sign-in redirect | PKCE S256 (a caught code is useless); allow-listed redirect URIs; verified https links before release | PKCE **Done** (PR #844); links MDV-P5-039 |
| I | **Lost phone** | Remote revoke refused on the next request; app wipes the encrypted store (including unsent audio) on the first 401; visit flagged "recording incomplete" | Server **Done** (PR #844); wipe MDV-P5-034/035 |
| I | **Shared phone** | 15-min biometric/PIN lock; one person per install; switch user = sign-out + wipe | Lock done locally (MDV-P5-034); switch user MDV-P5-034 |
| I | **Screenshot** or app-switcher preview | Staff: `FLAG_SECURE` on Android, blurred switcher and audited screenshot detection on iOS. Patient: blurred switcher | MDV-P5-034, MDV-P5-036 |
| I | **Push preview** on the lock screen | Payload = opaque id + fixed text; detail fetched after unlock; token removed on revoke | MDV-P5-038 |
| I | **Offline queue** read off the device | Encrypted SQLite, key in secure-store; staff cache today only, deleted after 24 h and at sign-out/revoke; nothing in AsyncStorage, logs or crash reports | MDV-P5-034, MDV-P5-035 |
| E | Rooted or jailbroken phone reading the store | Staff app refuses to sign in; patient app warns. Best effort, not relied on | MDV-P5-039 |
| T | Over-the-air update changing clinical logic | EAS Update for UI only; pipeline refuses clinical changes | MDV-P5-039 |
| D | Phones sharing one rate-limit bucket behind App Service | Client address from the last X-Forwarded-For entry on App Service | MDV-P0-007 (PR #845) |

## B8 · P1 entry points (MDV-P1-038 review, 7 Nov 2026)

STRIDE per entry point for the P1 features on `main`. Control = what is in code, with its file and the test that proves it. Gap = what is still open, with its ticket or issue. The authorisation and IDOR matrix for every endpoint with an id is `tests/Medvya.IntegrationTests/AuthorisationMatrixTests.cs`; review results are in [p1-security-review.md](p1-security-review.md).

| Entry point | STRIDE | Threat | Control (file · test) | Gap |
|---|---|---|---|---|
| Public site `/`, `/assist`, `/hospitals`, `/pricing`, `/trust` (EN, `/hi`, `/ar`) | I | Session cookie or tenant data on a public page | Razor pages run before authentication and never read the session cookie (`src/Host.Api/PublicSite/PublicSite.cs` `RunPublicSiteBeforeAuthentication`); no patient or tenant data in any page model | None |
| | D | Scraping, floods | Per-IP global limit (`Program.cs` `AddRateLimiter`) · `SecurityBaselineTests.PublicPagesAreRateLimitedPerClient` | In-memory per instance (MDV-P0-007 shortcut); DAST not run |
| | T | Script injection through content files | Server-rendered, Razor encodes output; strict CSP without inline script (`Middleware/SecurityHeaders.cs`) · `SecurityBaselineTests.ResponsesCarrySecurityHeaders` | `style-src 'unsafe-inline'` (accepted, below) |
| `/demo` form, `/region` waitlist | S, D | Bots, spam, mailbox flooding | Antiforgery token, honeypot, minimum fill time, per-client quota per hour with IPv6 /64 keying (`PublicSite/PublicForm.cs` `PublicFormGuard`) · `TrustCentreTests.SubmissionsAreRateLimitedPerClient`, `BotsAreTurnedAwayWithoutStoringAnything` | Quota per instance (shortcut in `PublicForm.cs`) |
| | I | Lead personal data without a notice | Notice at collection, unticked consent, notice version stored (`PublicForm.cs`, `Medvya.Leads.Domain`); form refused while no privacy contact is set | Privacy contact Development-only until MDV-P1-044; lead retention period not yet set (retention map) |
| Sign-up and set-up wizard `/bff/login?signup=true`, `/v1/onboarding*` | S | Practice created without a verified person | Entra sign-in with MFA (`amr=mfa`) before any account exists (`Auth/StaffAuthentication.cs` `OnTokenValidated`) · `StaffSignInTests.SignInWithoutMfaIsRefused` | `amr` claim to be confirmed on real Entra External ID (shortcut in `StaffAuthentication.cs`, MDV-P0-011) |
| | E, I | One user reading or finishing another's draft | Draft keyed by the signed-in user id only (`Tenancy.Api/OnboardingEndpoints.cs`); a user without membership reaches no clinic data · `OnboardingTests` | None |
| Staff sign-in `/bff/login`, `/bff/logout`, `/v1/me/*` | S | Account takeover, session theft | OIDC code + PKCE, MFA required, unknown accounts refused, tokens never kept (`SaveTokens=false`); HttpOnly Secure SameSite=Strict cookie, 8 h maximum, server-side session with idle timeout and revocation (`Auth/SessionControl.cs`) · `StaffSignInTests`, `SessionControlTests` | Stricter per-account lockout is the identity provider's (Entra) |
| | T | CSRF on state-changing calls | `X-Medvya-Csrf` header required with the cookie (`StaffAuthentication.UseStaffAuthentication`) · `StaffSignInTests.StateChangingRequestsNeedTheCsrfHeader` | None |
| | S, I | Open redirect, login-hint injection | Local return URLs only; login hint validated before the provider (`IsLoginHint`) · `ReturnUrlNeverLeavesTheSite`, `AMalformedLoginHintIsRefusedBeforeTheProvider` | The work email rides in the `/bff/login` query string (staff personal data, not PHI; query strings are scrubbed from telemetry). Accepted |
| | E | Choosing a facility the user has no membership at | `/v1/me/facility` checks the active membership; every capability is read from the membership at the session's facility, never from a claim (`Tenancy.Infrastructure/StaffAccess.cs`) · `AuthorisationMatrixTests.ASessionAtOneFacilityCannotReachAnother` (mutation-checked) | None |
| | D | Sign-in floods | `sign-in` limit per IP, 20/min (`Program.cs`) on `/bff/login`, `/native/*`, `/v1/invitations/preview` | None |
| Invitations `/v1/invitations*`, `/v1/staff/invitations/{id}/resend` | S | Guessed or replayed invitation link | 32-byte token, only its SHA-256 stored, tenant-bound, single use, 7-day expiry (`Tenancy.Domain/Invitation.cs`) · `InvitationTests` | None |
| | E, I | Another clinic's invitation revoked or resent | ManageStaff policy; tenant filter + RLS · `AuthorisationMatrixTests.AnotherTenantsIdsAnswerLikeUnknownIdsAndChangeNothing` | None |
| Staff, roles, offboarding `/v1/staff*` | E | Desk staff managing staff; custom role escalation | ManageStaff (Staff, roles, integrations = Config) on the group (`Identity.Api/StaffEndpoints.cs`); custom roles add only listed extras (`CustomRoleRules`) · `AuthorisationMatrixTests.ARoleWithoutTheCapabilityIsRefused` (mutation-checked), `RoleMatrixTests` | None |
| Practice settings `/v1/settings*` | E, T | Another clinic's locations, hours, closures or doctors changed | ManageStaff; tenant filter + RLS; unknown or foreign id is 404 · `AuthorisationMatrixTests` | None |
| Booking `/v1/slot-holds`, `/v1/appointments*`, `/v1/schedule*` | E, I | IDOR on appointment or patient ids; another facility's diary | Capability and facility checked in the use case (`Scheduling.Infrastructure/SchedulingContext.RequireAsync`); RLS · `AuthorisationMatrixTests` (cross-tenant and cross-facility rows) | None |
| | T, R | Double booking, replayed bookings | Slot holds, row locks, unique slots, `Idempotency-Key` · `NoDoubleBookingTests`, `IdempotencyKeyTests`; every change audited in its own transaction | None |
| Check-in, queue and board `/v1/appointments/{id}/check-in`, `/v1/front-desk/*`, board hub | E, I | Board of another facility or tenant; a revoked user still listening | Hub checks capability and facility on connect and on every call, group names include the tenant, per-user watch limit (`Scheduling.Api/BoardHub.cs`) · `BoardHubTests` | None |
| Queue TV `/v1/queue/tv*` | — | See B6 | Pairing limited per IP, 20/min · `SecurityBaselineTests.TvPairingHasItsOwnTighterLimit` | — |
| Reminders (email, SMS, WhatsApp) and `/v1/notifications/receipts` | I | Clinical detail in a message; message without consent | Templates carry clinic, time and name only (`Notifications.Application/MessageCatalog.cs`); consent read from receipts, deny-all default (`NotificationsModule`, `PatientReminderConsent`) · `NotificationsTests` | DLT and WhatsApp template approval (people, session-handoff) |
| | S, D | Forged or flooded delivery receipts | HMAC-SHA256 of the body in constant time, no secret = nothing accepted, 4 KiB cap, 600/min per IP (`Notifications.Api/NotificationEndpoints.cs`) · `NotificationReceiptEndpointTests`, `SecurityBaselineTests.TheDeliveryReceiptWebhookIsRateLimitedPerClient` | No timestamp in the signature, so a captured receipt can be replayed; harmless (idempotent state) |
| Access log `/v1/audit/access-log*` | E, I | Staff reading who opened which record | Audit log capability (admin, owner doctor, hospital IT at its own facility only) (`Audit.Api/AccessLogEndpoints.cs`); export watermarked, formula cells neutralised, audited · `AccessLogTests` | None |
| Native staff sign-in `/native/*` | S | Caught code, replayed refresh token | See B7 · `NativeSignInTests` | — |
| Clinical access `/v1/patients/{id}/access-requests`, `/v1/access-requests*` | E | Clinician without a care relationship opening a chart | Policy per practice; access through visit, assignment or a granted, time-limited request (`Tenancy.Application/IClinicalAccess.cs`) · `ClinicalAccessTests`, `AccessRequestEndpointTests` | Break-glass opens nothing until P7 (accepted) |
| Encounters `/v1/encounters*` | E, I | Receptionist or admin in a consultation; banner showing more than identity | Patient 360 capability on the group plus care relationship in the use case; banner carries identity fields only (`Encounters.Api/EncounterEndpoints.cs`) · `EncounterEndpointTests`, `AuthorisationMatrixTests` | None |
| Any `/v1` endpoint | E | An endpoint shipped without a policy | Deny by default: every endpoint declares a policy or is explicitly public · `EndpointAuthorizationTests`; every route with an id is in the IDOR matrix or explained · `AuthorisationMatrixTests.EveryRouteWithAnIdIsInTheMatrixOrExplained` | None |

## Residual risks accepted for now

- `style-src 'unsafe-inline'` in the CSP, for inline state-colour style attributes only (ADR-010). Remove by moving those colours to classes.
- Branch protection is enabled only once CI can be pushed (`workflow` scope); until then, review discipline only.
- Phone apps: rooted-device and iOS screenshot detection can be bypassed; unsent audio on a revoked phone is lost by design (ADR-011).
- Solo founder: limited separation of duties for privileged access. Mitigated by PIM activation logs and the break-glass procedure (`secure-development.md`).
