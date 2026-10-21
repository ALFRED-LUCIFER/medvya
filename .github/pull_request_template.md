## Ticket

MDV-

## What changed


## Definition of Done (docs/05-delivery)

- [ ] All acceptance criteria of the ticket pass
- [ ] **Synthetic data only**: no real patient data in code, tests, fixtures or logs
- [ ] X1 Security: server-side authorisation on new endpoints, input validated, no secrets
- [ ] X2 Privacy: no PHI in logs/telemetry; audit events for patient-data reads/writes
- [ ] X3 Clinical safety: identity on screen, allergy never colour-only, unknowns left blank
- [ ] X4 States: empty, loading, error, 403, offline handled; no data loss on refresh
- [ ] X5 Accessibility & language: WCAG 2.2 AA, keyboard, i18n strings (en/hi/ar), RTL
- [ ] X6 Performance budgets met
- [ ] X7 Migrations forward-only and reviewed; retries idempotent
- [ ] X8 Tests added; behind a tenant feature flag where user-facing
