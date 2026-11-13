# Reviewer A · Clinical product and workflow review

Independent review, one of three. Lens: role goals, task flow, patient safety, clinic operating reality (a busy Indian OPD), information architecture and workflow completeness. Visual polish and design-system architecture are left to the other two reviewers; I did not read their files.

Every claim carries one of these labels: **verified defect**, **usability observation**, **hypothesis**, **absent feature**, **blocked**, **untested**.

---

## 1 · Execution note

**Who ran this.** No skill named "medical-product-ux-director" is installed on this machine. I ran as a general-purpose Claude subagent with the written brief only. Nothing here is a clinician's opinion, and nothing here is a claim of compliance or certification.

**What I ran.**

- Read `web/clinic/src/app/App.tsx`, `features/shell/domain/navigation.ts`, `docs/02-design/journeys.md`, `sitemap-and-permissions.md`, the full-stack specs (`tests/e2e/fullstack/*.spec.ts`), the queue-token code (`src/Modules/Scheduling/.../FrontDeskService.cs`, `Domain/Queue.cs`), the checklist rule (`features/encounters/domain/checklist.ts`), the offboarding guard (`Tenancy/.../StaffRoster.cs`) and the UI strings in `packages/i18n/messages/en.json`.
- Reviewed the captured screenshots (Azure dev and local stack) and `evidence/local-metrics.jsonl` (615 page records).
- Drove the throwaway local stack (`http://localhost:5391/app/`, synthetic data, dev sign-in, fixed clock 3 Nov 2026 10:00 IST) headless with Playwright, after the screenshot capture had finished. Ten scripts, 133 screenshots, one 686-line log.

| Artefact | Path (under `docs/product-ux-audit/`) |
|---|---|
| Scripts | `tools/walkthrough-lib.mjs`, `walkthrough-receptionist.mjs`, `walkthrough-receptionist-2.mjs`, `walkthrough-handoff.mjs`, `walkthrough-doctor-1.mjs` to `-4.mjs`, `walkthrough-admin.mjs`, `walkthrough-crossrole.mjs` |
| Screenshots | `screenshots/local-stack/walkthroughs/<role>/<flow>/NN-state.png` |
| Raw log (every click, typed field, count and page text) | `reviews/reviewer-a-evidence/walkthrough-a.log` |

**How counts were made.** The harness counts clicks, typed fields, route changes (main-frame path changes) and scroll distance (sum of scroll-position changes before each action). A radio button or a chip counts as a click. Sign-in is excluded. I report no timings except one that the script measured (autosave, section 4.3). `scriptMs` values in the log are machine time, not human time, and are not used.

**What I changed on the stack.** Two synthetic patients (Lalitha Ramanbief, Bhavani Subramanianheae), several visits and tokens, three consultations, two signed notes, one authorised prescription, one waitlist entry, one booking for Meera Iyer on 4 Nov, one queue display "Waiting room TV (reviewer A)". Nurse intake was switched on for the handoff run and switched back off at the end.

**Limits.**

- English only. Arabic, Hindi, right-to-left, offline, 200% zoom and real devices: **untested** by me.
- Viewports: 1440×900 desktop, and 1024×768 touch emulation for nurse intake and the consultation. No phone-width walkthrough.
- Azure dev: screenshots only, never touched.
- The step-up was the dev provider (pick the same user again). Real Entra re-authentication and MFA: **untested**.
- Not submitted: staff invitation, offboarding, leave, holiday, merge approval, prescription share by SMS or email, PDF downloads, logo upload. I opened the forms and cancelled.
- UAE rules, child and pregnancy templates, referrals, documents upload, notification centre: **untested**.
- **Harness artefact, not a product defect:** my sign-in helper (copied from the capture script) clicks the first text matching "(synthetic) · ". On the doctor's Today that is a patient link, so each of my doctor sessions opened Meera Iyer's chart once before the flow started. A clean sign-in lands on `/today` (traced separately). The capture's `local-discovery.json` shows the same artefact as the doctor's "landing".
- The project rule to invoke the Ponytail skill applies to development tasks. This was an audit with no code change, so I did not invoke it.

**Sources opened.**

| Source | How opened | Used for |
|---|---|---|
| NISTIR 7804, *Technical Evaluation, Testing, and Validation of the Usability of Electronic Health Records* (Lowry et al., 2012). `https://nvlpubs.nist.gov/nistpubs/ir/2012/NIST.IR.7804.pdf` | PDF text extracted locally | The three-step EUP (Step I Application Analysis, Step II Interface Expert Review, Step III Validation Test) and the "Use Error Root Causes" list, pp. 16–17: patient identification, mode, data accuracy, data availability, interpretation, recall, feedback, data integrity errors |
| NISTIR 7804-1, *…Empirically Based Use Cases for Validating Safety-Enhanced Usability and Guidelines for Standardization* (Lowry et al., 2015). `https://nvlpubs.nist.gov/nistpubs/ir/2015/NIST.IR.7804-1.pdf` | PDF text extracted locally | Guidance 1 (1.1, 1.2), 2 (2.2, 2.4), 3 (3.1 to 3.4), pp. 36–39 |
| WCAG 2.2, `https://www.w3.org/TR/WCAG22/` | via web-fetch subagent | SC 2.5.8 Target Size (Minimum), 3.3.7 Redundant Entry, 3.3.4 Error Prevention, 1.4.1 Use of Color |
| NHS digital service manual: Question pages, Warning callout, Summary list, Accessibility (`https://service-manual.nhs.uk/...`) | via web-fetch subagent (titles and one or two lines each) | Background only. I cite none of these for a finding. |

Anything else from these documents: **not examined**.

---

## 2 · Route × role × state inventory

Legend: **V** visited in the browser by me · **S** seen in captured screenshots only · **C** code only · **D** denied (shows "Page not found") · **P** placeholder · **B** broken or wrong. Roles: Dr = owner doctor, Rc = receptionist, Nu = nurse, Ad = practice admin, CC = care coordinator, HD = hospital doctor, IT = hospital IT.

| Route | Dr | Rc | Nu | Ad | CC / HD / IT | States and overlays I met |
|---|---|---|---|---|---|---|
| `/` sign-in, dev provider, clinic picker | V | V | V | V | S | Picker skipped with one clinic |
| `/today` | V (own Today) | D | V (shows the reception board, titled "Today" in the menu) | D | CC S (board) · HD S (Today) | My patients / Whole clinic toggle; "Next patient" card; "notes to sign" list; second-consultation alert |
| `/front-desk` | V | V | V | V (read-only, no actions) | S · IT D | Columns Booked, Checked in, With nurse, Ready for doctor, In consult, Done; inline "Confirm identity" group; live-updates chip |
| `/front-desk/overview` | S | V | S | V | – | Doctors today; Waiting room table; inline "Move to another doctor" form |
| `/front-desk/walk-in` | S | V | – | – | – | Search results; picked patient; same-day second-visit alert; success with token |
| `/front-desk/waitlist` | S | V | – | – | – | Empty; entry with "Offer" and "Remove" |
| `/schedule` | S | V | S | S | S | Day / Week / List; Book panel (slot hold); Appointment panel; reschedule mode; move confirmation |
| `/schedule/leave` | S | S | – | V | – | Empty form only |
| `/patients/new` and `?mode=quick` | S | V | D (role message, not "not found") | S | – | Duplicate block with three choices; registered confirmation |
| `/patients/directory` | V | V | V | S | – | Search; filters |
| `/patients/directory/merge` | S | S | – | – | – | Empty only. Approve and undo **untested** |
| `/patients/directory/:id` (desk record) | S | V | – | – | – | Consents list; visits; possible-duplicate search |
| `/patients/directory/:id/complete` | S | S | – | – | – | Form only |
| `/patients/:id` (Patient 360) | V | D | S | D | S | Add medicine dialog; section filter buttons |
| `/patients/:id/timeline` | S | – | S | – | – | – |
| `/patients/:id/docs` | S | S | – | – | – | Empty only |
| `/consult/:id` | V | D | S (**B**: banner loads, then two red errors) | – | – | One page / Guided steps; tools tabs Assist, Chart, Checklist; Add medicine dialog; Sign dialog; step-up round trip; ended; signed; addendum; conflict alert (second tab) |
| `/consult/:id/problems` | V | – | – | – | – | ICD search; added row |
| `/consult/:id/medicines` | V | – | – | – | – | Empty ("No active medicines to reconcile") only |
| `/consult/:id/rx/print` | V | – | – | – | – | Not authorised message; printed layout; share form |
| `/consult/:id/note/print` | V | – | – | – | – | Signed note with version history |
| `/consult/:id/slip` | V | – | – | – | – | "Nothing has been placed yet" only |
| `/consult/:id/referrals/:id/letter` | C | – | – | – | – | **untested** |
| `/nurse`, `/nurse/:appointmentId` | – | – | V with the setting on · D with it off | – | – | Queue table; intake; flagged vitals; escalate form; read-only after hand-off |
| `/settings?tab=…` (17 tabs) | S | D | D | V | IT S | Add-staff wizard step 1; offboard dialog; display created (one-time link) |
| `/admin/staff` (invite by URL) | S | – | – | S | – | **untested** |
| `/organisation` | S | – | – | S | – | Local stack answers 404 from the API |
| `/account/devices`, `/more` | S | V | S | S | S | – |
| `/queue/tv#token` (public TV) | – | – | – | V (opened the link) | – | "Now calling" empty; "Next" list |
| `/verify/rx/:token`, `/verify/paper/:token`, `/rx/:token`, `/invite/:token` | C | – | – | – | – | **untested** |
| `/inbox`, `/billing`, `/messages`, `/care`, `/referrals`, `/devices`, `/video` | D | – | – | – | – | Flags off: all answer "Page not found". No "coming soon" is shown because the menu items are hidden too |

Notes on the inventory:

- A denied route and a mistyped route look the same: "Page not found. The link may be old." (A-21).
- `/schedule` "Print day sheet" calls the browser print dialog. Headless, nothing was produced: **untested** output.

---

## 3 · Workflows: gold path, observed path, gap

### 3.1 Receptionist

**Gold path for a walk-in.** Type the phone number. If the patient exists, one tap picks them, showing enough to tell two family members or two same-name patients apart (name, age, date of birth, last visit). If not, four fields. Doctor defaults sensibly. One action issues a token that the patient can hold (printed slip or SMS), numbered in the order patients will actually be called. The card shows token, doctor and wait. Mistakes are reversible: undo a check-in, mark "left without being seen", reissue a token.

**Observed.**

| Task | Clicks | Typed fields | Route changes | Scroll | Result |
|---|---|---|---|---|---|
| Walk-in, new patient: board → token | 6 | 3 | 3 | 0 px | Token B-01 on the success screen |
| Walk-in, returning patient by last 4 phone digits | 3 | 1 | 1 | 0 px | Token B-02 |
| Check in a booked patient from the board (identity confirm) | 2 | 0 | 0 | 0 px | Token A-10 |
| Book an appointment from the schedule | 4 | 1 | 1 | 188 px | Booked, reference shown |
| Reschedule to the next day | 5 | 0 | 1 | 92 px | "Moved. The old slot is free again." with a move history |
| Move a waiting patient to another doctor | 7 | 0 | 1 | 494 px | Moved; token unchanged |
| Add to waitlist | 4 | 1 | 1 | 162 px | Added; an offer appears when a slot fits |
| Walk-in with a chosen doctor, then Send to nurse | 10 | 3 | 4 | 60 px | Card in With nurse |

**What is good here.** The walk-in is short and needs no scrolling. Search works as you type on four phone digits. A same-day second visit is caught with a clear choice. Duplicate registration (same name and date of birth) is blocked until the desk chooses one of three actions, and a spelling variant with the same date of birth still matched. Booking holds the slot. Check-in asks the desk to confirm name and date of birth.

**Gaps.**

- Two same-name records look identical in the picker (A-05).
- After check-in there is no undo, no cancel and no "left without being seen" (A-06).
- The desk can move a visit to In consult and Done with two clicks and no doctor (A-06).
- The token cannot be printed, reissued or cancelled (A-12). The success copy names the wrong column (A-22).
- A follow-up the doctor sets never reaches the desk (A-07).
- No end-of-day function exists. Day close is ticketed in P4 (MDV-P4-012): **absent feature, planned**.

**Tokens, precisely.**

| Aspect | Status |
|---|---|
| Generation | Works. Issued at check-in, in the same transaction (code and browser). |
| Numbering | Letter per doctor calendar per day ("A", "B"), number per letter. First doctor to receive a check-in that day gets "A". |
| Uniqueness | Unique per facility, day, letter and number; a database lock and a unique index back it (code only, not load-tested by me). |
| After reassignment | The token is kept. A patient moved from Dr. Menon to Dr. Rao keeps B-01; a patient moved the other way keeps A-09. **Verified**; the letter then no longer says which doctor (A-12). |
| Calling order | The TV's "Next" list and the doctor's Today are ordered by slot time, not token number. The TV showed A-05, B-02, A-06, A-09, A-07, A-08. **Verified** (A-12). |
| Reissue | **Absent feature.** |
| Cancellation | **Absent feature.** A checked-in visit cannot be cancelled anywhere; the schedule says "The visit has started. Finish it on the front desk board." |
| Print | **Absent feature.** The token appears on the success screen, the card, the overview table and the printable day sheet. No slip, no SMS. No ticket found for a token slip. |
| Per-doctor reset | Per day by design. Next-day reset: **untested** (fixed clock). |

### 3.2 Nurse

**Gold path.** A queue of who is waiting for me, longest wait first. Open one; the patient's identity stays in view the whole time. Enter vitals on a number pad, with previous values beside them. Abnormal values are flagged once and need one deliberate confirmation. Allergies: confirm "none" or add. Escalate in one step, and the doctor sees it at the top of their list. Hand off; correct a typo afterwards if needed.

**Observed (1024×768 touch).** Landing on `/nurse` when the setting is on. Open intake: 1 click. Then 10 typed fields and 10 clicks to record six vitals, keep four flagged values, add one allergy and escalate; 1 more click to hand off. Scroll 1,528 px on a 1,778 px page.

**What is good here.** Numeric keypad hints on every vital. "No known allergies" is a deliberate button, never a default, and hand-off is locked until allergy status is set. Reversed blood pressure is refused in words. Escalation needs a reason. After hand-off the page is read-only and says so.

**Gaps.**

- The identity banner scrolls away (A-01).
- The chief complaint the nurse writes is not shown to the doctor (A-09).
- The escalation does not move the patient up the doctor's list (A-08).
- Each flagged value needs its own "Keep this value" tap; four taps for one febrile, hypertensive patient (A-18).
- No correction after hand-off by the nurse (A-18).
- With the setting off, the nurse role has no vitals screen at all and lands on a reception board labelled "Today" (A-20).
- Medicines reconciliation by the nurse and "My tasks" from the sitemap: **absent feature** (design phase M2; not checked against tickets).

### 3.3 Doctor

**Gold path.** Today shows who is next in the order patients are really seen, with anything urgent at the top. One action opens the visit. Identity and allergies stay pinned. The nurse's complaint and vitals are already in the note. The doctor writes complaint to plan on one screen without hunting, picks a diagnosis without leaving, prescribes with dose, frequency and duration, and is stopped by an allergy conflict whatever way the medicine was entered. One identity confirmation covers prescription and note. The system will not let the patient leave with a half-finished prescription unnoticed. The follow-up lands with the desk.

**Observed (1440×900).**

| Step | Clicks | Typed fields | Route changes | Scroll | Notes |
|---|---|---|---|---|---|
| Today → Start consultation | 1 | 0 | 1 | – | No identity step; no dialog |
| Note: complaint, subjective, objective, assessment, plan | 0 | 5 | 0 | 3,263 px (with the three tool tabs I opened) | First note field is at 1,138 px |
| Add one diagnosis | 4 | 1 | 2 (leave and return) | 0 | Separate page |
| Reconcile medicines (nothing to reconcile) | 2 | 0 | 2 | – | Separate page |
| Add one medicine through the dialog | 6 | 1 | 0 | 0 | Chips for frequency, timing, duration |
| Order one test, set follow-up, write advice | 5 | 2 | 0 | 3,514 px | Orders sit in a closed disclosure near the bottom |
| Authorise prescription with step-up | 4 | 0 | 2 (identity provider and back) | 1,071 px | Returns to the top of the page |
| Sign note (inside the 5-minute window) | 2 | 0 | 0 | 829 px | No second step-up |
| End consultation | 1 | 0 | 0 | – | No dialog |
| Addendum after signing | 2 | 2 | 0 | 2,548 px | Signed as version 2 |

For one simple visit (three note fields, allergy confirm, one diagnosis, one medicine, one test, follow-up, advice, authorise, sign, end) the segments sum to about **24 clicks, 9 typed fields, 5 route changes** and more than **9,000 px of scrolling** as scripted. A real doctor will scroll less than a script that jumps between sections, but the page length is real.

**Vertical space.** Measured on a patient who came through nurse intake.

| Measure | 1440×900 | 1024×768 |
|---|---|---|
| App header | 0–60 px | 0–60 px |
| Identity banner | 84–176 px | 84–176 px |
| Buttons row, hint line, "Consultation note" heading, recording line | 192–360 px | 192–360 px |
| Section chips | 368–424 px | 368–424 px |
| First section card starts | 440 px (49% of the viewport) | 440 px (57%) |
| First note field (Chief complaint) | 1,138 px, below the first screen | 1,138 px |
| Sticky "Review and sign" bar at the bottom | about 100 px | about 100 px |
| Tools panel (Assist, Chart, Checklist) | right column from 440 px | drops to 4,104 px, the bottom |
| Prescription section starts | 4,226 px | not measured |
| Page height | 4,801 px | 5,347 px |

The section chips and the sign bar are sticky. The identity banner is not.

**What is good here.** One click starts the visit. Autosave works and says so; "Saved" appeared 1,851 ms after typing (script-measured, one sample). A second tab on the same note gets "This note was changed elsewhere. Your text is kept here and is not saved yet." A second consultation while one is open is challenged. Amoxicillin for a penicillin-allergic patient is blocked in the dialog and at authorisation, with a logged override path. "Allergies not recorded" itself raises a severe alert, so unknown is never treated as none. Signing shows name, MRN and date of birth again. The signed note is read-only and the addendum is a new signed version. One step-up covered prescription, note and addendum.

**Gaps.** A-01, A-02, A-03, A-04, A-08, A-09, A-10, A-13, A-14, A-15, A-16, A-17 below.

### 3.4 Owner or manager

**Gold path.** One screen: today's bookings, arrivals, waits, who is behind; this week's visits per doctor, no-shows, new versus returning patients, and money.

**Observed.**

- The board's "By doctor" view gives today's counts per state per doctor, the next three patients and a waiting-room table with waits. This is useful and live.
- Settings → Practice → Pilot metrics gives four 14-day numbers: check-in time, no-show rate, double bookings refused, message delivery.
- Nothing shows visits per day, new versus returning, average wait, consultation length, or revenue. The owner dashboard is ticketed for P4 ("Clinic reports and owner dashboard"): **absent feature, planned**.

**Decisions an owner can make today:** rebalance a queue now; see whether someone is waiting too long now. **Decisions they cannot make:** staffing, hours, which doctor is overloaded across a week, whether no-shows are falling. See A-19.

### 3.5 Admin

**Observed.** Settings has five groups and 17 tabs: profile, locations, hours, holidays, queue displays, pilot metrics, terms; staff, doctors; nurse intake, medicines; messages; access requests, who opens records, access review, audit log, data.

| Task | Status |
|---|---|
| Invite staff | Three-step wizard exists; step 1 role cards are clear. Not submitted: **untested** beyond step 1. |
| Change a person's role | **Absent feature** in the staff table (only "Offboard"). Custom roles exist. |
| Offboard | Dialog explains the effect, needs a reason, and for a doctor asks what happens to booked patients. Code refuses the last administrator. Not submitted: **untested**. A practice admin is offered "Offboard" on the owner doctor (A-23). |
| Doctors and licences | Owner attestation shown with date; "Correct licence" action. |
| Hours, holidays, leave | Forms exist. Leave says affected visits are listed before saving. Not submitted: **untested**. |
| Formulary | 23 generic rows in the starter list, no brand names, with a warning that the list is a draft (A-02). |
| Audit log | Filters, patient search, CSV export. Half the action names are raw codes (A-24). |
| Queue displays | Create gives a one-time link; "Revoke" exists. 1 click and 1 field. |
| Data export | Placeholder: "Coming in a later phase." **Absent feature, stated honestly.** |
| Nurse intake switch | 4 clicks from the landing page; no confirmation; takes effect at once (A-25). |

### 3.6 Cross-role handoff

| Handoff | What the receiver sees | Gap |
|---|---|---|
| Desk → nurse | Card in With nurse; nurse queue row with wait and token | None found |
| Nurse → doctor | Vitals with who measured them and "Flagged high by the nurse"; allergy in the banner; escalation text on the Today row | Complaint text missing (A-09); escalation not prioritised (A-08) |
| Doctor → desk, during the consult | Card shows "Called to Room 3" and a "Consult done" button | The desk can close the visit under the doctor (A-06) |
| Doctor → desk, after | Card moves to Done on "End consultation" | Follow-up, prescription state and billing are invisible (A-07, A-04) |
| Two people, same note | Second writer is told and keeps their text | Good |
| Two people, same visit state | Desk action wins silently; the doctor's screen does not change | A-06 |
| Stale state on one screen | Note field shows old or empty text after a quick page change | A-03 |

---

## 4 · Findings

Severity: **P0** safety or blocker · **P1** critical workflow · **P2** major usability · **P3** polish. Screenshot paths are under `docs/product-ux-audit/screenshots/local-stack/walkthroughs/` unless they start with `local-stack/` or `azure-dev/`.

### A-01 · The patient identity banner scrolls away on the consultation and the nurse intake

- **Where:** `/consult/:id` (doctor, 1440 and 1024), `/nurse/:id` (nurse, 1024). All states.
- **Severity:** P1 (safety-related). **Label:** verified defect. **Observed.**
- **Evidence:** `doctor/consult/05-consult-scrolled-1600.png`, `nurse/intake-tablet/05-vitals-flagged.png`; log lines "banner sticky? static" and "anything naming the patient or allergy in viewport at 1600px? NOTHING".
- **Problem:** The banner is not pinned. The consultation is 4,800 to 5,350 px tall; the prescription table starts at 4,226 px. While the doctor adds medicines in that table, nothing on screen names the patient or the allergy. The section chips and the sign bar are pinned; identity is not.
- **Consequence:** Wrong-patient and missed-allergy risk when the doctor is interrupted and returns to a scrolled page. NISTIR 7804-1 guidance 1.2: identification "shall continue to be displayed in the same location regardless of scrolling". NISTIR 7804 lists "patient identification error" first among use-error root causes.
- **Reproduce:** Start any consultation; scroll to Prescription.
- **Remedy:** Pin a one-line strip (name, age and sex, MRN, allergy summary) under the app header on the consultation, its sub-pages and the nurse intake. Collapse the full banner into it on scroll.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** pilot doctor, nurse.
- **Mitigations that exist and must stay:** the Add medicine dialog and the Sign dialog repeat the patient; one open consultation at a time is challenged.

### A-02 · A medicine typed as free text skips the allergy check, and the formulary has no brand names

- **Where:** `/consult/:id` Prescription section, doctor, 1440.
- **Severity:** P0 (safety) until a clinician decides otherwise. **Label:** verified defect for the missing check; hypothesis for how often it will be hit.
- **Observed:** For a patient with a recorded penicillin allergy I typed "Augmentin 625 Duo". No formulary match. "Add as free text" added the line with "Attention · uncertain. Free-text medicine, not from the formulary… left out of interaction checks" and, lower down, "typed as free text, so not checked for allergies, interactions or dose". No severe alert, no reason asked. The formulary holds 23 generic rows and the Brands column is empty. **Inferred:** once a dose is filled in, authorisation proceeds (I did not complete that line; the only refusal I saw was the missing dose).
- **Evidence:** `doctor/consult-sign/03-free-text-augmentin.png`; `admin/settings/09-medicines.png`; log "matches for a brand name:" (empty).
- **Problem:** The safety net covers only formulary picks. Indian outpatient prescribing is largely by brand, so a pilot doctor is likely to type brands on the first day and get free-text lines.
- **Consequence:** A penicillin-class brand can reach an authorised prescription for an allergic patient with an amber note as the only signal. The honest "not checked" wording is good, but it is the same wording for every free-text line, so it will be read past.
- **Reproduce:** Consultation for a patient with a penicillin allergy → Prescription → type a brand → Add as free text.
- **Remedy:** (1) When the patient has any recorded allergy, a free-text line needs an explicit acknowledgement that names the allergy ("Not checked against: Penicillin"). (2) Load brand names for the pilot clinic's top medicines before go-live, mapped to generics. (3) Offer "Did you mean…" generic matches on free text.
- **Dependency:** backend, frontend, clinical validation, product decision. **Confidence:** high on behaviour, medium on frequency. **Validation owner:** pilot doctor.

### A-03 · After a quick page change and back, the note shows old or empty text although the new text was saved

- **Where:** `/consult/:id` ↔ `/consult/:id/problems`, doctor, 1440.
- **Severity:** P1. **Label:** verified defect (reproduced 2 of 2). **Observed.**
- **Evidence:** `doctor/consult-2/02-after-quick-route-change.png`; log: `typed "SECOND VERSION…", left for Problems at once, came back: screen shows ""; after a reload (server) "SECOND VERSION dengue suspected (synthetic)"`. In the first run the screen showed the previous text, and the signed note later carried the text the screen had not shown.
- **Problem:** Typing, then opening "Problems and diagnoses" within about two seconds, then returning: the field shows what the page had cached, not what the server holds. The edit is safe on the server; the screen says otherwise.
- **Consequence:** The doctor believes text was lost and retypes, or reviews a note that is not the one that will be signed. NISTIR 7804 calls this class a data accuracy or feedback error. The design invites it: diagnoses and reconciliation live on other pages, so doctors change page mid-note all the time.
- **Reproduce:** Type in Assessment; at once click "Problems and diagnoses"; click "Back to the consultation"; compare with a reload.
- **Remedy:** Refetch (or invalidate the cached note) when the consultation page mounts, and update the cache with the flushed text when leaving.
- **Dependency:** frontend. **Confidence:** high on the symptom; the cause is a hypothesis. **Validation owner:** engineering.

### A-04 · A consultation can be signed and ended with an unauthorised draft prescription, with no warning and no way back

- **Where:** `/consult/:id`, doctor, 1440. Then `/today`.
- **Severity:** P1. **Label:** verified defect. **Observed.**
- **Evidence:** `doctor/consult-2/12-ended-with-draft-rx.png`, `13-rx-after-end.png`, `14-today-notes-to-sign.png`, `doctor/consult-sign/18-addendum-after-sign.png` ("This consultation has ended. The draft prescription can be read, not changed." · "Draft, not authorised").
- **Problem:** "End consultation" is one click with no dialog. With a medicine on the draft, it ends anyway. Afterwards the lines are read-only, no reopen exists, and Today lists "notes to sign" but nothing about unauthorised prescriptions. In one of my runs the draft held a line with no dose, which can now never be completed or authorised.
- **Consequence:** The patient leaves with no valid prescription and nobody is told. The print page then says "Only an authorised prescription prints".
- **Reproduce:** Add a medicine; do not authorise; End consultation.
- **Remedy:** On End (and on Sign), if a draft prescription or unplaced orders exist, ask: authorise now, discard, or end anyway. Add "prescriptions to authorise" beside "notes to sign" on Today. Allow finishing a draft after the visit has ended.
- **Dependency:** frontend, backend, product decision. **Confidence:** high. **Validation owner:** pilot doctor.

### A-05 · Two records with the same name cannot be told apart when picking a patient

- **Where:** Walk-in, booking and waitlist pickers (receptionist); header search (doctor). 1440.
- **Severity:** P1. **Label:** verified defect for the picker content; the wrong pick itself is a usability observation.
- **Observed:** Searching "0102" returned two rows, both "Deepak Nair (synthetic) · 47 y · Male · Phone ··0102", differing only in "MRN ··0004" and "MRN ··4483". My script, like a hurried person, picked the first: the duplicate created earlier, not the original chart. The doctor's header search shows only "DN · 47 y · MRN ··0004" and "DN · 47 y · MRN ··4483".
- **Evidence:** `receptionist/repeat-by-phone/01-search-4-digits.png`, `doctor/misc/02-global-search.png`.
- **Problem:** Rows show age, not date of birth, and no last-visit date or "possible duplicate" mark. The duplicate block at registration can be bypassed with "Request merge review", which then creates the second record; from then on both appear everywhere until a clinician merges them.
- **Consequence:** Visits, vitals and prescriptions split across two charts; the allergy recorded on one is missing on the other. A wrong-patient or data availability error in NISTIR 7804 terms.
- **Reproduce:** Walk-in → type 0102.
- **Remedy:** Show date of birth and last visit in every picker row; mark records with an open merge request and name the older one "main record"; sort the main record first.
- **Dependency:** frontend, backend (last visit), product decision on masking. **Confidence:** high. **Validation owner:** receptionist, pilot doctor.

### A-06 · Visit status moves forward only; the desk can close a visit without the doctor, and nobody can undo

- **Where:** `/front-desk` cards, receptionist (and nurse, doctor). 1440.
- **Severity:** P1. **Label:** verified defect (no undo); usability observation (who may close).
- **Observed:** After check-in the card offers only "Call in". "Call in" puts the visit In consult with "Called to Room 3" in one click, no confirmation. "Consult done" then puts it in Done. I did both as the receptionist with no consultation started. While a doctor was mid-consultation, the desk pressed "Consult done": the card went to Done, the doctor's screen showed nothing, and the consultation stayed open. There is no undo, no cancel after check-in and no "left without being seen". The schedule panel says "The visit has started. Finish it on the front desk board."
- **Evidence:** `receptionist/board-checkin-callin/04-call-in.png`, `06-desk-closed-consult.png`, `05-schedule-inroom-panel.png`, `handoff/desk-closes-mid-consult/01-desk-pressed-consult-done.png`, `doctor/consult-2/09-doctor-screen-after-desk-closed.png`.
- **Problem:** One mis-tap on a dense board is permanent. A walk-in who leaves must be pushed through "Call in" and "Consult done", which records a completed visit that never happened.
- **Consequence:** The board and the queue TV call the wrong token; completed-visit counts and waits are wrong; the doctor's Today shows "Done" for a patient not seen. WCAG 2.2 SC 3.3.4 asks that such submissions be reversible, checked or confirmed.
- **Reproduce:** Board → any Ready for doctor card → Call in → Consult done.
- **Remedy:** Add "Undo" for the last move (time-limited), "Left without being seen" and "Cancel visit" with a reason. Let "In consult" follow the doctor's Start, and "Done" follow End, when a doctor uses the consultation screen; keep desk buttons for clinics that do not.
- **Dependency:** backend, frontend, product decision. **Confidence:** high. **Validation owner:** receptionist, owner.

### A-07 · The follow-up the doctor sets never reaches the front desk

- **Where:** Consultation Plan → Follow-up (doctor); board, desk patient record, schedule (receptionist).
- **Severity:** P1. **Label:** verified defect against the ticket's own acceptance line.
- **Observed:** Choosing "2 weeks" shows "The front desk will book it from Tuesday, 17 November 2026." Afterwards the board, the Done card, the patient's desk record and the schedule for 17 Nov show nothing about it. Ticket MDV-P2-043 says "The follow-up choice appears on the front desk's list for that patient." The follow-up does print on the prescription.
- **Evidence:** `doctor/consult-2/05-follow-up.png`, `handoff/follow-up-at-desk/01-board.png`, `02-patient-record.png`.
- **Consequence:** The screen makes a promise that does not happen. Chronic-care patients are not rebooked unless they ask.
- **Reproduce:** Set a follow-up; end; sign in as receptionist; look for it.
- **Remedy:** Show "Follow-up due from 17 Nov · Book" on the Done card and the desk record, opening the booking panel on that date.
- **Dependency:** frontend, possibly backend. **Confidence:** high that the desk screens I opened lack it; I did not search every screen. **Validation owner:** receptionist, pilot doctor.

### A-08 · A nurse escalation and real arrival order do not shape the doctor's Today

- **Where:** `/today`, doctor, 1440.
- **Severity:** P1. **Label:** verified defect (ordering); usability observation (prominence).
- **Observed:** The 72-year-old with "Nurse escalation: High fever with BP 168/96" and "Abnormal vitals today" was row 11 of 11, at about 1,446 px, below the first screen. "Next patient" stayed on the 10:00 booking. Rows sort by slot time; a walk-in who arrived at 09:58 holds a nominal 19:00 slot, so she sorts last. Today shows no token numbers.
- **Evidence:** `doctor/consult/01-today.png`, `02-today-full.png`; log "position of our visit in the list: 11 of 11".
- **Consequence:** An urgent patient can wait behind ten others unless the nurse walks over. The doctor's order (slot time) differs from the token order patients believe in; the doctor cannot call "A-07" from this screen.
- **Reproduce:** Run `walkthrough-handoff.mjs`, then open Today.
- **Remedy:** Pin escalated visits above "Next patient" with the reason. Sort arrived patients by the clinic's chosen rule (token or arrival) and show the token on each row. Give walk-ins their arrival time, not a far slot time.
- **Dependency:** frontend, backend, product decision (queue rule). **Confidence:** high. **Validation owner:** pilot doctor, nurse.

### A-09 · The nurse's chief complaint does not appear in the doctor's consultation

- **Where:** `/nurse/:id` → `/consult/:id`.
- **Severity:** P2. **Label:** verified defect that it is not shown; whether it is stored is **untested**.
- **Observed:** The nurse typed "Fever and sore throat for three days" and saved. In the consultation, Chief complaint was empty and the text appeared nowhere on the page.
- **Evidence:** log "textboxes: … Chief complaint(required)=\"\"" and "occurrences of \"sore throat\" = 0".
- **Consequence:** The doctor asks and types again. WCAG 2.2 SC 3.3.7 (Redundant Entry) describes the same burden within one process; here it crosses two users.
- **Remedy:** Prefill Chief complaint with the nurse's text, marked "from nurse intake", editable.
- **Dependency:** frontend, maybe backend. **Confidence:** medium-high. **Validation owner:** nurse, pilot doctor.

### A-10 · The sign checklist marks "Identity confirmed (name and date of birth)" as done when nobody confirmed anything

- **Where:** Consultation → tools → Checklist.
- **Severity:** P1 (trust). **Label:** verified defect (code and browser).
- **Observed:** On opening a consultation, before any action, the checklist showed "Identity confirmed (name and date of birth) · Done". In `checklist.ts` the item is `identityShown`: the banner was displayed. "Every AI suggestion accepted or dismissed · Done" also shows when no AI exists.
- **Evidence:** log "checklist on open (nothing done by the doctor yet)"; `doctor/consult-2/01-open-allergies-unknown.png`.
- **Consequence:** The record of what was checked before signing overstates what happened. If this checklist is ever shown to an auditor, it claims a confirmation that was not made.
- **Remedy:** Either reword to "Patient shown: name, date of birth" or add a one-tap "Confirmed with the patient" at the start. Hide the AI line until AI drafting exists.
- **Dependency:** product decision, frontend, clinical validation. **Confidence:** high. **Validation owner:** pilot doctor.

### A-11 · The reception card does not name the doctor

- **Where:** `/front-desk` By status, all roles, 1440 and 1024.
- **Severity:** P2. **Label:** verified defect (two-doctor clinic).
- **Observed:** A card shows name, slot time, age and sex, tags, token, wait. No doctor. With two doctors the desk must infer from the token letter, which is wrong after a reassignment (A-12), or switch to "By doctor".
- **Evidence:** `receptionist/walkin-new/07-board-after.png`.
- **Remedy:** Add the doctor's short name to the card when more than one doctor works that day.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** receptionist.

### A-12 · Tokens: no paper, no reissue, and the order shown is not the order of the numbers

- **Where:** Walk-in success, board, overview, queue TV.
- **Severity:** P2. **Label:** absent feature (print, reissue, cancel); usability observation (order and letter).
- **Observed:** See the token table in 3.1. The TV listed A-05, B-02, A-06, A-09, A-07, A-08. Tokens B-01 and A-09 sat in the other doctor's queue after reassignment.
- **Evidence:** `admin/settings/queue-tv-1920.png`, `receptionist/reassign/`, `receptionist/walkin-new/06-checked-in-token.png`.
- **Consequence:** Patients holding A-07 watch A-09 go first and come to the desk. The desk must speak or write the token by hand.
- **Remedy:** Decide the queue rule with the pilot clinic (token order or slot order) and make the TV, the board and Today follow it. Add a small printable slip (browser print is enough) and "Reprint". On reassignment, say on the TV which doctor each token is for.
- **Dependency:** product decision, frontend, backend. **Confidence:** high. **Validation owner:** receptionist, owner.

### A-13 · The consultation page is very long, and the first note field is below the first screen

- **Where:** `/consult/:id`, doctor, 1440×900 and 1024×768.
- **Severity:** P2. **Label:** usability observation, measured.
- **Observed:** See the table in 3.3. Chief complaint starts at 1,138 px. At 1024 the Chart and Checklist panel sits at 4,104 px. Orders and the prescription table are near the bottom. Diagnoses and reconciliation are on separate pages.
- **Evidence:** `doctor/consult/03-consult-top.png`, `04-consult-full.png`, `doctor/consult-tablet/01-consult-1024.png`, `02-consult-1024-full.png`.
- **Consequence:** Much scrolling per patient, and the context a doctor glances at (chart, vitals) is far from where they type on a tablet.
- **Remedy:** Without a rewrite: collapse Vitals and Allergies to a summary line once the nurse has recorded them; put the buttons row and hint into the pinned strip from A-01; keep the tools panel reachable on tablet (a drawer); move Prescription directly under Plan; let a diagnosis be added inline.
- **Dependency:** frontend, design. **Confidence:** high on measurements. **Validation owner:** pilot doctor.

### A-14 · After confirming identity, the doctor lands at the top of the page and must find the button again

- **Where:** Authorise prescription → identity provider → back.
- **Severity:** P2. **Label:** verified defect. **Observed:** After the round trip `scrollY` was 0 and the prescription section was out of view; "Authorise prescription" had to be pressed a second time. Log: "back from step-up: scrollY=0 rx in view=false".
- **Remedy:** Return to the section that asked, and complete the pending action or focus its button with a line saying "Identity confirmed. Press Authorise."
- **Dependency:** frontend. **Confidence:** high with the dev provider; real Entra **untested**. **Validation owner:** engineering, pilot doctor.

### A-15 · A reload within about two seconds of typing loses the last edit

- **Where:** Consultation note. **Severity:** P2. **Label:** verified defect (one reproduction).
- **Observed:** Typed into Plan, reloaded at once: the earlier text came back. Leaving by an in-app link did save (A-03 is the display side of that).
- **Consequence:** A browser crash, an accidental refresh or a session expiry right after typing drops the last sentence, silently.
- **Remedy:** Flush on `pagehide`/`visibilitychange` with `keepalive`, and keep a local unsent draft per note.
- **Dependency:** frontend. **Confidence:** medium-high. **Validation owner:** engineering.

### A-16 · The signed note's print view leaves out vitals, allergies, diagnosis and medicines

- **Where:** `/consult/:id/note/print`. **Severity:** P2. **Label:** usability observation; needs clinical validation.
- **Observed:** The print showed identity, Chief complaint, Subjective, Objective, Assessment, Plan and version history. Not the nurse's vitals (168/96, 39.2 °C), the penicillin allergy, the coded diagnosis J03.9 or the medicines. Vitals enter the note only if the doctor presses "Add to Objective". The print heading has no clinic name or doctor licence line in the text I captured.
- **Evidence:** `doctor/consult-sign/14-note-print.png`.
- **Consequence:** The legal record of the visit may be thinner than the doctor believes.
- **Remedy:** Ask the pilot doctor what a visit record must hold; add vitals, allergies and diagnoses by default.
- **Dependency:** clinical validation, backend. **Confidence:** medium. **Validation owner:** pilot doctor.

### A-17 · Small touch targets inside the consultation on a tablet

- **Where:** `/consult/:id` at 1024 touch. **Severity:** P2. **Label:** verified defect (metric).
- **Observed:** `local-metrics.jsonl` records 14 targets under 24 px on the consultation (samples "BUTTON 32x18", "INPUT 32x18"), and 8 on Today (patient-name links 19 px tall). WCAG 2.2 SC 2.5.8 sets 24 by 24 CSS pixels, with exceptions I did not assess one by one; the project's own rule is 44 px.
- **Remedy:** Enlarge the switch and link hit areas. **Dependency:** frontend. **Confidence:** medium (exceptions not assessed). **Validation owner:** engineering.

### A-18 · Nurse intake: one tap per flagged value, and no correction after hand-off

- **Where:** `/nurse/:id`, 1024 touch. **Severity:** P2. **Label:** usability observation.
- **Observed:** BP 168/96, pulse 104 and temperature 39.2 each raised "High · re-check" and each needed "Keep this value" (four taps) before hand-off. After hand-off: "Readings cannot be changed here." The ranges still await clinician sign-off (issue #956, from project notes).
- **Evidence:** `nurse/intake-tablet/05-vitals-flagged.png`, `09-intake-reopened-after-handoff.png`.
- **Consequence:** For sick patients, the ones that matter, the nurse taps most. Habitual tapping weakens the check.
- **Remedy:** One "I re-checked these readings" for all flags, with the option to re-measure one. Allow the nurse a correction with a reason until the doctor starts.
- **Dependency:** clinical validation, frontend, backend. **Confidence:** medium. **Validation owner:** nurse.

### A-19 · The owner cannot see the business, and one pilot metric misleads

- **Where:** Settings → Pilot metrics; board overview. **Severity:** P2. **Label:** absent feature, planned (dashboard, P4); verified defect (metric).
- **Observed:** "Check-in time, returning patients (median): 0 s. Target of 60 s met." with "3 returning of 13 timed check-ins". A walk-in is created and checked in by the same action, so its timed check-in is zero.
- **Evidence:** `local-stack/doctor/settings-metrics/1440-default-full.png`; admin log.
- **Consequence:** The one number meant to prove the desk is faster will always look perfect. The owner has no daily counts.
- **Remedy:** Time check-in from opening Walk-in (or the first search keystroke) to the token. Before P4, add one small "Today and last 7 days" table: visits per doctor, walk-in versus booked, no-shows, median wait.
- **Dependency:** backend, product decision. **Confidence:** high. **Validation owner:** owner.

### A-20 · With nurse intake off, the nurse has no clinical screen and two menu items open the same board

- **Where:** Nurse, `/today`, `/front-desk`, `/nurse`. **Severity:** P2. **Label:** usability observation; partly by design.
- **Observed:** The nurse lands on "Today", which is the reception board with "Front desk" as its heading; "Front desk" in the menu opens the same board. `/nurse` is "Page not found". The nurse can press "Intake done" and "Call in" but cannot record a vital anywhere. Opening a consultation shows the banner and then two red lines: "Your role does not allow notes on this consultation." and "The note could not be opened." The permission matrix lists Clinical notes as Read for a nurse.
- **Evidence:** `local-stack/nurse/today/1440-default.png`, `local-stack/nurse/consult/1440-default.png`, `nurse/flag-off/`.
- **Remedy:** Hide "Today" for roles that only get the board. Decide whether a clinic with a nurse but without the intake step is a real case; if so, give the nurse the vitals form. Show one calm message, not two errors. Align the matrix and the behaviour.
- **Dependency:** product decision, frontend. **Confidence:** high. **Validation owner:** nurse, owner.

### A-21 · A screen the role may not open says "Page not found. The link may be old."

- **Where:** Receptionist `/today`, `/settings`, `/consult/:id`; admin `/today`; all flagged-off routes. Also on Azure dev (`azure-dev/receptionist/today-denied/1440-default.png`).
- **Severity:** P3. **Label:** usability observation. The nurse on `/patients/new` gets a proper role message, so the pattern exists.
- **Remedy:** "Your role does not include this screen" with the start-page button. **Dependency:** frontend. **Confidence:** high. **Validation owner:** engineering.

### A-22 · The walk-in success message names the wrong column

- **Severity:** P3. **Label:** verified defect. **Observed:** With nurse intake off the text says "The patient now waits in the Checked in column of the board"; the card is in Ready for doctor. **Evidence:** `receptionist/walkin-new/06-checked-in-token.png`, `07-board-after.png`. **Remedy:** Name the real column. **Dependency:** frontend. **Confidence:** high. **Validation owner:** engineering.

### A-23 · A practice admin is offered "Offboard" on the owner doctor; roles cannot be changed in place

- **Severity:** P2. **Label:** hypothesis (not submitted); absent feature (role change).
- **Observed:** As K. Lakshmi (practice admin) the owner's row has an active "Offboard" button and the dialog opens. Code refuses only the last administrator. A staff row has no other control.
- **Evidence:** `admin/settings/02-offboard-dr-arjun-rao.png`.
- **Consequence, if it goes through:** a hired admin can end the owner's access and move the owner's patients to "Needs rebooking".
- **Remedy:** Only an owner offboards an owner; ask for the step-up on any offboarding; add "Change role".
- **Dependency:** product decision, backend. **Confidence:** medium. **Validation owner:** owner, engineering.

### A-24 · The audit log mixes plain names with raw codes and fills with routine reads

- **Severity:** P2. **Label:** verified defect (labels); usability observation (noise).
- **Observed:** The action filter lists "Appointment booked" beside `encounter.end`, `rx.check`, `patient.chart.allergy-flag`, `note.pending`. One load of Today wrote a `patient.chart.allergy-flag` row per patient.
- **Consequence:** An owner asked "who opened this chart?" has to read developer codes among hundreds of rows.
- **Remedy:** Name every action; default the view to "opened or changed a chart", with routine list reads behind a filter. **Dependency:** frontend, translations. **Confidence:** high. **Validation owner:** owner.

### A-25 · Switching nurse intake on or off has no confirmation and applies mid-day

- **Severity:** P3. **Label:** usability observation. **Observed:** One tap, no dialog; the board's flow changes for everyone at once. Visits already "With nurse" stay there after switching off, and the desk then gets an "Intake done" button. **Remedy:** Confirm, and say what happens to patients in the nurse column. **Dependency:** frontend. **Confidence:** medium. **Validation owner:** owner.

### A-26 · The Terms tab says booking is paused while booking works

- **Severity:** P3. **Label:** hypothesis. **Observed:** Settings → Terms showed "Not accepted yet. Booking and messages are paused." I booked, rescheduled and checked in throughout. The local stack may bypass the rule, or the status line may be wrong; I did not find out which. **Remedy:** Check on Azure dev. **Dependency:** engineering. **Confidence:** low. **Validation owner:** engineering.

### A-27 · The Azure dev header pushes "Sign out" off-screen at 768 and 375

- **Where:** `azure-dev/receptionist/front-desk/768-default.png`. **Severity:** P2 on that deploy. **Label:** verified from the screenshot; local `main` does not show it (`local-stack/receptionist/front-desk/375-default.png`).
- **Remedy:** Deploy current `main` to Azure dev and re-capture before the pilot. **Dependency:** engineering. **Confidence:** high. **Validation owner:** engineering.

---

## 5 · Product-completeness gaps

| Area | Status | Basis |
|---|---|---|
| Billing, invoices, receipts, day close | **Absent, planned** (flag `billing.invoices` off; P4 tickets) | The board's "Billing" column and "Take payment" strings exist but are hidden |
| Owner dashboard and reports | **Absent, planned** (P4) | A-19 |
| Results inbox, care plans, messages, patient portal, video, referrals menu | **Absent, planned** (flags off) | Routes answer "Page not found" |
| Data export | **Absent, placeholder shown honestly** | Settings → Data |
| Token slip print, reissue, cancel | **Absent, no ticket found** | A-12 |
| Undo check-in, left without being seen, cancel a started visit | **Absent, no ticket found** (not searched exhaustively) | A-06 |
| Change a staff member's role | **Absent** | A-23 |
| Follow-up reaching the desk | **Broken** against MDV-P2-043 | A-07 |
| Brand names in the formulary | **Absent data** | A-02 |
| Nurse medicines reconciliation, nurse tasks | **Absent**; the nurse can list patient-reported medicines | Sitemap M2 |
| Reopen an ended consultation, finish a draft prescription later | **Absent** | A-04 |
| Diagnosis inline in the note | **Merely difficult** (separate page, 4 clicks, 2 route changes) | 3.3 |
| Orders | **Merely difficult** (closed disclosure near the bottom) | 3.3 |
| Doctor's patient search | **Merely difficult** (initials only until a chart is opened) | A-05 |
| WhatsApp reminders, SMS code for the patient's prescription link | **Blocked on people work** (DLT, provider), per the journeys file | Not tested |

---

## 6 · Clinical-safety review

| Risk | What protects today | What is missing | Finding |
|---|---|---|---|
| Wrong patient at the desk | Name and date of birth confirm at check-in; duplicate block at registration | Same-name rows are indistinguishable; duplicates persist until merged | A-05 |
| Wrong patient in the consultation | Full banner at the top; dialogs repeat the patient; second-consultation challenge; opaque ids in URLs | Banner not pinned; no identity step; checklist claims one | A-01, A-10 |
| Allergy missed | Banner line; unknown is never "none"; severe alert with reason; blocked authorisation; allergy printed on the prescription | Free text skips the check; banner out of view while prescribing | A-02, A-01 |
| Unsafe defaults | Vitals blank unless measured; exam systems blank unless marked; "no known allergies" needs a tap | Walk-in defaults to "Next available doctor" (fine for a clinic; ask the pilot); visit type guessed | – |
| Irreversible actions | Sign has a dialog with identity; cancel prescription needs a reason; merges can be undone by an admin | Call in, Consult done, End consultation, Send to nurse are single taps with no undo | A-06, A-04 |
| Concurrent edits | Note conflict is detected and the second writer keeps their text | Visit state: last tap wins silently; stale text after a quick page change | A-06, A-03 |
| Urgent patient overlooked | Escalation text and "Abnormal vitals today" tag on the row | No reordering, below the first screen | A-08 |
| Lost work | Autosave with a visible "Saved"; flush on in-app navigation | Reload inside the delay; no local draft | A-15 |
| Privacy on the waiting-room TV | Tokens and waits only; no names, initials or MRN; link is one-time and revocable | – | Good |
| Privacy at the desk | Receptionist routes to charts and consultations are refused; search masks MRN and phone; "Show full numbers" is logged | The board shows full names to anyone who can see the monitor (a placement matter for the clinic) | – |
| Privacy on printouts | Prescription carries identity and allergies as it should; day sheet is specified to omit phone and IDs | Day-sheet output **untested** | – |

I found no case where colour alone carried allergy or status (WCAG 2.2 SC 1.4.1); the tags carry words and icons.

---

## 7 · What is good and must not be broken

1. **The walk-in path.** Six clicks and three fields for a new patient, three clicks for a returning one, no scrolling. Do not add steps.
2. **Phone-digit search as you type**, and the same-day second-visit warning.
3. **The duplicate block** with three explicit choices, and matching that survives a spelling variant.
4. **"Unknown is not none" for allergies**, end to end: banner wording, the deliberate "Confirm: no known allergies", the severe alert when allergies are not recorded, hand-off locked until set.
5. **The Add medicine dialog.** Patient and allergy at the top, chips for frequency, timing and duration, the instruction sentence built for the doctor, the block with a logged override.
6. **Honest safety wording.** "Dose not checked", "not fully checked" and "About these checks" say what the system did not do.
7. **Note conflict handling** and the second-consultation challenge.
8. **Signing.** Identity in the dialog, read-only afterwards, addenda as signed versions, one step-up covering prescription and note.
9. **The nurse intake form.** Numeric keypads, previous readings beside each field, plain-word validation.
10. **The queue TV.** Tokens only.
11. **The reschedule flow** with its "From … to …" confirmation and move history.
12. **Server-side refusals** for roles, and the plain "Coming in a later phase" where something is not built.

---

## 8 · Recommended direction

**Vote: A, surgical fixes**, with one bounded layout pass on the consultation page.

Reasons:

- The model underneath is sound. States, permissions, safety checks, audit and conflict handling are already server-side and careful. The serious findings are gaps at the seams (identity not pinned, free text outside the check, draft prescription at End, follow-up not delivered, forward-only visit states), not a wrong structure.
- The reception flow is already efficient by count. A redesign would risk the best part of the product.
- A solo founder before a pilot cannot afford option B or C. The fixes in A-01 to A-10 are each small and testable, and each maps to a journey step.
- The one place that needs more than a patch is the consultation page (A-13). That is a re-ordering and collapsing of existing sections plus a pinned strip, not new components. I would do it as a single ticket after A-01 to A-04.

Suggested order before a pilot: A-02, A-01, A-04, A-03, A-06 (undo and left-without-being-seen only), A-07, A-08, A-05, A-10. Then A-13, A-14, A-12 with the pilot clinic's answers.

---

## 9 · Items that need the pilot doctor's or staff's validation

| Question | Who |
|---|---|
| Do you prescribe by brand or generic? Which 100 medicines cover most of your day? (A-02) | Pilot doctor |
| Are patients seen in token order, arrival order or appointment order? Do booked patients jump walk-ins? (A-08, A-12) | Doctor, receptionist |
| Does the patient need a paper token? Is a spoken number enough? (A-12) | Receptionist, owner |
| Who marks a patient as "in the room" and "done": the desk or the doctor? (A-06) | Doctor, receptionist |
| How often do walk-ins leave before being seen, and how do you record it today? (A-06) | Receptionist |
| What must the visit record and the printed prescription contain for you: vitals, diagnosis, weight, qualification line, signature image? (A-16) | Pilot doctor |
| Is a tap to confirm identity at the start of each consultation acceptable? (A-10) | Pilot doctor |
| Are the vitals ranges right, and is one confirmation for all flags safe? (A-18) | Clinician sign-off, nurse |
| Should the nurse's complaint prefill the doctor's note? (A-09) | Doctor, nurse |
| Is the one-page note or "Guided steps" preferred on the tablet in the room? (A-13) | Pilot doctor |
| Which five numbers do you want every evening? (A-19) | Owner |
| May a practice admin remove a doctor or the owner? (A-23) | Owner |

---

## 10 · Subscores

| Score | Value | Evidence | Coverage |
|---|---|---|---|
| Workflow efficiency | **15 / 25** | Reception paths are short (6, 3 and 2 clicks; no scrolling). The consultation needs about 24 clicks, 5 route changes and several thousand pixels of scrolling for a simple visit, with the first note field below the first screen (A-13), a return-to-top after step-up (A-14), a retyped complaint (A-09) and diagnoses on another page. No undo costs the desk time on every mistake (A-06). | Good for reception, nurse and doctor at 1440 and 1024. Phone width and Arabic **untested**: provisional there. |
| Clinical safety and trust | **11 / 20** | Strong: allergy handling, blocked authorisation, honest "not checked" wording, conflict detection, signing and addenda, token-only TV. Weak: identity not pinned (A-01), free-text bypass (A-02), stale note text (A-03), draft prescription at End (A-04), indistinguishable duplicates (A-05), escalation not prioritised (A-08), checklist overclaim (A-10). | Provisional: no clinician reviewed this; real step-up, interactions beyond one allergy class, paediatric dosing and pregnancy **untested**. |
| Information architecture | **9 / 15** | Clear role menus and a sensible Settings grouping. Against that: "Today" means two different screens by role (A-20), denied equals not found (A-21), the consultation spreads one task over four routes and a closed disclosure, the card hides the doctor (A-11), the audit log speaks in codes (A-24). | Provisional for care coordinator, hospital doctor and hospital IT (screenshots only). |
