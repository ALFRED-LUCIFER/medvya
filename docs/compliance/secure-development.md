# Secure development policy (MDV-P0-025)

Applies to everyone who writes or reviews Medvya code, including AI coding assistants. Checklist source: OWASP ASVS Level 2 (MDV-P0-026).

## 1 · Synthetic data only

- Real patient data never enters the repository, CI logs, test fixtures, screenshots, issue trackers or AI evaluation sets.
- Use the synthetic cast in [docs/00-overview/personas.md](../00-overview/personas.md). Mark seeded records `SYNTHETIC`.
- National IDs in fixtures must be **invalid by construction**: Aadhaar-like numbers must fail the Verhoeff check, and Emirates-ID-like numbers must fail the Luhn check. `node tools/check-synthetic/check.mjs` enforces this on every PR and fails the build on a checksum-valid ID.
- Production data is never copied to dev or staging. Debugging production uses audited support access (MDV-P5-08), not data exports.

## 2 · Code review rules

- Every change goes through a pull request with the Definition-of-Done checklist (`.github/pull_request_template.md`).
- **Security-sensitive code is reviewed line by line by a human**, even when an AI assistant wrote it. That covers authentication, authorisation policies, tenant isolation (RLS, query filters), encryption, audit, consent, payment webhooks, file upload and anything that builds SQL or shell commands.
- AI coding tools are welcome for tests, migrations and CRUD (Build Plan advice). Their output is reviewed like any other code.
- No secrets in code or config. Names go in `.env.example`; values live in Key Vault (cloud) or local user secrets.
- New dependencies need a reason in the PR. Prefer the standard library and existing packages.

## 3 · Privileged access: just-in-time only

Nobody holds standing Owner/Contributor rights on production subscriptions or admin rights on production databases.

Procedure (applies once Azure exists, MDV-P0-006):
1. Eligible roles are assigned through **Microsoft Entra Privileged Identity Management (PIM)**, with a maximum activation of 4 hours, a required justification and a ticket reference.
2. Activating production database admin requires MFA and approval by a second person where one exists. A solo founder records a self-approval note in the incident/ops log.
3. Every activation is reviewed weekly in the access log.
4. If PIM licensing is not available, keep the role unassigned. Assign it manually for a time-boxed task and remove it immediately after, recording both steps in the ops log.

## 4 · Offline break-glass account

- One cloud-only emergency account per country tenant, excluded from conditional access policies only as documented in Entra.
- The password (long random) and a FIDO2 key are stored **offline**: a sealed envelope plus a hardware key in a safe, with a second copy held by a trusted person.
- Any sign-in by this account raises an alert to the founder and the backup contact (Azure Monitor alert on its sign-in logs).
- Tested every 6 months: sign in, confirm the alert fired, rotate the credential, reseal.

## 5 · Classification

Every stored field has a class (public, internal, personal, sensitive health, national ID) that drives its controls. See [docs/03-architecture/data-model.md](../03-architecture/data-model.md#data-classification-p0-10). The full register is MDV-P0-016.
