# 01 · Scope, environments and coverage

> **Screenshots are kept outside git.** The real-app screenshots (about 200 MB) stay in the local evidence set and are not in this repository. Every evidence path under `screenshots/` in these documents refers to that local set. The concept previews in `concepts/screenshots/` are in git.

Audit run on 13 November 2026. Audit only: no application code, test, infrastructure or design asset was changed. Everything the audit wrote is under `docs/product-ux-audit/`, with two exceptions listed under "Side effects".

## 1 · Environments

| Environment | What it is | How it was verified as non-production | Used for |
|---|---|---|---|
| **Azure dev**, `https://app-medvya-dev-3afs7i.azurewebsites.net` | App Service `app-medvya-dev-3afs7i` in resource group `rg-medvya-dev-in`, Central India, real PostgreSQL, real Entra sign-in with MFA | `docs/03-architecture/azure-env-dev.md` ("Synthetic data only", "development only" subscription) and `docs/05-delivery/session-handoff.md` name this app and its synthetic test account. Every clinic on screen is marked "(synthetic)" | The real deployed product as owner doctor and as receptionist |
| **Local full stack**, `http://localhost:5391` | `tests/e2e/fullstack/stack.mjs`: a throwaway PostgreSQL 16 in Docker, the synthetic cast seeded by the project's generator, the production web build, the real `Host.Api` with the development sign-in provider | Created by the audit, on this machine, removed on exit | All roles, all five viewports, the live walkthroughs |
| **Platform console with a mocked API**, `http://localhost:4175` | The console's dev server with the API mocked as in `tests/e2e/platform-console.spec.ts` | Not a deployed environment at all | Screens of the console only |
| **iPhone simulator with Expo Go** | For the clinician app | Local | Blocked: the app did not bundle |

No production environment exists for this product yet, and none was touched.

**Version difference.** The latest tag is `v0.0.19`; the local stack ran the working tree of `main` as merged into the checked-out branch, which is ahead of it (232 web and package files differ). So "Azure" findings describe what is deployed and "Local" findings describe current `main`. Finding F-13 exists only on Azure.

**Sign-in.** On Azure the founder signed in by hand in the Playwright window, as the synthetic test owner. The audit typed no credential and no MFA code. On the local stack the development provider has no passwords.

## 2 · Tools and skills actually used

| Tool or skill | Used how | Note |
|---|---|---|
| Playwright MCP browser (headed, 1920×1080 window) | Azure dev: navigation, screenshots, keyboard checks | Viewport resizing only: no touch or mobile emulation on Azure |
| Playwright library from `tests/e2e` (headless Chromium) | Local capture (`tools/local-capture.mjs`), walkthroughs, state captures, concept rendering | 375, 768 and 1024 used touch and mobile emulation |
| axe-core | Run on every locally captured page | Automated checks are evidence, not proof of conformance |
| The project's own journey specs (J1, J2, J3, front desk) | Run once against the local stack to fill a realistic clinic day | 16 passed, 7 skipped by design |
| Impeccable skill (v4.9.0) | Loaded by the main session (context, craft floor for the concept) and by reviewer B (critique, audit, layout, typeset, animate references; detector run once) | The project has no `PRODUCT.md` or `DESIGN.md`; the skill's interview steps were replaced by the audit brief |
| UI UX Pro Max skills (v2.15.0) | Loaded by reviewer C: ui-ux-pro-max, design-system, ui-styling; 11 read-only searches, token validator twice | The `brand` and `design` skills were invoked by the request; they are for brand assets, logos and banners, and nothing in them was run, because the audit forbids changing brand assets |
| "medical-product-ux-director" | **Not installed.** Reviewer A ran as a general-purpose Claude subagent with a written brief | |
| Animation skills (review-animations, find-animation-opportunities) | Read by reviewer B, in part | |
| Claude in Chrome | Not used: the Playwright browser covered it | |
| Subagents | Three reviewers ran **in parallel** as background agents, then a second round of rebuttals | See [06](06-three-independent-reviews.md) |

References the reviewers report having opened: WCAG 2.2 (partly, wording paraphrased), NHS design system pages, Vercel Web Interface Guidelines, NISTIR 7804 and 7804-1 (reviewer A extracted the PDFs; reviewer C could not). No competitor product was examined, so none is cited.

## 3 · Synthetic accounts

| Role | Azure dev | Local stack |
|---|---|---|
| Owner doctor | Arjun Menon (synthetic), Pilot Walk Clinic | Dr. Arjun Rao |
| Receptionist | Arjun Menon (synthetic), Load Test Clinic | A. Joseph |
| Nurse | none | S. Thomas RN |
| Practice admin | none | K. Lakshmi |
| Clinic doctor | none | Dr. S. Menon |
| Care coordinator | none | Priya N. |
| Hospital IT | none | R. Varghese |
| Patient | none | not opened (no clinic screens) |

Patients: Azure had "Test Patient 2" and "Kavya Rao (synthetic)" and an empty clinic day. The local day had 9 to 13 visits: Meera Iyer (penicillin allergy), Deepak Nair, and walk-ins created by the journey specs and by reviewer A's scripts.

## 4 · Coverage in numbers

| Measure | Count |
|---|---|
| Route patterns found in the clinic app's code | 47: 10 signed-out or public, 30 inside the shell (Settings counted once; it has 17 tabs), 7 flagged-off placeholders |
| Opened in a real browser | 32 of 47 patterns, plus all 17 settings tabs |
| Not opened | 15: sign-up, onboarding wizard, invitation, three public verify and share pages, session-expired page, the referral letter, and the 7 flagged-off placeholders |
| Azure dev page states captured | 155 (54 role-screens: 44 as owner doctor, 10 as receptionist) |
| Local page states captured | 615 (123 role-screens × 5 viewports), each with axe and layout metrics |
| Roles covered | 2 on Azure, 7 locally |
| Viewports | 1920, 1440, 1024, 768, 375. Azure: all five for the main routes, 1440 only for settings tabs. Local: all five for every route |
| Interactive walkthroughs | 11 scripts by reviewer A: walk-in new and returning, check-in, booking, reschedule, reassign, waitlist, nurse intake and escalation, consultation to signing and addendum, free-text prescription, admin settings, cross-role handoffs |
| State captures | 130 by reviewer B: keyboard focus, empty-submit errors, search, notifications, Arabic and Hindi, 200 % and 400 % zoom, reduced motion |
| Screenshots | 1,832 real-app and mock files (305 Azure, 1,230 local routes, 130 states, 136 walkthroughs, 30 console mock, 1 simulator), plus 23 concept previews. Each route has a first-viewport and a full-page file |
| Accessibility checks attempted | axe on 615 pages; computed contrast on rendered colours; keyboard tab order and focus visibility; focus return after dialogs; target-size metrics; 200 % and 400 % zoom; reduced motion; right-to-left |

The per-screen table is in [evidence/local-coverage-table.md](evidence/local-coverage-table.md). Screenshot paths follow `screenshots/<environment>/<role>/<route-or-flow>/<viewport>-<state>.png`.

## 5 · Blocked and untested

| Item | Why | Effect |
|---|---|---|
| Mobile apps on a device or simulator | Metro fails to bundle: "Cannot find module '@babel/helper-define-polyfill-provider'" | Mobile is code review only (F-34) |
| Platform console against a real API | Not deployed; needs a platform database role and a security key | Mock screenshots only |
| Nurse, admin and other roles on Azure | Only one Azure account exists, with owner-doctor and receptionist memberships | Those roles are local only |
| A populated clinic day on Azure | The Azure clinic had no visits today; the audit did not create patients or visits there | Azure shows empty and near-empty states; workflows were walked locally |
| Real Entra step-up during prescribing and signing | Needs the founder's credentials each time | F-25 is verified with the development provider only |
| Sign-up, onboarding, invitation acceptance | Would create accounts or need a readable mailbox | Not opened |
| SMS and WhatsApp messages, payments | No provider; must not be triggered | Untested by design |
| Staff invite, offboarding, leave, holidays | Opened, not submitted | F-30 is a hypothesis |
| Printing to paper, PDFs, the day sheet | Print views were opened; no printer or PDF output checked | Untested |
| The queue display on a television | Opened at 1920 by reviewer A only | Partly tested |
| Physical tablets and phones; a screen reader; dark mode | Emulation only; dark mode is not built | All device and touch claims are provisional |
| Offline behaviour | Not exercised | Untested |
| Arabic and Hindi | Seen in state captures for a few screens; the workflows were walked in English | Largely untested |
| A clinician, nurse, receptionist or owner | None took part | See [11](11-pilot-doctor-validation-plan.md) |
| Load (30 or more patients on the board) | Not created | The board and list claims are untested at volume |

## 6 · Side effects of the audit

- **Azure dev:** read-only, except that the test user's active clinic was switched twice (Pilot Walk Clinic → Load Test Clinic → Pilot Walk Clinic). It ends on Pilot Walk Clinic. No patient, visit or setting was created or changed there.
- **Local stack:** disposable. It holds the journey specs' data and reviewer A's two synthetic patients, visits, three signed notes and two authorised prescriptions. It is removed when the stack stops.
- **Git:** another session working in the same checkout committed 547 of the audit's in-progress files into `fix/MDV-P0-042-locked-digest-policy` (commit `d7664b5f`, pushed). The audit did not touch that commit. To stop it happening again, the audit added `docs/product-ux-audit/`, `.playwright-mcp/` and `web/clinic/.impeccable/` to the local-only `.git/info/exclude`. **Remove those three lines when you want to commit the audit.**
- **`web/clinic/.impeccable/config.json`:** written by the Impeccable hook when the audit recorded one narrow ignore for the concept files. It is outside the audit folder and can be deleted.
- **`.playwright-mcp/`** at the repository root holds the Playwright MCP's page snapshots and console logs. It can be deleted.
- **Size:** the screenshots total about 204 MB. Consider keeping the first-viewport files in git and the full-page files outside it.
