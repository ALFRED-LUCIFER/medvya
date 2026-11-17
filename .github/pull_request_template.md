## Ticket

MDV-

## What changed


## How verified

<!-- Commands run and their result, tests added or run, browser check in English and Arabic, axe result. -->


## Not done / follow-up ticket

<!-- What is deliberately left out, and the ticket that covers it. "None" if nothing. -->


## Definition of Done (AGENTS.md §5)

- [ ] All acceptance criteria of the ticket pass
- [ ] **Synthetic data only**: no real patient data in code, tests, fixtures or logs
- [ ] AGENTS.md §5 commands pass (affected tests locally; full suite in CI)
- [ ] Safety tests mutation-checked, each with its `// SAFETY: mutation check:` comment (name the tests): 
- [ ] Database change: ERD regenerated (`python3 -I tools/erd/generate.py`), or no schema change
- [ ] X1 Security: server-side authorisation on new endpoints, input validated, no secrets
- [ ] X2 Privacy: no PHI in logs/telemetry; audit events for patient-data reads/writes
- [ ] X3 Clinical safety: identity on screen, allergy never colour-only, unknowns left blank
- [ ] X4 States: empty, loading, error, 403, offline handled; no data loss on refresh
- [ ] X5 Accessibility & language: WCAG 2.2 AA, keyboard, i18n strings (en/hi/ar), RTL
- [ ] X6 Performance budgets met
- [ ] X7 Migrations forward-only and reviewed; retries idempotent
- [ ] X8 Quality: tests added (unit, integration on PostgreSQL, e2e where a journey is touched)
