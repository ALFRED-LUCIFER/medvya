# Delivery process, conventions and ticket rules

**Current plan to the first release:** [r1-roadmap.md](r1-roadmap.md) gives the ticket sequence to R1, the distance left, and the people items on the critical path.

## Release process (same five steps close every phase)

1. **Scope freeze.** Requirement IDs for the phase are locked. Changes go to the next phase unless they are safety-critical.
2. **Staging acceptance.** Playwright journeys are green. Exit criteria are checked on synthetic data. The security scan is clean.
3. **Pilot on flags.** Recipients get the release behind tenant flags, with daily check-ins for the first week.
4. **Exit gate review.** Founder, clinical advisor and one pilot user confirm each exit criterion with evidence.
5. **Handover.** The delivery package is sent: release notes, training, runbook, known issues. The phase is closed.

**Feature flags (MDV-P0-028):** per tenant, stored in `tenancy.FeatureFlags` (owner team and description required, same row-level security as other tenancy tables). A flag with no row is off. Gate an endpoint with `.RequireFeature("area.feature")`: while the flag is off the endpoint answers 404, before any other filter. Change a flag only through `IFeatureFlags.SetAsync`, which audits it (`flag.enable` / `flag.disable`) and takes effect on the next request; never edit the table by hand. Not yet built: the authorised operations endpoint that calls it (needs sign-in, MDV-P0-011), the console UI (P5-08), and listing flags in `/v1/me/context` for navigation.

**Versioning:** R0–R4 are 0.x pilot releases. R5 = v1.0 GA. Later phases ship as minor versions. Migrations are forward-only and reviewed.

## Definition of Ready (ticket can start)

- [ ] Requirement ID(s) and screen ID(s) linked, and the design file named. A `DESIGN NEEDED` ticket has its design spike done first
- [ ] Acceptance criteria are testable (Given/When/Then or checklist) and agreed
- [ ] Dependencies are done or explicitly stubbed
- [ ] Data classification of new fields decided (public / internal / personal / sensitive health / national ID)
- [ ] Country differences (IN/AE) listed or marked "same"
- [ ] Confidence ≥ 60%. Lower-confidence tickets need a spike or a question answered first

## Definition of Done (every ticket)

Functional:
- [ ] All acceptance criteria pass, demonstrated on staging with synthetic data
- [ ] Linked requirement ID(s) referenced in the PR title and the release notes

Cross-cutting X1–X8 (PRD v2 §05). A ticket that misses one is **not shippable**:
- [ ] **X1 Security:** server-side authorisation on every new endpoint (tenant, facility, role, relationship, purpose). Input validated. No secrets. Rate limit where public
- [ ] **X2 Privacy:** no PHI in logs, analytics or errors. Audit event written for patient-data reads and writes. Consent respected
- [ ] **X3 Clinical safety:** patient identity on screen. Allergy never colour-only. Uncertain match = hard stop. Unknowns left blank
- [ ] **X4 States:** first-use, empty, loading/slow, error, unauthorised (403), offline, no-consent states designed and implemented. No data loss on refresh or network drop
- [ ] **X5 A11y & language:** WCAG 2.2 AA (axe clean), keyboard path, screen-reader labels, 200% zoom, strings in i18n (en/hi/ar) with RTL verified, touch targets ≥ 44px (portal 48px)
- [ ] **X6 Performance:** reads p95 < 300 ms, writes < 600 ms, page interactive < 2.5 s p75
- [ ] **X7 Reliability:** migration reviewed and forward-only. Idempotent retries
- [ ] **X8 Quality:** unit + integration tests (Testcontainers SQL Server), Vitest for UI, Playwright where a journey is touched. OpenTelemetry spans with PHI scrubbing. Behind a tenant feature flag with an owner

## Ticket conventions

- **Key:** `MDV-<phase>-<nnn>`, e.g. `MDV-P1-014`. **Epics:** `MDV-<phase>-E<nn>`. Keys are stable and never reused.
- **Types:**
  - Epic: one per PRD requirement group
  - Story: user-visible value
  - Task: technical/enabling work
  - Spike: time-boxed investigation or design
  - Gate: exit-criterion verification
  - Chore: docs, packages, compliance artefacts
- **Priority:** the PRD priority, MUST / SHOULD / COULD (in Jira, Highest / High / Medium).
- **Estimate:** ideal developer-days for one developer with AI coding help (the PRD plan assumption).
- **Window:** target start/end dates from the phase calendar ([../01-prd/roadmap.md](../01-prd/roadmap.md)). Week 1 = 26 Oct 2026.
- **Traceability fields:** `PRD` (requirement IDs), `Screens` (screen IDs), `Sources` (design files / docs).

## Confidence level (on every ticket)

How sure we are that **the scope and acceptance criteria are right and complete, and the estimate holds**. It is not a measure of how hard the work is.

| Band | % | Meaning | Typical reasons |
|---|---|---|---|
| **High** | 85–95 | Ready to build as written | Hi-fi design and interaction logic exist; requirement is explicit; no external dependency |
| **Medium** | 65–84 | Buildable; expect small AC changes during refinement | Design labels only (e.g. truncated import), or an external approval/vendor choice still open |
| **Low** | 40–64 | Needs a spike, a decision or a design first | `DESIGN NEEDED` screen, regulator/vendor onboarding, hardware, legal review outstanding |

100% is never used: clinical and legal review can always change scope. Each ticket states the **reason** for its confidence and what would raise it.
