# 02 · Route, role and state inventory

Sources: `web/clinic/src/app/App.tsx` (the route table), `web/clinic/src/features/shell/domain/navigation.ts` (navigation and capability guards), `web/clinic/src/app/SettingsRoute.tsx` (settings tabs), and what the browser showed. "Visited" means a real browser opened it during this audit; "code only" means the route exists in code and was not opened.

Reviewer A's independent inventory, with the modals and dialogs met during the walkthroughs, is in [reviews/reviewer-a-clinical-workflow.md](reviews/reviewer-a-clinical-workflow.md). The machine-generated table of every captured state is in [evidence/local-coverage-table.md](evidence/local-coverage-table.md).

## 1 · Roles

| Role in code | Synthetic user (local stack) | On Azure dev | Lands on | Status |
|---|---|---|---|---|
| Owner doctor (`SoloDoctor`) | Dr. Arjun Rao | Arjun Menon (2 clinics) | `/today` | Built |
| Clinic doctor (`HospitalDoctor` in the seed) | Dr. S. Menon | none | `/today` | Built, same shell as the owner doctor without Settings |
| Receptionist | A. Joseph | Arjun Menon at Load Test Clinic | `/front-desk` | Built |
| Nurse | S. Thomas RN | none | `/today` (the board), or `/nurse` when nurse intake is switched on | Built, behind the owner's Nurse intake setting |
| Practice admin | K. Lakshmi | none | `/front-desk` | Built |
| Care coordinator | Priya N. | none | `/today` (the board) | Role exists, its own screens (care plans, inbox) are planned for phase 4 |
| Hospital IT | R. Varghese | none | `/settings` | Role exists, enterprise screens are planned for phase 7 |
| Patient | Meera Iyer | none | no clinic screens | Web portal planned (phase 4). The Expo patient app exists with development-only data |
| Medvya operations | none in the clinic app | not deployed | platform console | Console built (`web/platform-admin`), not deployed to Azure |

No separate "administrator" product exists for a clinic: administration is the Practice settings screen, open to the owner doctor and the practice admin.

## 2 · Clinic web app routes

Legend for "Opened": **Az** Azure dev, **L** local full stack. Widths are in [01-scope-environments-and-coverage.md](01-scope-environments-and-coverage.md).

### Signed out and public

| Route | Purpose | Guard | Opened | States seen | Notes |
|---|---|---|---|---|---|
| `/` | Sign in (email hint, then the identity provider) | public | Az, L | default, loading | Azure shows "Loading…" alone for a moment before the form |
| `/signup` | Start a new practice | public | code only | – | Not opened: creating an account is outside this audit's permissions |
| `/onboarding` | Setup wizard for a new practice | signed in, no clinic | code only | – | Needs a new account |
| `/choose-facility` | Pick the clinic to work in | signed in, more than one membership | Az | default (3 memberships) | One of three attempts needed a second click (hypothesis, not reproduced) |
| `/invite/:token` | Accept a staff invitation | public token | code only | – | Needs a real invitation link |
| `/verify/rx/:token` | QR check of a printed prescription | public token | code only (reviewer A may have opened it) | – | |
| `/verify/paper/:token` | QR check of a slip or referral letter | public token | code only | – | |
| `/rx/:token` | Shared prescription link | public token, code by SMS | code only | – | SMS code needs a provider that is not set up (MDV-P1-045) |
| `/__ui` | Developer component gallery | none | L (reviewer B) | – | Development aid, not a product screen |
| session expired page | Replaces every screen when the session ends | – | code only | – | |

### Signed in, inside the shell

| Route | Purpose | Who sees it | Opened | Primary action | States seen |
|---|---|---|---|---|---|
| `/today` | Doctor's day: next patient, visits, notes to sign | Patient 360 at Full: doctors. Other clinical roles get the reception board here | Az (empty day, 2 notes to sign), L (8 visits) | Start consultation | empty, populated, notes-to-sign |
| `/front-desk` | Reception board by status (6 columns) | everyone with the queue capability | Az (empty), L (9 visits) | Walk-in | empty, populated |
| `/front-desk/overview` | Board grouped by doctor, reassign | same | Az, L | Move patient | empty, populated |
| `/front-desk/walk-in` | Find or register, then check in a walk-in | register and queue both at Full | Az, L | Check in now | default |
| `/front-desk/waitlist` | Waitlist entries and offers | register at Full | Az, L | Add to waitlist | empty form |
| `/schedule` | Day, week, list calendar and booking panel | register capability | Az (past slots), L (booked day) | Book a slot | day view |
| `/schedule/leave` | Leave and schedule exceptions | register at Full | Az, L | Add exception | default |
| `/patients/new` | Full registration; `?mode=quick` for five fields | register at Full | Az, L | Register patient | empty form (validation states: reviewer B) |
| `/patients` → `/patients/directory` | Patient search and list (masked until opened) | register at Read or Full | Az, L | Search | list, filters |
| `/patients/directory/merge/:id?` | Duplicate review | same | Az, L | Request / approve merge | empty |
| `/patients/directory/:patientId` | Administrative record (demographics, IDs, contacts) | same | Az, L | Book, check in | complete, incomplete registration |
| `/patients/directory/:patientId/complete` | Finish a quick registration | register at Full | L | Save | default |
| `/patients/:patientId` | Patient 360 chart | Patient 360 capability | Az (sparse), L (allergy, history) | Add medicine | tabs Everything, Summary, Vitals and labs, Chronic care, History, Timeline |
| `/patients/:patientId/timeline` | Visit timeline | same | Az, L | – | empty, populated |
| `/patients/:patientId/docs` | Documents | clinicians, and the desk for upload | Az, L | Upload | empty |
| `/consult/:encounterId` | Consultation: note, prescription, orders, referrals | Patient 360 capability | Az (ended, empty note), L (ended) and reviewer A (open) | Sign note | One page / Guided steps, Assist / Chart / Checklist panel |
| `/consult/:id/medicines` | Medicine reconciliation | Patient 360 at Full | Az, L | Reconcile | empty |
| `/consult/:id/problems` | Problems and diagnoses | same | Az, L | Add diagnosis | empty |
| `/consult/:id/note/print` | Signed note print view | same | L | Print | no `h1` on the page (axe: `page-has-heading-one`) |
| `/consult/:id/rx/print`, `/slip`, `/referrals/:id/letter` | Printed prescription, investigation slip, referral letter | prescribers | L (rx print, slip) | Print | unauthorised-draft message |
| `/nurse`, `/nurse/:appointmentId` | Nurse intake queue and one intake | vitals at Full and the tenant flag | L: "Page not found" while the setting is off; reviewer A with it on | Hand off | – |
| `/settings?tab=…` | 17 panels in 5 groups: Practice (profile, locations, hours, holidays, displays, metrics, terms), People (staff, doctors, review), Clinical (nurse, policy, requests, medicines), Messages (notifications), Security (audit, data) | staff/roles at Config | Az (all 17 at 1440), L (all 17 at 5 widths) | varies | default per tab |
| `/admin/staff` | Invite a colleague | staff/roles at Config | Az, L | Send invitation | Reached by URL or from Settings only |
| `/organisation` | Onboarding request and documents | same | Az, L | Submit | `GET /v1/organisation/request` answers 404 for an already-approved clinic and the page shows an empty request |
| `/account/devices` | My devices and sessions | everyone | Az, L | End session | list |
| `/more` | Mobile "More" menu | everyone | Az, L | – | list |
| `*` | Not found | – | Az, L | Go home | Also what a role sees for a screen it may not open |

### Flagged off or placeholder (planned, not built)

These navigation items exist in `navigation.ts` behind tenant flags that are off, and their routes render a "coming soon" placeholder when the flag is on: Messages (`care.messages`, phase 4), Video (`clinical.video`, phase 8), Referrals list (`clinical.referrals`, phase 2), Results inbox (`care.results-inbox`, phase 4), Care plans (`care.plans`, phase 4), Billing (`billing.invoices`, phase 4), Devices (`assistant.devices`, phase 3). None was opened: they are not reachable for a pilot clinic today.

## 3 · What each role is refused

Observed on the local stack and on Azure (receptionist membership):

| Role | Asked for | Shown |
|---|---|---|
| Receptionist | `/today`, `/settings`, `/patients/:id` (chart), `/consult/:id` | "Page not found" with the full shell |
| Nurse | `/settings` | "Page not found" |
| Nurse | `/patients/new` | A message that this role cannot register (the only refusal that says why) |
| Nurse | `/consult/:id` | The page loads, then two API calls answer 403 (`/v1/note-templates`, `/v1/encounters/:id/note`) |
| Practice admin | `/patients/:id` (chart) | "Page not found" |
| Hospital IT | `/front-desk` | "Page not found" |

The server refuses correctly in every case seen. The interface does not tell a user that the page exists and their role cannot open it (finding F-17).

## 4 · Other surfaces

| Surface | State | Evidence |
|---|---|---|
| Platform console (`web/platform-admin`): overview, approvals, team, audit, sign-on with a security key | Built, not deployed. Opened with a mocked API only | `screenshots/platform-admin-mocked/` (reviewer C) |
| Clinician mobile app (`mobile/clinician`): sign in, lock, Today, Patients, patient record, Inbox, Consult, Account | Built with development-only data. Could not be run: Metro fails to bundle on this machine | `screenshots/mobile-expo/clinician/375-bundle-error.png`, code review |
| Patient mobile app (`mobile/patient`): sign in, lock, Home, Book, Records, Messages, Profile | Same | code review |
| Queue TV display | Configured in Settings → Queue displays. The display page itself was not opened | untested |
