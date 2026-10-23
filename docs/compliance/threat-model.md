# Threat model (MDV-P0-024)

Method: STRIDE per trust boundary (Spoofing, Tampering, Repudiation, Information disclosure, Denial of service, Elevation of privilege). Scope: P0–P3 architecture (`docs/03-architecture`). Review at every release gate and whenever a new trust boundary appears (AI worker P3, integrations P5/P7, clip P6).

Status column: **Done** = implemented and tested; otherwise the ticket that delivers the mitigation.

## Trust boundaries

```
[Browser SPA] ──TLS──▶ [Host.Api BFF] ──private endpoint──▶ [Azure SQL]   [Blob (CMK)]   [Key Vault]
     │                    │      └──▶ [Entra External ID]
     │                    ├──▶ [Hangfire jobs] ──▶ [SendGrid / SMS]          (P1)
     │                    └──▶ [Service Bus] ──▶ [AI worker] ──▶ [Azure OpenAI / Speech, in region]   (P3)
[Phone/clip] ──TLS + scoped SAS──▶ [Blob]                                       (P3/P6)
[Queue TV] ──display token──▶ [Host.Api]                                        (P1)
```

## B1 · Browser → Host.Api (BFF)

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Stolen password or OTP; shared clinic PC left signed in | Entra External ID with MFA (passkeys preferred); idle timeout; device/session revocation | MDV-P0-011, MDV-P0-012 |
| S | Token theft via XSS | BFF: tokens server-side, HTTP-only Secure SameSite=Strict cookie; strict CSP (no inline scripts) | CSP **Done** (P0-026); BFF MDV-P0-011 |
| T | CSRF on state-changing calls | Antiforgery tokens + SameSite=Strict | MDV-P0-011 |
| R | Staff deny opening a record or making a change | Append-only audit (ledger) of every patient read/write with actor, purpose, time and outcome (Allowed; Denied for 401/403; NotFound for 404; Rejected for 400/409/422, codes only), one rule for reads and writes; a write's event commits in the same database transaction as the change, so no committed change lacks its event and a failed audit write rolls the change back | MDV-P0-017 |
| I | IDOR: guessing patient/appointment ids; receptionist reading notes | Server-side policy on every endpoint (tenant, facility, role, relationship, purpose); RLS; IDOR test suite | MDV-P0-014, MDV-P0-010, MDV-P1-038 |
| I | PHI in URLs, logs, telemetry, browser storage | No PHI in URLs (API rule); allowlist telemetry scrubbing; nothing but UI language in localStorage | Telemetry **Done** (P0-018); storage **Done** (P0-021) |
| I | Clickjacking, MIME sniffing, referrer leaks | `frame-ancestors 'none'`, X-Frame-Options DENY, nosniff, Referrer-Policy no-referrer | **Done** (P0-026) |
| D | Request floods, huge bodies | Per-IP rate limit (429; forwarded client address only from listed proxies); per-user limit on patient search; 1 MiB body limit | **Done** (P0-026, MDV-P1-018) |
| E | Role escalation via custom roles | Custom roles composed only from existing capabilities; locked role capabilities | MDV-P0-014, MDV-P1-008 |

## B2 · Host.Api → Azure SQL / Blob / Key Vault

| STRIDE | Threat | Mitigation | Status |
|---|---|---|---|
| S | Stolen DB password | Entra-only auth, managed identity, no SQL logins | MDV-P0-008 |
| T | Tampering with audit, signed notes, consents, invoices | Ledger tables; digests to immutable Blob, verified on schedule | MDV-P0-017, MDV-P2-012 |
| I | Cross-tenant leakage through a missing filter | EF global filter **and** SQL RLS on `SESSION_CONTEXT`; CI test proves RLS blocks even with filters off | MDV-P0-010 (P0 exit gate) |
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
| I | Patient names shown in the waiting room | TV payload contains tokens, rooms and waits only (contract test) | MDV-P1-031 |
| S | Anyone opening the TV URL | Revocable per-facility display token | MDV-P1-031 |

## Residual risks accepted for now

- `style-src 'unsafe-inline'` in the CSP, for inline state-colour style attributes only (ADR-010). Remove by moving those colours to classes.
- Branch protection is enabled only once CI can be pushed (`workflow` scope); until then, review discipline only.
- Solo founder: limited separation of duties for privileged access. Mitigated by PIM activation logs and the break-glass procedure (`secure-development.md`).
