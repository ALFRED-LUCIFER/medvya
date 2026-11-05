# Session handoff — 4 Nov 2026

For the next person or session. No secrets or subscription ids here; those live in Azure and the GitHub `dev` environment.

## State of `main`
- Merged today: sign-up and onboarding wizard (P1-004), practice settings (P1-005), consents (P1-019), email/SMS reminders (P1-032/033), India dev environment in Bicep (P0-003/006), tagged deploy with GitHub OIDC (P0-007), Gherkin criteria for all 223 tickets plus `tools/tickets/gh_issues.py`.
- Tickets: 22 Done; the rest Partly done, Blocked or To do (see `tickets/README.md`).
- GitHub issues exist for every ticket (223), epic (48) and open chunk. Source of truth stays `tickets/_source` + `tickets/_source/gherkin`; resync with `python3 -I tools/tickets/gh_issues.py --apply --refresh [--only=MDV-P1-007]`. Never retry create calls (it duplicated 8 issues once). Chunk issues for P1-004/005/032/033 are stale: their Gherkin `@done/@todo` tags were not updated.
- Open PRs: only Dependabot bumps (#669–#676); #671 is a major (`@hookform/resolvers` 5→7) and needs a deliberate upgrade with a browser check.

## Azure (India dev, synthetic data only)
- Resource group `rg-medvya-dev-in`, Central India: web app `app-medvya-dev-3afs7i` (F1), Azure SQL `sql-medvya-dev-3afs7i` (free offer, Entra-only), Key Vault `kv-medvya-dev-3afs7i`, storage, Application Insights, 1,800 INR/month budget.
- Deploy: push a `v*` tag (workflow `deploy-dev`, environment `dev`, OIDC). `v0.0.1` deployed and `/health` returns 200.
- Sign-in: Entra app "Medvya Clinic (dev, India)"; its secret is only in Key Vault (`oidc-client-secret`), read by a Key Vault reference.
- Not done: database schema, SQL roles and Hangfire tables are NOT applied in Azure, so the deployed API only proves `/health`. Real Entra sign-in is untested. No private endpoints, Defender, staging slot or web app hosting (shortcuts in `infra/bicep/main.bicep`).
- Check: the budget currency (INR vs USD) in Cost Management.

## Next tickets, in order
1. **P0-008 / P0-009 slice: make the Azure database real.** Apply migrations and `infra/sql/roles.sql` on deploy with the web app's managed identity, passwordless `Encrypt=Strict` connection string, Hangfire schema. Without it nothing else works in the cloud.
2. **P0-011 on Azure:** sign in through the real Entra app and test MFA/`amr` handling (see the `shortcut:` in `StaffAuthentication`).
3. **P0-019:** backups, point-in-time restore and a timed restore drill (To do; now possible). Then **P0-029** R0 gate.
4. **Practice setup:** P1-007 add doctor, P1-008 add staff, P1-009 offboarding, P1-035 access log.
5. **Resilience basics from the India connectivity research:** P1-047/048 (idempotency keys, timeouts, retry) — proposed, not yet in the ticket sources; add them first.
6. **Desk:** P1-012 leave and exceptions, P1-026 recurring, P1-017 merge review, P1-036 tablet views, P1-037 load test (run on Azure).

## Needs a person (not code)
Legal review of consent wording (P1-044); India DLT sender and templates (P1-045); pilot agreements (P1-043); ADR sign-off 001/003–006 (P0-002); clinical advisor sign-off on the journeys (P0-023); Founders Hub decision and Azure OpenAI / Speech region check (P0-003); UAE regulator applications (P1-046, later).

## Known gaps and risks
- Consent wording is DRAFT (`2026-11-draft`); hi/ar text is unreviewed. Tenant ToS/DPA acceptance and a screen to change a consent are not built.
- Reminders respect consent through `IPatientConsents`; the SMS provider and SendGrid receipt parser are not built.
- Several new screens were checked in a browser only against mocked APIs (onboarding) or not at all (reminder panel, Email consent switch).
- Hangfire runs inside Host.Api, not a worker, and only creates tables in Development.
- Connectivity: no full offline mode; see `docs/03-architecture/india-connectivity-power.md` (research, partly unverified, ticket numbers proposed).

## Working rules that bit us
`--admin` merges are blocked; update-branch then wait for CI. macOS `sed -i` needs an empty suffix. GitHub's OIDC subject includes numeric ids, so federated credentials must use that form. Agents building in parallel conflict on i18n json, migrations and `tickets/README.md`: merge one PR at a time and regenerate tickets with `build.py`.
