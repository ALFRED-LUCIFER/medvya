# Road to R1 (front desk MVP): sequence and distance

As of 26 Oct 2026, `main` at `9243809`. Statuses come from `tickets/_source`. Update this page when a wave finishes.

**R1 is done when the P1 exit gate passes** (`tickets/_source/P1.tickets`):
1. Two clinics take real bookings for 2 consecutive weeks, with no paper.
2. 0 double bookings with 100 parallel bookings.
3. Median check-in takes 60 s or less.
4. At least 98 % of reminders are delivered, and failures are visible.
5. No open high or critical security findings.

R0 (the P0 exit gate: journeys signed off, restore tested, staging deploy from a tag) comes first.

## Where we are

| | Estimate | Done | Partly done | Not started or blocked |
|---|---|---|---|---|
| P0 Foundation (R0) | 49.5 d | 18.5 d | 24.5 d | 6.5 d |
| P1 Front desk (R1) | 86.5 d | 12.5 d | 26.0 d | 48.0 d |

The front desk can already run end to end, locally, on synthetic data:
- register (India and UAE), detect duplicates, enforce the guardian rule;
- search, book, view day, week and list, reschedule, cancel, mark no-show;
- walk in, check in on a live board, show the queue on a TV.

Most "Partly done" tickets are waiting only on **staging (Azure)**, **the pilot** or a **later ticket** in this plan.

What is missing for R1:
1. **Practice setup.** A clinic can't sign up or configure itself; everything runs on seeded data.
2. **Consents and notifications.** Reminders are an R1 exit criterion, and they need consent wording from legal.
3. **Cloud.** No Azure environment exists, so there's no staging, no production and no real sign-in.
4. **People.** The decisions, agreements and registrations below.

## The sequence

Each ticket is one PR against `main`, opened only after the previous PR in its track has merged and `main` is merged in (no stacked PRs).

**Tracks are split by module, so no two agents work in the same code.** That's why Setup and Team are one track: P1-004, 005, 007, 008, 009 and 035 all touch Tenancy/Identity and the practice-settings screens, and P1-007 (add doctor) reuses P1-008's staff invite and roles.

### Wave 1: buildable now, no outside dependency (≈ 22 dev-days, about 3 weeks with 3 agents)

**Track 1, Practice and people** (Tenancy, Identity, settings screens). Strictly in order; this is the critical path of Wave 1 (≈ 13.5 d):

| Order | Ticket | Size | Why this order |
|---|---|---|---|
| 1.1 | **MDV-P1-004** Sign-up and six-step onboarding wizard | 3 d | Creates real tenants. Closes the P1-002 audit leftover and the P1-014 tenant-country check. |
| 1.2 | **MDV-P1-005** Practice settings: profile, locations, hours, holidays, plus the schedule-template editing screen (P1-011 leftover) | 2.5 d | Needs the tenant from P1-004. Gives the settings area the later screens live in. |
| 1.3 | **MDV-P1-008** Add staff with role, facility scope, invite; custom roles (F-10) | 2.5 d | Builds the staff list and invite flow that P1-007 reuses |
| 1.4 | **MDV-P1-007** Add doctor (licence India/UAE, verification, schedule link) | 3 d | Needs P1-005 (locations, hours) and P1-008 (staff invite) |
| 1.5 | **MDV-P1-009** Offboarding ends access and sessions at once | 1 d | Needs P1-008. Feeds the P1-038 security review. |
| 1.6 | **MDV-P1-035** Practice access log (who opened which record) | 1.5 d | A settings screen, so it goes after the settings area exists. Feeds P1-038. |

**Track 2, Scheduling** (Scheduling module, schedule screens, ≈ 4.5 d):

| Order | Ticket | Size | Note |
|---|---|---|---|
| 2.1 | **MDV-P1-012** Leave and schedule exceptions with re-offer | 1.5 d | Do it first: Track 1's P1-005 builds the template editor on these rules, so P1-005 merges `main` after this lands. |
| 2.2 | **MDV-P1-026** Recurring appointments (series with per-occurrence exceptions) | 1.5 d | Uses the no-double-booking path |
| 2.3 | **MDV-P1-037** Load test for booking and calendar (local profile now, staging later) | 1.5 d | Last, so it measures the finished booking features (R1 NFR 1) |

**Track 3, Patients and desk** (Patients module, registration and desk screens, ≈ 3.5 d):

| Order | Ticket | Size | Note |
|---|---|---|---|
| 3.1 | **MDV-P1-017** Duplicate/merge review with a second approver | 2.5 d | Safety: never merges clinical data without a second approver |
| 3.2 | **MDV-P1-036** Desk-tablet views (board, schedule, registration) | 1 d | Last, after merging `main`: it touches screens Tracks 1 and 2 change (R1 NFR 3) |

When Tracks 2 and 3 finish (after about one week), they move on to whichever of Wave 2 or Wave 3 is unblocked first:
- Track 3 to **P1-019 consents** (Patients module);
- Track 2 to **P1-032 reminders**;
- or either one to **Wave 3 cloud** once P0-003 is decided.

### Wave 2: consents and notifications (≈ 15 dev-days, starts when legal wording arrives)

The mechanism can be built on draft wording, but it can't go live until **MDV-P1-044** (legal review) is signed. SMS needs **MDV-P1-045** (India DLT sender and template approval, which can take a few weeks, so start it now).

| Order | Ticket | Size | Needs |
|---|---|---|---|
| 12 | **MDV-P1-019** Versioned consents at registration (completes P1-013/014 leftovers) | 3 d | P1-044 wording |
| 13 | **MDV-P1-032** Email confirmations and 24 h / 2 h reminders (Hangfire) | 2.5 d | P1-019 |
| 14 | **MDV-P1-033** SMS confirmations and reminders with delivery receipts (also the SMS sink, P0-005 leftover) | 2 d | P1-045 DLT |
| 15 | **MDV-P1-034** Staff notification centre | 1.5 d | P1-032 |
| 16 | **MDV-P1-006** Notification preferences and data tab | 1.5 d | P1-032 |
| 17 | **MDV-P1-010** Licence expiry reminders and access review | 1.5 d | P1-007, P1-032 |
| 18 | **MDV-P1-027** Waitlist with "offer earlier slot" | 1.5 d | P1-024, P1-033 |
| 19 | **MDV-P1-041** Baseline metrics (check-in time, no-shows) | 1.5 d | P1-028, P1-033 |

### Wave 3: cloud (≈ 11 dev-days, starts when MDV-P0-003 is decided)

| Order | Ticket | Size |
|---|---|---|
| 20 | **MDV-P0-006** Azure environments as code (Bicep) | 3 d |
| 21 | **MDV-P0-007** OIDC federation, tagged deploy to staging | 1.5 d |
| 22 | Leftovers of **MDV-P0-008/009** (Entra-only SQL, Key Vault keys, TDE with customer-managed key) | ≈ 2 d |
| 23 | Leftovers of **MDV-P0-011/012/013** (Entra External ID sign-in with MFA and passkeys, "My devices", SMS invites) | ≈ 2.5 d |
| 24 | **MDV-P0-019** Backups, point-in-time restore and a timed drill; leftovers of **P0-017/018/027** (Blob digest, dashboards and the 5xx alert, staging seed) | ≈ 2 d |
| 25 | Staging runs: P1-022 100-parallel and P1-023/037 budgets | ≈ 0.5 d |

### Wave 4: gates and release (≈ 8 dev-days plus 2 calendar weeks of pilot)

| Order | Ticket | Needs |
|---|---|---|
| 26 | **MDV-P0-029** R0 exit gate and release notes | Wave 3, P0-023 clinical sign-off |
| 27 | **MDV-P1-038** P1 security review (external reviewer) | P1-019, P1-009, P1-035 |
| 28 | **MDV-P1-039** Production deployment, a tenant per pilot clinic | P1-038, P1-043 signed clinics |
| 29 | **MDV-P1-040** Reception training, desk card, support contact | P1-039 |
| 30 | **MDV-P1-042** R1 pilot: 2 consecutive weeks of real bookings, exit gate | everything above |

## Distance to R1

- **Development left:** about **55 dev-days** (Wave 1 ≈ 22 d, Wave 2 ≈ 15 d, Wave 3 ≈ 11 d, Wave 4 ≈ 8 d). With three tracks:
  - Wave 1 takes about **3 weeks**, set by Track 1 (≈ 13.5 d);
  - Waves 2 and 3 overlap it once their blockers clear;
  - so building takes roughly **5–6 weeks**, followed by the **2-week pilot**.
- These figures assume the people items below are resolved by the time each wave needs them. If they aren't, the code waits.
- **The real critical path is people, not code.** Nothing in Waves 3–4 can start before these:

| Owner | Item | Blocks |
|---|---|---|
| Founder | **MDV-P0-003** launch market + Azure subscription and region | all of Wave 3, production, pilot |
| Founder | **MDV-P0-002** accept ADR-001, 003–006 | R0 gate |
| Founder | **MDV-P1-043** sign two pilot clinics | production, pilot |
| Founder | **MDV-P1-045** India DLT sender and SMS templates (start early: approval has lead time) | SMS reminders (exit criterion 4) |
| Counsel | **MDV-P1-044** privacy notice, ToS, DPA and consent wording; real privacy contact | consents, reminders, public forms going live |
| Clinical advisor | **MDV-P0-023** journey sign-off | R0 gate |
| External reviewer | **MDV-P1-038** security review | production |
| Tax adviser | sign-off of printed documents (**MDV-P4-031**) | not R1 (billing is P4) |

Recommended next step: **start Wave 1** (three tracks), and **in parallel start the people items**: P0-003, P1-043, P1-044 and P1-045 have the longest lead times.
