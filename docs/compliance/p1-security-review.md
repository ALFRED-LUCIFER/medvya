# P1 security review (MDV-P1-038)

Status: **groundwork done, external review and DAST open.** Date of this pass: 7 Nov 2026, on `main` at `5a8ae31` plus this branch. It covers the P1 clinic flow on `main`: public site, sign-up and set-up wizard, staff sign-in (BFF, login hint, invitations, facility choice), booking, check-in, queue, board and TV, reminders, the access log, native staff sign-in, clinical access and encounters.

Linked from the R1 release notes when they are written. The threat model is [threat-model.md](threat-model.md) (B8 has the P1 entry points) and the checklist is [asvs-l2.md](asvs-l2.md).

## What was checked

| Check | Result |
|---|---|
| Threat model | B8 added: STRIDE per P1 entry point, the control with its file and test, and the gap |
| ASVS 4.0.3 L2 | V1–V14 refreshed with evidence; open gaps listed per chapter |
| Endpoint inventory | 119 operations in `packages/api-client/openapi.json`, plus the public pages, the board and TV hubs and the development-only identity provider. Every endpoint declares a policy or is explicitly public (`EndpointAuthorizationTests`) |
| Authorisation and IDOR matrix | `tests/Medvya.IntegrationTests/AuthorisationMatrixTests.cs`, real PostgreSQL with row-level security on. 45 routes take an id: 36 are in the cross-tenant matrix (40 method and route pairs), 9 are listed with the reason and the test that covers them elsewhere. A guard test fails when a new route with an id is in neither list |
| (a) Another tenant's id | On each of the 40 pairs, a real row of another clinic answers with the same status as a random id, never with that clinic's data, and changes nothing (the visit, encounter, hold, display, leave, invitation, membership, role, location, closure, access request and patient are checked afterwards) |
| (b) Missing capability | 26 role and endpoint pairs from the role matrix get 403 with no record data; one control pair (solo doctor on settings) gets 200 |
| (c) Facility scope | A practice admin of one location whose session claims another, and reception naming another location's id in a query, are refused on 14 requests (board, overview, day and week diary, displays, failed reminders, patient lookup, consents, completion, staff, settings, access log) |
| Mutation checks | Removing the ManageStaff policy from `/v1/staff` fails (b) on 4 rows. Removing the facility condition from `FacilityDirectory.ActiveRoleAsync` fails (c) on 11 requests. Running as a role that bypasses RLS **and** removing the patient query filter fails (a) (an access request lands in the other clinic). Removing a row from the matrix fails the guard. Removing the TV pairing and receipt rate-limit policies fails their tests |
| Rate limits | Public pages (`/`, `/assist`, `/hospitals`, `/pricing`, `/demo`, `/trust`, `/region`, `/ar/...`): per-IP global limit, new test. Demo and waitlist submissions: hourly quota per client (existing test). TV pairing: 20/min, new test. Delivery-receipt webhook: 600/min, new test. Sign-in and invitation preview: 20/min |
| `pnpm audit --audit-level high` | Pass. Full audit: 2 moderate, 1 low, all in mobile test tooling (jest > test-exclude > glob/minimatch; jest-config > deepmerge) |
| `dotnet list Medvya.sln package --vulnerable --include-transitive` | No vulnerable packages in any of the 43 projects |

## Findings

No high or critical finding is open.

| Severity | Finding | Where | Remediation |
|---|---|---|---|
| Medium | Moderate npm advisories (GHSA-3g3m-2fjr-6pw4 glob, GHSA-g3hv-75wg-m249 minimatch) in mobile test tooling; not shipped | `mobile/clinician` dev dependencies | [#869](https://github.com/ALFRED-LUCIFER/medvya/issues/869), due 4 Dec 2026 |
| Low | GHSA-gcqx-w48m-w2h4 deepmerge, same path | as above | [#869](https://github.com/ALFRED-LUCIFER/medvya/issues/869) |
| Low | Rate limits and the public-form quota are kept in memory per app instance, so scaling out multiplies them | `src/Host.Api/Program.cs`, `PublicSite/PublicForm.cs` | Recorded shortcut, move to a shared store before scale-out (MDV-P0-007) |
| Low | A delivery receipt has no timestamp in its signature, so a captured one can be replayed; the state change is idempotent | `Notifications.Api/NotificationEndpoints.cs` | Add a signed timestamp when the SMS/WhatsApp provider adapter is chosen; no ticket yet |
| Low | The work email typed on the sign-in page travels in the `/bff/login` query string (staff personal data, not PHI; telemetry drops query strings) | `Auth/StaffAuthentication.cs` | Accepted (MDV-P1-059 design) |
| Info | `GET /v1/appointments/{id}/notifications` answers 200 with an empty list for an unknown or foreign id, where other routes answer 404. No existence oracle | `Notifications.Api/NotificationEndpoints.cs` | None needed |
| Gap | `amr=mfa` must be confirmed on real Entra External ID; until then a tenant without it fails closed (no sign-in) | `Auth/StaffAuthentication.cs` | MDV-P0-011 |
| Gap | Retention period for demo and waitlist leads not yet set | `Medvya.Leads` | Retention map; retention jobs MDV-P5-025 |

## Still to do before R1

1. **DAST** against the Azure India dev environment (public pages, demo, waitlist, TV pairing, receipts webhook, sign-in). Needs approval to scan; not run.
2. **External review** of the security-sensitive code (authentication, sessions, tenancy, consent, clinical access) by the part-time reviewer, with the report added here.
3. Container scan of the deployed image.
4. Close [#869](https://github.com/ALFRED-LUCIFER/medvya/issues/869) by its date.
