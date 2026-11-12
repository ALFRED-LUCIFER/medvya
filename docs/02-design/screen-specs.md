# Screen behaviour specs (from the hi-fi designs)

These rules were collected from the interactive logic and copy in the design files. They are the concrete source for ticket acceptance criteria. Each section names its design file. **Rules marked ⚠ were inferred from labels only**, because the Clinician App data script was truncated on import (see [docs/02-design/source/README.md](source/README.md)).

---

## Access & onboarding (F-01 … F-07) — `Medvya Access and Onboarding`

### F-01 Landing
Rebuilt to the 6 Nov 2026 design (`Medvya Web`, area web) by MDV-P1-053 (shell) and MDV-P1-054 (home).
- Shell: announcement bar · Medvya · Product and Solutions menus (with "By specialty") · Medvya Assist · Pricing · Security · region switch (India/UAE) · Sign in · Book a demo · Start free. On phones a Menu button holds the same links. Menus are `<details>`, so they work without JavaScript. Footer with Product, Solutions and Company links, the region switch and the language switch. App Store and Google Play links appear only when `PublicSite:AppLinks:AppStore` / `:GooglePlay` are configured.
- Home: hero ("Run your whole clinic from one calm screen.", two CTAs: Start your free trial, Book a demo) with an HTML product picture · a clinic day timeline · feature grid with "Compare plans" · Medvya Assist · Who it's for (solo, clinic, hospital) · patient app · "Live in a day. We move your data for you." · trust strip linking /trust · pricing teaser without prices linking /pricing · FAQ (`<details>`).
- Region-dependent copy (`.IN` / `.AE` keys): payment method (UPI or card), residency line, billing and insurance names, the payment FAQ answer.
- Product pictures use only the synthetic cast. No customer quotes until real, permitted ones exist.
- **No fake metrics.** The pricing page is a placeholder.

### F-02 Region and residency
- Copy: "Where do you practise?" Data is stored and processed in this country and **cannot be moved to the other region later without a new account and export**.
- Two cards:
  - India: "Azure Central India · DPDP Act · ABDM integration available"
  - UAE: "Azure UAE North · separate deployment · DoH / DHA / MOHAP integrations per emirate"
- CTA: "Continue in {region}". The choice locks the tenant to one data plane.

### F-03 Sign in and MFA (3 steps)
1. Email or mobile, then **Continue with passkey** (primary), or "Use a one-time code instead". Links: "Hospital staff? Sign in with SSO" and "Can't sign in?".
2. Six-digit code: "Sent to +91 •••• ••3146. Expires in 4:52." A wrong code shows "Incorrect code. 2 attempts left before a 15-minute lock."
3. "Signed in. Choose where you are working today." Facility picker lists each membership with its role, e.g. "Doctor · owner" or "Visiting consultant · hospital SSO".
- Required states (wireframe): wrong code, locked (15 min), passkey unsupported, SSO-enforced tenant.
- **6 Nov 2026 revision (MDV-P1-059, MDV-P1-060):** Medvya renders the sign-in start (work email, passed to Entra as a login hint; passkey and organisation sign-in start the same Entra flow), create account steps 1-2 (country, practice name, city; full name, email, doctor or clinic manager: they pre-fill the F-04 draft), invitation acceptance (F-06), choose location ("Where are you working today?", last used first, "Not you? Sign out") and signed out (F-05a, "You were signed out"). The code, passkey, MFA set-up, recovery and the states above are Entra pages, branded by `infra/entra/branding.sh`. Not built: "This is a shared computer" (no shared-device session policy on the server yet), the mobile number (recovery is Entra's), Help link and legal line (no pages yet).

### F-04 Organisation approval request, then set up while waiting (ADR-016; `Medvya Web`, redesigned 10 Nov 2026)
Replaces the 6-step solo-doctor wizard. Eight steps: four of the **approval request**, three of **setup while you wait**, one status step. Draft autosaves ("Draft saved 10:42"); a "Why we approve first" note says Medvya approves the organisation once and reviewers never see patient data.

| Step | Content |
|---|---|
| 1 of 4 Clinic or hospital | Two cards: Clinic ("including a doctor working alone; the owner registers it") and Hospital ("an authorised signatory registers it"). Clinic only: toggle "I am a doctor and the owner" (adds the council registration document). Specialties multi-select (for services and forms). |
| 2 of 4 Organisation details | Legal name, registered address, contact phone and email, country (fixed at sign-up, "can't be changed"), tax number with format check (India GSTIN, state code checked; UAE TRN 15 digits). Hospital adds authorised signatory and IT or security contact. |
| 3 of 4 Documents | Clinic: registration certificate, tax number, owner ID, owner's council registration (if doctor) = 4 or 5 items. Hospital: registration certificate, tax number, signatory ID, authorisation letter, IT contact, signed DPA = 6 items. PDF, JPG or PNG up to 10 MB, "stored privately in your country". India ID hint: mask the first 8 digits of Aadhaar. |
| 4 of 4 Check and submit | Review rows with ticks, missing count in red. Clinic: checkbox accepting Terms and DPA ("[LEGAL]" wording pending). Hospital: shows whether the signed DPA is uploaded. Confirmation that details are true and the person is authorised. Usual reply time "[SLA, to be decided]". CTA "Submit for approval". |
| Setup 1 of 3 Hours and services | Opening days, appointment length, services with fees (prefilled from specialty). |
| Setup 2 of 3 First team | "N of 3 starter places used, 1 doctor + 2 other staff"; at the cap an Invite is disabled with "Starter limit reached ... after Medvya approves". Invitees see only "waiting for approval". |
| Setup 3 of 3 Payments and messages | India UPI, WhatsApp, NHCX (later); UAE cards, WhatsApp. "Connect now; they start working when you are approved." |
| Waiting for approval | Summary, what happens next, app download prompt, CTA "Open Medvya". |

**After the wizard** (`prac` states in the clinic app, owner and staff):
- States: Draft, Submitted (in review), Needs changes, Rejected, Suspended, Approved. Each has a banner (icon plus words, not colour alone), a "Medvya reviewer" reason card and a four-step timeline (started, submitted, reviewed, approved).
- Before approval (Draft, Submitted, Needs changes, Rejected) non-owner staff see a full-page "Waiting for approval" (or "isn't active" for Rejected) with "Questions? Ask {owner}". The owner sees "You can do now" (profile and locations, hours and services, staff up to the cap, payments) and "Opens after approval" (register patients, booking, check-in and queue, notes and signing, more staff), with "The server refuses these until approval, not only this screen".
- Needs changes: reason, fix and Resubmit. Rejected: reason, "final for this request", "Start a new request" copies the details in (documents re-uploaded), "Contact Medvya".
- Suspended: banner only; existing records open; "New registration paused".
- Settings, **Organisation and approval**: profile, documents (locked while in review), history; **Team and roles**; **Practice and locations** (each location records its certificate and tax number); **Plan and billing**.
- **Attest doctor's licence** drawer (Team): licence number, issuing authority (country list), valid until, checkbox "I have seen this doctor's current licence ... I am responsible", "Printed on prescriptions as ...". Copy: Medvya does not check licences; reminders 30 days before expiry and on the day; an expired licence warns and does not block signing ("policy under review").
- Public site: solo plan reads "you register as a clinic and are its owner"; FAQ "Why does Medvya approve my clinic first?"; switch-over steps "Sign up as a clinic or hospital, Medvya approves you".
- Not designed: clinic approval email text, resubmit validation errors, document upload errors (size, type, malware), Hindi and Arabic strings for the new steps, mobile layouts of the wizard.

### F-05 System states
| ID | Title | Body | CTA |
|---|---|---|---|
| F-05a | Session expired | Your unsaved draft is kept on this device. Sign in again to continue; **you will be asked to confirm the patient.** | Sign in again |
| F-05b | No access to this facility | You are signed in to {facility}. This record belongs to another facility. | Request access |
| F-05c | Page not found | The link may be old. Nothing has been changed. | Back to Today |
| F-05d | You are offline | Recording continues to the encrypted device buffer. Notes and orders will save when you reconnect. | Retry now |

Microcopy key `err.403` = "You don’t have access to this facility."

### F-06 Invite acceptance
- "Join {clinic}. Dr. Arjun Rao invited you as **Receptionist**."
- Shows a capabilities list, e.g. ✓ Register patients, bookings, check-in, billing · ✕ Clinical notes, prescriptions, results.
- Shows facility scope and hours ("Facility: Adyar · clinic hours only").
- CTA "Create passkey & join". "Invite expires {date}. Not you? Ignore this message." An expired link means asking the admin to resend.
- Membership status goes `invited` → `active`. Clinicians also need a licence check.

### F-07 Trust centre
- Six blocks: Data residency · Consent model · AI drafts · Retention (audio 30 days by default) · Audit · Sub-processors (changes announced 30 days ahead).
- Footer: "We describe our controls; we do not claim certifications we have not obtained." Policy versions and sub-processors are listed with dates. A demo request form collects **no PHI**.

---

### F-07 (Foundations 10 Nov 2026) Organisation approvals queue
Foundations now lists a second **F-07** ("Organisation approvals queue, admin /approvals") next to this Trust centre. The ID clashes; the queue is specified under Platform console below. Question for the designer: renumber one.

## Platform console, organisation approvals (N-12, MDV-P1-064, MDV-P5-044) — `Medvya Admin`, redesigned 10 Nov 2026
Nav item "Approvals" (was "Verifications"), badge = waiting count. Overview KPI "Approvals waiting" (oldest 3 days, target [SLA]) and attention row "N organisation requests waiting for approval: can set up but can't see patients until decided". Funnel drop-off reads "Waiting for organisation approval". The page header says "Medvya approves clinics and hospitals, never individual doctors. You see only the request and its documents, never patient data."
- **Queue** (left, 340 px): filters Waiting (count), Needs changes, Decided, All; each row shows name, place, state chip, type chip (Clinic or Hospital), country chip, wait. Empty: "Nothing here."
- **Detail**: header with name, place, country, type and state chips; wait line with target "[SLA · to be decided] · reviewer not yet named" and "No patient data in this request"; profile grid (owner or authorised signatory, contact, GSTIN or TRN, starter team); **Documents** each with an automatic check line (e.g. "Name and address match", "Format OK · state 27 matches", "Spelling differs" as a warning) and Open (private, watermarked viewer, no download, every view logged); History with actor.
- **Decision** (only Operations and Reviewer): Approve, Ask for changes, Reject. Required reason, sent to the applicant, plain language, no patient details; preset text per decision. Reject shows "Rejection is final for this request. The applicant can start a new one." A hospital without a signed DPA cannot be approved ("Can't approve yet ... Ask for changes instead"). The confirmation toast says email sent and logged; an email preview ("No PHI") appears after the decision. Name, decision, reason and time go to the audit log.
- **Roles** ("Acting as", demo control): Support and Finance read requests only (cannot approve, ask for changes, reject or suspend); team page adds a Reviewer, "Support, read-only on approvals" and "Finance, read-only on approvals".
- **Suspend and reinstate** (tenant page): button reads "Suspend organisation..." and, once suspended, "Reinstate organisation" (one click, no confirmation, logged). Hint "Operations and reviewers only. Always with a written reason; logged." Confirmation copy: existing records stay readable for care under way; new registration, booking and signing are refused; the owner is emailed the reason; nothing is deleted.
- Not designed: the tenant catalogue and tenant detail do not yet show organisation type, approval state or location count; plans do not show seat types; resubmitted requests (history only); per-document flagging (one reason for the whole request); suspension reason entry on this screen beyond the confirm dialog; 403 page; loading and error states; Arabic.

## Mobile clinic app (`Medvya Mobile`, 10 Nov 2026)
A "Clinic approval" demo switch (Approved, Waiting, Suspended). Waiting: after sign-in a "Waiting for approval" page (hourglass, "{clinic} is waiting for Medvya to approve it. We'll send a notification as soon as you can start", "Ask {owner}, the owner", Sign out). Suspended: a banner "Clinic suspended by Medvya. You can open existing records for care under way. New patients, bookings and signing are paused."

## Patient registration (P-02) — `Medvya Patient Registration`

- **Modes:** "Quick (busy desk)" and "Full registration". **Country:** India / UAE (follows the tenant). Header shows a SYNTHETIC badge in demo.
- **Steps (full):** 1 Identity & IDs · 2 Contact & address · 3 Family & emergency · 4 Payer · 5 Consents · 6 Review (duplicates & save).
- **Quick mode:**
  - India fields: full name, mobile, DOB or age, sex, ABHA (optional)
  - UAE fields: name as on EID, mobile, DOB, sex, Emirates ID
  - Saves with a **completion task** on the front-desk queue ("Address, emergency contact and payer are missing").
- **Identity verification banner:**
  - India: "Verify with ABHA". Scan & Share QR or OTP to the Aadhaar-linked mobile. Demographics are pre-filled after consent.
  - UAE: "Read Emirates ID or verify with UAE PASS". The card reader fills name EN/AR, DOB, nationality and expiry, and staff confirm against the person.
- **India rules:**
  - Minimum to register: name, DOB **or age** (marked "estimated"), sex, mobile.
  - ABHA is optional (14 digits, stored tokenised); care never depends on it.
  - Aadhaar: **last 4 only**, never the full number. Other IDs: PAN, Voter ID, Passport, Driving licence.
  - PIN code must be 6 digits (`^\d{6}$`, error "PIN code must be 6 digits").
  - Indian address format: house, street, city, district, state/UT, PIN.
  - Gender identity is optional and separate from sex at birth.
- **UAE rules:**
  - Minimum: name, DOB, sex, mobile, **Emirates ID or passport**.
  - EID format `^784-\d{4}-\d{7}-\d$` (error "Format 784-YYYY-NNNNNNN-N").
  - An expired EID **warns, never blocks** care.
  - Arabic name with RTL entry. Nationality.
  - The emirate drives the HIE: Malaffi (Abu Dhabi), NABIDH (Dubai), Riayati (other emirates).
  - UAE records never leave the UAE deployment.
- **Family:**
  - Emergency contact name, relationship and phone are required for full registration.
  - Guardian becomes required when age < 18 and drives portal proxy rules.
  - Note: "Emergency contacts are not given portal access" — caregiver access goes through R-04.
- **Payer:**
  - India: Self-pay / Corporate / Private insurance (TPA) / Government scheme. Insurer, TPA, policy no., government scheme (M4 placeholder), valid until.
  - UAE: Insurance / Self-pay / Corporate. Insurer/plan, member card no., card expiry, network. Eligibility check is marked M4.
  - Note: "Card numbers are not stored; payments go through the provider."
- **Consents (each recorded separately with its policy version):**
  - Treatment (required, locked)
  - Reminders on WhatsApp / SMS
  - Link to ABHA (India) / HIE (UAE)
  - Portal access
  - Health tips & offers (**off by default**)
  - Recording consent is **asked at each visit, not here**.
- **Duplicate check on review:**
  - Matched on name + DOB. "**A shared phone number alone is never treated as a match.**"
  - Side-by-side NEW vs EXISTING record.
  - Actions: "Not the same person · continue" · "Use existing record" · "Request merge review (P-03)".
  - Save is blocked ("Resolve duplicate to save") until resolved.
- **On save:**
  - MRN assigned (facility-scoped, e.g. DQ-11-1093). "{name} is registered", with timestamp and actor.
  - Writes: PATIENT (demographics encrypted), PATIENT_IDENTIFIER (tokenised), EXTERNAL_PATIENT_MAP (queued), CONSENT_RECEIPT (n decisions, policy-versioned), PAYER_COVERAGE, PORTAL_ACCESS (invite), AUDIT_EVENT `PatientRegistered.v1` (**no PHI in event payload**).
  - Next actions: Book appointment · Check in now (walk-in) · Register another.
- Receptionists cannot see clinical notes after registration.

## Duplicate / merge review (P-03) — `Medvya Clinical Screens`
- Side-by-side field comparison: name, DOB, sex, mobile, ABHA, visits, allergies.
- Actions: "Not the same person" or "Request clinician approval".
- **Merge needs a second approver when clinical data exists on both records.**
- Merges are reversible and audited (P5-02).

## Front desk board (P-01) — `Medvya Clinician App`
- Columns: **Booked → Checked in → With nurse → Ready for doctor → In consult → Billing → Done**.
- Card CTA per column: Check in · Send to nurse · Intake done · Call in · Consult done · Payment received.
- Header: "Receptionist view hides clinical notes". Buttons: Walk-in, Register patient.
- Cards show name, time, doctor/visit type, and nonclinical tags: "Walk-in", "New patient · ID verified", "No-show · offer rebook", "Due ₹600 · UPI link sent", "Recording consent given".
- The "Checked in" column shows wait time ("Waiting 12 min"). "With nurse" cards have **Open intake**.
- Side panels:
  - Waitlist ("earlier slot wanted", **Offer {time}**)
  - Doctor workload
  - Results inbox summary (P4)
- Queue TV (P-05): **token numbers only, never names.** Shows "Now: token, room, doctor" and "Next: token, ≈ wait".

## Schedule (P-04) — `Medvya Clinician App`
- Views: day / week (list view per PRD P1-06). Columns per doctor and per resource (e.g. procedure room).
- 20-minute slots in the design. Unavailable blocks show the reason ("Not in today · Mon–Wed", "Lunch break").
- Appointment states:

  | State | Icon | Style |
  |---|---|---|
  | Booked | `event` | — |
  | Checked in | `how_to_reg` | — |
  | In room | `meeting_room` | — |
  | Completed | `check_circle` | — |
  | No-show | `event_busy` | dashed red |
  | Cancelled | `cancel` | dashed grey |
  | Unavailable | `block` | hatched |

- Detail panel: reference (APT-…), name, state, "Sat 4 Oct · 10:00 · 20 min", type, doctor.
  - Actions: **Check in** (only when Booked) · **Reschedule** (not when Completed) · **Cancel** (asks for a reason; not when Completed or Cancelled) · **Mark no-show** (only when Booked).
- Clicking an empty slot opens booking for that doctor and time. A doctor's unavailable column is not bookable.
- Drag to reschedule needs confirmation. **No-show offers rebook.** Multi-slot visits span (e.g. "New consult · 40 min" = 2 slots).
- Leave (settings ⚠): "Leave: 20–24 Oct 2026 · **6 booked patients will be offered new slots before leave is saved.**"

## Global search (D-02)
- Search name / MRN / phone. Results show **initials + age + MRN tail only** until the record is opened.
- Patients outside your care relationship show "Request access" (break-glass in hospitals).
- Empty state: "No patients match. Register a new patient".

## Notification centre (D-03)
- Overlay with Mark all read.
- "**Browser and phone push notifications never include patient names or results.**" Push copy is `push.generic` = "New update from {clinic}. Open Medvya to view."

## Practice settings (F-08 … F-12) — `Medvya Clinician App` ⚠ (data truncated; labels only)
- Tabs (from journeys): profile, hours/availability, doctors, staff, services, notifications, data/access log.
- Profile: logo "Shown on prescriptions, invoices and the patient portal."
- Hours: per doctor, default slot (15/20/30 min), weekday toggles, leave with re-offer of booked slots.
- **Add doctor (F-09):** details → licence India/UAE → verification → schedule (day, time, room) → fees → access → signature image.
  - Signature note: "The image only appears on printed prescriptions. **Signing in Medvya is always an authenticated action by the doctor (passkey / MFA), never the image alone.**"
- **Add staff (F-10):**
  - Role picker with descriptions → scope → permissions → invite. Table columns: Name, Role, Access, MFA, Status.
  - "Locked rows come from the role and cannot be granted here. **Every change is written to the audit log.**"
  - "Doctors need licence verification. Use Settings → Doctors → Add doctor." Custom roles are supported.
- Services (F-11, P4):
  - "Saving creates a new **price version** effective from the date above. **Issued invoices keep the price they were issued with.**"
  - Invoice line preview. Archive / Save version.
- Formulary (F-13, P2): Generic · strength · form, brands, national code, class, default sig, status. Order sets. "Not in the clinic formulary. Add it in Settings → Medicines or prescribe as free text (flagged for review)."
- Practice access log (F-12): who opened which record and when (owner-visible).

---

## Clinical (P2) — `Medvya Clinician App`, `Medvya Clinical Screens`

### N-01 Nurse intake
- Queue: "With the nurse" cards open their intake; "Checked in" cards can be taken to intake by the nurse without waiting for reception.
- Usual ranges follow the patient's age band (PALS for children, adult from 18); they are a draft until a clinician signs them off (`VitalRules.SignOff`).
- Large vitals inputs: BP systolic/diastolic, pulse, temperature, SpO₂, respiratory rate, weight, height (MDV-P2-031). Each shows the previous value.
- Out-of-range values are flagged in words ("High · re-check", "Outside usual range", "+2.2 kg since 02 Oct"). **Re-measure prompt, then Escalate to doctor with a reason.**
- Allergies: each must be set to **Confirmed / Patient reported / Unknown / Not an allergy**. Hand-off is disabled with "Confirm allergy status to hand off" until all are set.
- Medicines are patient-reported vs confirmed. "Needs clarification" covers unknown names (e.g. "a white tablet for sugar").
- Chief complaint in the patient's words. Hand off sets the card to "Ready for doctor".

### E-01 Patient 360 / E-02 Timeline
- Fixed identity banner. Allergy is never colour-only.
- Verified chart is kept separate from the AI summary (the AI part is P3).
- Timeline filters: All, Encounters, Labs, Medicines, Documents. Each entry has a **provenance tag**: Medvya · signed, Clinician verified, Lab feed · HL7, Synced from HIS, Uploaded by patient. "Load earlier…".
- Trends: HbA1c / BP / weight.
- Life stage, banner details and history (MDV-P2-032): under the banner, the life stage as a label with an icon and words (Child under 18, Pregnancy while a pregnancy is open (MDV-P2-034), Older adult from 65, else Adult), then blood group, preferred language and care team only when recorded, and the treatment consent. A History card lists the sections the life stage offers (birth and feeding for a child, function and falls for an older adult, obstetric in pregnancy, women's health for a woman) plus any section already written, each with its author and date and its earlier versions; a doctor or nurse edits a section or the blood group and every save adds a version.
- Pregnancy card (MDV-P2-034): shown for a patient who is not male and is 10 to 55, or while a pregnancy is open. With none open it says "No pregnancy recorded" (the medicine checks treat it as not known) or the "not pregnant" statement made today with who and when; a doctor starts a pregnancy (LMP, dating scan date and gestation, G P A L, a blank count stays unknown) and a doctor or nurse records "not pregnant today". With one open: gestation today in the facility's zone with the trimester, the due date with how it was dated (LMP + 280 days, or the scan when it differs by more than the dating rule) and any earlier due date it replaced, G P A L, a trimester bar with a text alternative, the antenatal contacts (8–40 weeks) each said in words as done, this week, to come or no visit, the danger signs with who counselled them and when, and actions: danger signs counselled and record fundal height and fetal heart (doctor or nurse, on the open consultation), close with an outcome (doctor). The rules are marked as a draft awaiting clinical review.
- Last vitals and Results (MDV-P2-031): the latest reading of each code, flagged in words with an arrow (High or Low, with the usual adult range) and a line saying the ranges await clinical review. BMI is worked out from the latest weight and height and names the older of the two with its date. The full results table and trend lines per code are the one-page chart (MDV-P2-033).

### E-03 Documents
- List with type and source. Secure viewer: watermarked, **no download for reception**, time-limited links.
- "Extract findings (AI)": each extracted value (with page number) needs **Confirm** before it enters the chart. Failed upload offers retry.

### E-04 Manual note (consent declined)
- Banner "Recording declined". SOAP sections prefilled from the **verified chart only**. Quick phrases and templates. Same sign flow. **No guilt copy.**
- `consent.declined` = "No recording for this visit. You can write the note manually."
- **Add to the note** panel (MDV-P2-009), for a draft only; nothing is added until the doctor presses a button, and the text lands at the cursor of the section last edited:
  - Verified vitals of this visit (the nurse's intake readings, checked by a clinician; a flagged reading only once the nurse kept it), each worded with who took it and when. "Add to Objective" adds them. With none verified the panel says so and Objective stays blank.
  - Quick phrases of the template (static, versioned with it).
  - Earlier signed notes of the same patient as snippets with date and signer; an inserted snippet is marked "(copied from the note of {date})".
  - Each read of the vitals and of earlier notes is an audit row (`note.prefill`, `note.previous`) with the patient and no values.

### E-06 Signed note
- Banner "SIGNED v1 · read-only · Print · Addendum".
- "Signed by Dr. Arjun Rao (TNMC 84213) · 04 Oct 2026 10:31 IST · Corrections are added as signed addenda."
- Version history. The addendum composer creates a new signed version. **v1 is never edited.**
- Print: A4, header with MRN/DOB on every page.

### E-07 Coding and reconciliation
- Problems with ICD-10 code and a verification state: Verified / Added today · verified / Patient reported / Provisional.
- Medicines compared across three columns: chart, patient-reported, HIS.
  - Per row: Keep / Change / Stop. Patient-only items: Add / Ignore.
  - Summary "0 of 4 decided · 3 sources disagree". **Confirm is disabled until all are decided.**

### E-05 / E-11 / E-10 Rx and orders
- Rx items carry a source chip ("From conversation 05:40", "Added by you", "Chart reconciliation") and a safety-check chip ("Dose within usual range", "Renal dose OK · eGFR 72").
- **Rx authorisation and order placement are separate actions** from note signing.
- Order states: Draft → Ordered.
- Medicine not in the formulary → free text, flagged for review.
- Printed Rx: generic names first, multilingual, QR verify. e-Rx to pharmacy.

### One-page chart, life stages, Add medicine and one-page consultation (12 Nov 2026 design, epic MDV-P2-E07)
Design ids D-06 (chart), D-07 (consultation), D-08 (Add medicine) and PT-09 (patient-app trackers) in Foundations; the product screens are E-01, E-04 and E-05. Built already: identity banner, allergies, problems with ICD-10, medicines and reconciliation, vitals with age-band flags, HbA1c/BP/weight trends, timeline, documents, note templates, consent and the manual note, Assist, signing and addenda, the Rx composer and its allergy, interaction, duplicate and adult dose checks. New in the design:
- **Chart on one page (MDV-P2-033):** banner adds blood group, language, life-stage label, alerts, care team and consent; six vitals tiles with a trend line and a flag ("Above 130/80"); results table with reference range, trend and flag; history in sections; right column for care gaps, goals and documents; section buttons (Everything, Summary, Vitals and labs, the life-stage module, History, Timeline). New codes and labs: MDV-P2-031. Life stage and history: MDV-P2-032.
- **Life-stage modules:** adult chronic targets and women's health (MDV-P2-038); pregnancy with gestation, due date (LMP + 280 days, confirmed by scan), G P A L, trimester bar, antenatal contacts and danger signs (MDV-P2-034); child growth chart with centile bands, immunisations and milestones (MDV-P2-035, P2-036); older adult falls, walking test, standing BP drop, frailty, memory, daily activities and medicine review (MDV-P2-037).
- **Add medicine (MDV-P2-039, P2-040):** search, strength, how often (1-0-0, 0-0-1, 1-0-1, 1-1-1, when needed, weekly), when, how long, instructions in the patient's language, safety checks for this patient (allergy block, duplicate, pregnancy from 20 weeks, weight-based child dose in mg and mL, kidney with eGFR and an ARB, older adult), the finished line and the quantity.
- **Consultation on one page (MDV-P2-041 to P2-044):** consent bar first, seven numbered sections with a sticky section bar and ticks, review of systems as yes/no chips, adaptive template by patient, examination Normal / Abnormal / Not examined per system, plan with continue / new / change / taper / stop, tests to tap, advice, follow-up and referral, and Sign locked until the checklist is complete. "Guided steps" keeps the old flow.
- Clinical sign-off before use: growth curves (WHO, IAP, UAE), vaccine schedules (IAP 2023 marked "verify"), every drug rule and dose (session handoff).

### Sign modal (A-05/E-06)
- "Sign note for {name}?" with MRN, DOB and encounter id.
- Body: "I have reviewed every section against the source evidence. This note becomes part of the medical record and is read-only after signing. Prescriptions and orders are authorised separately." Shows the edit summary.
- CTA "Sign as Dr. {name}". The button is disabled while unresolved: `note.signBlocked` = "Sign note ({n} unresolved)".

---

## AI scribe (P3) — `Medvya Clinician App`, `Medvya Clinical Screens`

- **Encounter stepper:** Brief → Consent → Record → Speakers → Note → Rx & orders → Follow-up.
- **Consent (A-02):**
  - Consent script in the patient's language (English / Tamil / Hindi shown). `consent.script.en` = "With your permission, Medvya will record our conversation today to help me write your medical notes. Only your care team can access it. You can say no or ask me to stop at any time, and your care will not change."
  - Pill states: not yet asked / given {time} / declined · manual note.
  - Device choice: This phone (mic test), Desk microphone, Medvya Clip (M3, disabled if not paired).
- **Live (A-03):** REC timer, pause, waveform (stops with reduced motion), live provisional transcript with speaker tags Doctor / Patient? / Companion?.
- **Speakers (A-04):**
  - Each speaker gets a suggestion with its reason (device owner voice profile, answers to name, consent roster) and a talk share.
  - **Confirm per speaker.** Unconfirmed labels are dashed.
  - Original-language text sits beside the translation.
- **Note review (A-05):**
  - Field states: AI draft · not reviewed (violet dashed) → Accepted by you / Edited by you. Verified chart data (e.g. nurse vitals) is shown as verified.
  - Flags:
    - `warn`: e.g. "Daughter stated this, not the patient. Speaker confirmation pending."
    - `conflict`: e.g. "Conflicts with chart: atorvastatin 10 mg listed as active".
  - Evidence panel: speaker, timestamp, original-language quote, translation, audio replay, source reference (e.g. "Transcript seg #4 · RS-2291 · Tamil → English").
  - Sign stays disabled until 0 unresolved.
- **States (A-06, A-08 … A-10):**

  | State | Behaviour |
  |---|---|
  | Upload in progress | "Uploading 14 of 17 encrypted chunks." Review can start |
  | Failed / partial | Missing range is marked in transcript and draft; retry from phone or continue partial |
  | AI unavailable | `ai.unavailable` "AI drafting is unavailable right now. Your recording is saved." Write manually |
  | Low audio | Dismiss or mark segment |
  | Wrong patient suspected | **Stop & lock**. Re-binding needs a reason and is audited |
  | Consent withdrawn | Recording stopped at {t}, later audio discarded, receipt recorded, continue manually |
  | Overlapping speakers | Statements stay flagged until assigned or marked unknown |
  | Emergency symptom | "This is a prompt, not triage." Acknowledge or Not relevant (with reason); logged |

- **After signing:** done list shows each item with its own state.
  - Note signed (lock)
  - Prescription (Authorised / Not authorised)
  - Investigations (Ordered / Draft)
  - Care plan (Open, with owner and due date)
  - Patient reminder: "Queued · not delivered"
  - Copy: "Each item below has its own owner and state. Sending a message does not complete care."

---

## Billing (P4) — `Medvya Clinician App`

- **Invoice states:**

  | State | Label | Actions |
  |---|---|---|
  | `draft` | Draft | Issue invoice, Add line, Discount (reason) |
  | `issued` | Issued · awaiting payment | Send UPI link, Record cash, Card on terminal |
  | `pending` | Awaiting provider confirmation | Waits for provider callback |
  | `paid` | Paid | Print receipt, Tax invoice, Refund |
  | `failed` | Payment failed | Retry, Record cash instead |
  | `refunded` | Refunded | Print credit note |
  | `insurer` | Insurer · claim pending | View claim, Collect co-pay |

- **Paid only on provider confirmation** (`pay.pending` = "Awaiting provider confirmation"). Discounts need a reason (e.g. "Senior citizen · policy SC-10").
- **Documents:**
  - India: Receipt `RCT/25-26/n`, Tax invoice `INV/25-26/n` with place of supply, Credit note `CN/25-26/n` against the original invoice, Advance receipt. GSTIN, CE reg., amount in words (lakh/thousand), A5 and 80 mm thermal, QR verify.
  - UAE: TRN, VAT 5% on non-healthcare lines, insurer pays / patient co-pay split, claim number, Arabic titles.
  - India clinical services are GST-exempt.
- **Split payment (P-10):** cash + UPI + card + advance. Remaining shows "Balance remaining" / "Fully covered" / "Over-collected · reduce an amount". A partial balance moves to Dues with a due date. Card on terminal: **no card data stored**.
- **Packages (P-11):** sessions used/left, validity, redeem 1 session (creates a ₹0 invoice line), expired balance follows the refund policy.
- **Dues (P-12):** ageing (days, colour by age), patient statement, reminder with no clinical details ("You have a pending balance with {clinic}. Pay securely: link").
- **Day close (P-13):** denominations per country, opening float, expected vs counted, variance. Variance needs **a note + second staff confirmation** ("Attention · cash short ₹200"). Summary by method (cash, UPI settled, card batch, refunds, insurer receivable).
- **Revenue report (P-09):** net = gross − discounts − refunds. Insurer receivable is counted when settled. Payout share per doctor. Admin only.
- **Pre-auth (P-14, P5):**
  - India: TPA cashless form (patient, insurer/TPA, policy, scheme, provisional diagnosis ICD-10, proposed treatment, estimated cost, treating doctor) and a required-documents checklist.
  - UAE: eClaimLink.
  - Claims needing action: rejected (with reason, fix) and query.
  - "Medvya does not decide eligibility."

## Care (P4) — `Medvya Clinician App`, `Medvya Clinical Screens`

- **Results inbox (C-01):**
  - List with severity in words (Critical / Abnormal / Normal) and loop state: Received · not reviewed → Reviewed → Assigned → Released → Completed.
  - Result rows: analyte, value, reference range, flag, previous value. Source (lab, HL7 ORU / PDF upload, matched). "Identity matched on MRN + DOB".
  - Optional AI context note, e.g. "Telmisartan 80 mg started 12 Sep… ACE/ARB can raise potassium".
  - **Mark as reviewed** stores actor and time. Assign and Release unlock only after review. `result.received` = "Received · not reviewed".
- **Care plan (C-02):** actions with owner, due date and stage track. A completed stage needs **evidence** (e.g. "LR-2311 reviewed", "Call log 11 Oct, 3 min"). Overdue is shown in words.
- **Contact log (C-03):**
  - Channel: Call / WhatsApp / SMS.
  - Outcome required: Spoke · rebooked / Spoke · declined / No answer · retry / Wrong number / **Message delivered (not a contact)**.
  - `msg.delivered` = "Delivered — this does not complete the task".
- **Overdue (C-04):** list with days late and reminders delivered. Escalate to doctor creates an inbox item. Reassign, or close with a reason.
- **Report match (C-05):** PDF preview. Low-confidence match ("name matches, DOB differs"). Candidate list (MRN, DOB, open orders). **Confirm patient** is disabled until one is picked. Option to reject.
- **Messages (C-06):**
  - Patient ↔ clinic threads. AI reply draft that staff approve.
  - "Attention · clinical question. Routed to Dr. {name}. Staff can acknowledge but only a clinician can give medical advice."

## Patient portal (P4) — `Medvya Patient Portal`

- Patients see only what their doctor has released. **Never** unreviewed AI drafts, internal notes or other patients.
- **Home:** greeting, "New from Dr. {name}: Your visit summary and prescription are ready", next appointment, To-do list (tests, medicines to start/stop, referral).
- **Visit summary:**
  - "Reviewed and shared by Dr. {name}". Language tabs (English / Tamil / Hindi).
  - Medicines with start/stop.
  - "Translated with AI and checked against the English summary your doctor approved. If unsure, ask the clinic."
  - Download PDF, Message clinic.
- **Records:** items with a status (Shared by your doctor / Reviewed by doctor / Ordered / Uploaded by you). "New results appear here after your doctor reviews them. Most are reviewed within 2 working days."
- **Booking (R-02):**
  - Choose In clinic or Video, then a day strip, then slots (unavailable ones struck through), then an optional reason.
  - Fee and cancellation policy are shown before confirming ("Free cancellation up to 2 hours before"). "Booked · confirmation sent".
  - The reason is never shown on reception screens beyond its category.
- **Consent (R-03):**
  - Separate switches: Record my consultations (asked again at each visit) · Reminders on WhatsApp (no medical details) · Share records with ABHA/HIE · Caregiver access · Health tips & offers (optional).
  - Footer: "Policy v2026.2 · India (DPDP Act) · last change {time}".
- **Caregiver (R-04):**
  - Name and mobile. Scopes: Appointments / Medicines / Lab results / Messages.
  - Expiry ("Access ends on {date}"), revocable any time. The caregiver sees "Viewing as caregiver for {name}".
  - "Invite sent · they must verify with OTP". Minor/guardian rules per country.
- **UAE Arabic RTL home:** insurance eligibility card (e.g. "Thiqa (Daman) · eligible · 0% co-pay · valid until 12/2026").

## Enterprise (P7) — `Medvya Enterprise Admin`, `Medvya Enterprise Pages`

- **Record authority (H-08):**
  - Per facility: Medvya EMR or Existing HIS. Status values: "HIS authoritative · write-back on" / "Medvya authoritative" / "HIS not connected yet".
  - Mixed authority shows "Attention · mixed authority" (cross-facility views show source badges and conflicts).
  - Onboarding steps: Organisation, Facilities, SSO, HIS connection, Record authority, Pilot go-live.
- **Integrations (H-01):**
  - Connection tiles show protocol, status (Healthy / Degraded / Not connected), last message, 24 h volume and direction.
  - Examples: FHIR R4 HIS, HL7 v2.5 MLLP LIS, ABDM gateway, Entra SSO (OIDC+SCIM), WhatsApp. UAE: Malaffi / NABIDH / Riayati pending regulator onboarding.
- **Write-back queue (H-04):**
  - Item states: Delivered · receipt / Failed · retrying paused / Conflict · clinician review / Retrying / Sent to clinician.
  - The attempts list shows timestamp and code. A conflict shows a side-by-side diff (Medvya signed v1 vs HIS current).
  - "**Medvya never overwrites the HIS silently.** Clinical conflicts go to the signing clinician; transport errors can be retried by IT."
- **Audit (H-05):**
  - Append-only, no PHI in logs. Columns: time, actor, action, patient ref (opaque), purpose, review.
  - Break-glass is "Pending review". Denied actions are logged ("Policy enforced").
- **Org (H-02):** facilities with authority and departments.
- **SSO (H-03):** IdP, SCIM status, MFA policy. Grants table: user × role × facility scope × expiry. "Role alone never grants access to another facility." Quarterly access review.
- **Retention (H-06):** region, CMK rotation, audio 30 days, transcripts with the signed note, medical records per local rules, audit 7 years (configurable), cross-border transfer **Disabled**. "Labels show configured policy… not certification claims."
- **Data requests (H-10):** access / correction / deletion (legal hold) / withdraw recording consent (audio purge + vendor receipt). Tamper-evident export ZIP.
- **Analytics (H-07):**
  - KPIs: visits, notes signed same day, doc time per visit (**estimate with method caveat**), unreviewed results > 48 h, no-show rate.
  - Charts: visits/day by facility, open loops. Marked ILLUSTRATIVE DATA in demos. Drill-down only to authorised detail.

## Devices (P6) — `Medvya Mobile and Tablet`, `Medvya Wireframes`

- **Pairing (W-01):**
  - Hold the button 3 s → LED pulses → **4-digit code on clip must match** → mic self-test ("say testing") → pair to doctor.
  - Pairing expires in 90 days.
- **Health (W-02):**
  - Battery %, BLE status, firmware, encrypted offline queue count, staged firmware update, Sync now, Report lost.
  - Upload conflict (same session on phone + clip): keep both and the clinician picks.
- **Revoke (W-03):** reason (lost / stolen / retired). Revoking wipes keys, and queued audio on the device becomes unreadable. Audit event and admin notification.
