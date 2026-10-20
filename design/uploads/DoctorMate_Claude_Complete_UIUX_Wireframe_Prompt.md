# CLAUDE MASTER DESIGN PROMPT — DoctorMate v2 | Complete Modern Clinical Product & End-to-End Wireframes

You are the **founding product designer, clinical UX architect and principal design systems engineer** for DoctorMate (working name), an India-first, UAE-ready, doctor-first medical platform. Use the entire attached **DoctorMate Architecture v2**, original product requirements, ERDs and application diagrams as the single scope reference. Produce a **complete, high-fidelity, responsive, accessible visual product design and fully connected wireframe**, not a mood board or a single attractive dashboard.

## 1. Mission and quality bar

Make the product feel as polished, calm and approachable as a premium 2026 SaaS application, but as clinically clear and reliable as professional healthcare software. The product must be learnable by an independent doctor in minutes and scalable to a hospital with receptionists, nurses, clinicians, care coordinators, patients, admins and IT. Design for real Indian clinic workflows first; include a separate UAE enterprise deployment experience with country-specific policy/identity/integration labels. **Never prioritize visual delight over medical clarity, safe approvals, consent or accessible contrast.** Use realistic, clearly synthetic sample data and prominent labels for simulated patient information.

**The core promise:** A doctor can open the right patient, see a 60-second verified clinical brief, obtain recording consent, talk naturally with the patient or several people, record from a phone/desktop mic or wearable clip, review speaker labels, edit an evidence-linked AI clinical draft, sign the note and approved orders, and generate accountable follow-ups—without constantly typing. An independent doctor also gets appointments and simple billing. A hospital instead can connect to its existing authoritative HIS/EMR.

## 2. Non-negotiable information architecture: visually separate all product blocks

Create recognizable functional shells and navigation for:

**PMS / Practice Management (blue grouping):** facility setup, doctor availability, registration/MRN, patient directory/dedup, day/week calendar, waitlist, check-in, waiting room, nurse intake, invoices, payments, receipts, reconciliation; later insurance/claims handoff. Receptionist must not see sensitive doctor-only notes.

**EMR / Clinical Workspace (teal grouping):** Patient 360, clinical timeline, conditions, allergies with verification, medications, vitals trends, documents, encounters, SOAP notes, orders, results, prescriptions, amendments, clinical provenance and signed/unsigned status.

**Ambient AI / DoctorMate Assistant (subtle purple grouping):** previsit brief, consent, phone/desk/wearable capture, live recording and audio status, provisional speaker labels, Hindi/English language mixing, transcript/edit, original audio evidence with secure replay, source-linked note generation, uncertainty alerts, explicit manual edit and physician sign-off; optional private voice assistant via earpiece.

**Care Coordination (sage grouping):** doctor inbox, lab/result matching and review, order → result → acknowledgment → patient contact → follow-up completion workflow, accountable ownership and escalation, patient instructions and opt-in WhatsApp/SMS with limited PHI.

**Patient Portal (cleaner simplified shell):** self-scheduling, identity/proxy, consent and privacy preferences, released visit notes, prescriptions, lab results, patient-friendly care instructions, reminders and messaging. Never expose unreviewed AI drafts or other patients' information.

**Hospital Enterprise (navy/neutral grouping):** facility and department admin, staff invitations, role and facility permissions, patient data authority indicator (`DoctorMate EMR` vs `Existing HIS`), integration mapping/status, audit and break-glass justification, managed devices, privacy/retention, analytics and deployment residency.

Do not mix PMS invoices and EMR clinical decisions in a single unstructured page. Show integrated navigation, cross-links and a consistent patient identity banner, while preserving module responsibilities and role-based visibility.

## 3. Distinctive premium color system (actual tokens, not generic pastel medical blue)

**Direction:** quiet luxury, clinically reassuring, unmistakably modern: deep midnight navy with Mediterranean teal, disciplined blue actions and restrained violet for AI. Mostly white/off-white and cool gray clinical surfaces. Functional danger/red and amber must be reserved for genuine clinical risk or workflow errors; never use only color to communicate risk.

| Token | Light | Purpose |
|---|---|---|
| `brand.950` | `#102A43` | Sidebar, headings, anchor brand |
| `brand.700` | `#0F766E` | Active navigation and brand |
| `brand.500` | `#0E9384` | Primary actions, accepted completion |
| `action.600` | `#2563EB` | Secondary actions and navigational links |
| `ai.600` | `#7155D8` | Clearly provisional AI output, voice features |
| `surface.base` | `#F7FAFC` | App canvas |
| `surface.card` | `#FFFFFF` | Clean content cards |
| `ink.900` | `#142536` | Body text |
| `ink.600` | `#506579` | Secondary text |
| `border.default` | `#DFE7ED` | Crisp quiet separation |
| `danger.700` | `#B42333` | Unacknowledged critical alerts |
| `warning.700` | `#975A16` | Clinical uncertainty/attention |
| `success.700` | `#087F5B` | Confirmed (not inferred) success |

Define accessible dark counterparts; use a contrast tester and ensure WCAG 2.2 AA. Typography: **Inter or Manrope** for UI; easily readable body 14–16px; 16–18px for dense medical charts where practicable, strong numeric clarity and large accessible controls. Use an 8px spacing system, 44px minimum touch areas where appropriate, ~12px card radius, restrained 1px borders, minimal shadow and no neon/cyan glow. Don't use generic heartbeats or stethoscopes everywhere; icons should communicate function.

Produce exact design tokens for typography, spacing, layout, corner radius, shadows, elevation, icon scale, semantic statuses, empty/loading/error states and responsive breakpoints (mobile 360+, tablet 768+, desktop 1280+, widescreen). Include RTL-ready structures for potential Arabic in UAE, without falsely presenting all localizations as production-complete.

## 4. Design system: build real reusable components and variants first

Global: app shell, split sidebar, responsive topbar, command search, patient identity header, safety alert, last-updated provenance tag, AI-draft tag, role/facility switcher, accessible table, calendar, drawer, modal, wizard, toast, skeleton, timeline, status stepper, empty state, in-place form validation, secure document viewer, voice capture control, patient-risk banner, results inbox triage row, prescription composer, signature modal, patient consent sheet, integration status tile, audit detail panel, keyboard shortcuts and live-upload progress.

Variants must show: normal/hover/focus/disabled/loading/empty/error/critical; high-contrast accessible alternatives; mobile layouts; reduced-motion state; correct keyboard order; doctor safety confirmations. Design dense chart layouts with clarity, not endless decorative cards.

## 5. Deliverable order — actually produce these artifacts

A. Visual brand board (wordmark placeholder, palette and typography) and two visual directions: one refined navy/teal **recommended**, one warm clinical minimal. Select and fully develop the first direction after showing both.

B. A **complete sitemap by role** (doctor, receptionist, nurse, patient/caregiver, care coordinator, practice admin, hospital admin, IT/integration admin), mapping every architecture block to user-visible routes. Explicitly label `M1`, `M2`, `M3`, `M4` and `Later`; show restricted screens/permission indicators.

C. **Low-fidelity wireframes for every screen in the inventory below**, then high-fidelity designs for the 12 flagship screens. Connect them as a clickable end-to-end prototype with realistic navigation, back/forward, save-draft and return flows. Design not just happy paths: also no consent, device offline, low audio quality, failed upload, wrong patient, unconfirmed speaker, missing lab result, AI outage, imported record conflict and integration failure.

D. A component library + token specification + table/drawer/form/safety and clinical status patterns, mobile + tablet + desktop versions and dark mode examples.

E. A user journey storyboard for **six** realistic cases: first solo-doctor visit; returning diabetic patient; busy reception-to-nurse handoff; multilingual ward-round consultation with 3 speakers and a wearable; delayed lab result escalation; large hospital with existing HIS and failed write-back/retry. Show every involved screen and approval step.

F. A complete UX handoff: annotated interaction states, Figma-friendly frame naming, component hierarchy, responsive breakpoints, testable microcopy, empty/loading/error copy, synthetic data specification, accessibility checklist and a route-to-screen matrix proving no module was omitted.

**If generating code:** build navigable responsive React 19 + TypeScript + MUI design prototype using locally mocked/synthetic data only; typed route contracts; no backend needed for design. Use meaningful interaction rather than fake buttons. If generating Figma, all layers and components should be editable and auto-layout-ready. Do not imply that a prototype is a clinically deployable product.

## 6. Exhaustive wireframe screen inventory; mark phase and role on every frame

**Foundation & access (M0/M1):** marketing landing with two CTAs (Solo Doctor / Hospital), pricing placeholder, trust center, demo request, country selector/data residency explanation, invite accept, registration, MFA/passkey, password recovery, session expired, 403/no facility access, first-time solo doctor profile/licensing, onboarding progress, new facility, hospital SSO/integration/record-authority wizard and staff role selection.

**Doctor home (M1):** Doctor Today (calendar, waiting patients, priority results, overdue notes, consult button, device ready status, one-click return), global privacy-safe search, urgent vs routine alerts, compact vs expanded density, specialty template selection and saved workspace.

**PMS — Registration & scheduling (M1):** new patient, identity validation, manual merge-review/possible duplicate, patient directory, day/week appointment calendar, slot booking, walk-in, reschedule, cancellation, no-show, receptionist waitlist, check-in, room queue, called/in-room/completed states, doctor workload; minimal fields for busy reception.

**PMS — Billing (M2):** service catalogue, charge capture, line items, invoice preview, tax/discount controls where applicable, third-party payment initiation, payment received/failed, partial settlement/refund, receipt and cash reconciliation; insurance eligibility/claims/integration screens as M4 (don't pretend payer workflows are uniform India/UAE).

**Nurse (M2):** check-in verification, vitals and allergies collection, meds as patient-reported vs confirmed, room assignment, escalation to treating doctor, task ownership and completed handoff.

**EMR (M1+M2):** Patient 360 with fixed identity/allergy bar, flags and provenance, overview, encounters, full longitudinal timeline, vitals trends and lab trends, problem list, active/ceased meds, allergy confirmation, vaccinations if in approved scope, reports/document upload and viewer, prior signed note, manual visit notes, diagnosis coding, clinical order composer, lab collection/result timeline, prescriptions with dose validation display and signed/unsigned status, patient instructions and amendments.

**Ambient AI (M1/M3):** previsit verified/AI-generated split view; explicit recording consent/decline; mic permissions; choose phone/desktop/wearable; clip pairing/firmware and battery; patient and encounter binding; large unmistakable start/pause/stop controls, red recording status and timer, low battery/network warnings, waveforms without excessive animation, provisional speaker badges ('Doctor', 'Patient?', 'Companion?') awaiting verification; multi-language transcription; upload/sync progress; partial audio recovery; transcript review with edit and timestamped secure evidence; source-vs-AI note side by side; clinical uncertainty and missing values; note rewrite/regeneration confirmation; explicit clinician signature and source-of-truth write-back receipt. Private two-way assistant only when a private earpiece is connected and the doctor requests it; public speech may reveal PHI.

**Care coordination (M2):** care plan builder; order and lab tracking; inbox triage; report arrives/identity match; physician result review/acknowledgment; assignment to nurse/care coordinator; patient contact log, WhatsApp opt-in/minimal content, reminders and retry; overdue escalation; follow-up booking and closure with evidence. A 'sent notification' badge must never look like 'result reviewed' or 'care completed'.

**Patient/caregiver portal (M2):** patient sign-in/proxy authorization, appointment booking and rescheduling, recording and data-sharing preferences, patient-friendly approved visit summary, e-prescriptions and labs released by physician, medicine/investigation instructions and translated explanations, follow-up calendar, secure messages, notification preferences and withdrawal/change consent process.

**Clip & fleet (M3):** first-time pairing, explicit pairing code, device model/firmware, battery/charge status, microphone self-test, Bluetooth/mobile connectivity, loss/theft revoke, offline encrypted queue, upload conflict, firmware update staged rollout and health check; admin fleet assignment.

**Hospital enterprise (M4):** organization/facilities/departments, SSO, role grants and licenses, external HIS-as-authority toggle, chart provenance badges, source conflict review, FHIR/HL7 mapping and sync, lab/vendor integration dashboard, explicit failed write-back case, ABDM or UAE HIE admin integration surfaces (subject to actual permissions), access audit and break-glass, patient export, retention policy, device fleet and region-specific compliance status.

**Analytics (M2/M4):** clinic throughput, doctor documentation time *estimate with methodological caveat*, unacknowledged result count, overdue follow-ups, patient no-show rates, payment status, integration health and clinician adoption. Always permit drill-down to authorized, relevant detail only.

**All modules:** empty, partial, validation error, 403, 404, no internet, slow server, interrupted recording, denied consent, device reconnect, AI unavailable, unverified clinical fact, high-contrast, mobile and printer-friendly variants. Synthetic sample patients only; clearly mark all demonstrated metrics and alerts as illustrative.

## 7. Interaction rules and safety requirements

- Every clinical note draft has an obvious **AI GENERATED — NOT REVIEWED** label until clinician-approved; generated uncertainty is explicit; clinician review reveals source evidence with transcript timestamps. A submitted/approved/signed state must never be visually ambiguous.
- Protect the clinical context against wrong-patient mix-ups: persistent name/MRN + DOB and encounter date in relevant workflows; interruption recovery requires revalidation.
- Clinical red/orange warnings are **severity-specific and text-labeled**; role-specific staff cannot inspect charts without permission. Never show patient details in browser notification, WhatsApp preview or nonclinical dashboard by default.
- Signed notes remain signed and read-only; amendments use visible addenda. Distinguish `ordered`, `result received`, `reviewed`, `patient contacted`, `follow-up completed` with distinct state-machine UI.
- Consent refusal must lead to usable manual notes; do not guilt-trip the patient. Offline/no-clip workflows must still work.
- One doctor can complete the primary visit with minimal context switching, but avoid unsafe one-click approval: note, Rx, investigation and communication have appropriate confirmation boundaries.
- Protect patient data in demo: synthetic only. All visual examples must avoid actual real-person healthcare data.

## 8. Prototype interaction acceptance tests

The clickable prototype passes when an observer can:

1. Create a new solo-doctor facility, define schedule and invite reception.
2. Register a synthetic patient, book a visit, check-in and assign a nurse.
3. Record vitals/allergies, hand off to doctor and open verified Patient 360.
4. Capture consent and run simulated ambient recording using phone; handle a declined-consent alternative.
5. Simulate a multi-speaker transcript (doctor, patient, relative) and show ambiguous identity verification.
6. Review an AI draft side-by-side with evidence, modify it and sign an immutable clinical note.
7. Approve a mock prescription and lab order; show separate result receipt/doctor acknowledgment.
8. Create an assigned care plan, send a minimal opt-in reminder and close a verified follow-up.
9. Generate an invoice and mark the external provider's payment confirmation, separate from clinical sign-off.
10. Let a patient see only documents explicitly released to their portal and manage consent.
11. Pair/revoke a simulated clip and handle offline/retry statuses.
12. Switch to hospital mode, attach an external HIS, show record-authority badges, resolve a write-back conflict and inspect an audit event.
13. Complete all critical workflows via keyboard and on a narrow mobile layout (where role appropriate).

## 9. Output protocol — start to end without missing any thing

Produce in this order and **do not stop after the dashboard**:
1. Product visual identity + tokens, with one premium recommended palette.
2. Sitemap and role-permission matrix by PMS/EMR/AI/Care/Portal/Enterprise blocks.
3. Full screen matrix: role, feature block, phase, route, wireframe ID and completion state.
4. Low-fidelity wireframes and states for every screen above.
5. Twelve flagship high-fidelity screens: Doctor Today, Appointment Board, Patient 360, Consent/Capture, Live Recording, Transcript Review, Evidence-linked Note Review, Rx+Order Composer, Results Inbox, Care Plan, Patient Portal, Hospital Integration Admin.
6. Mobile responsive views for Doctor Today, capture, note review, appointment, patient records and wearable pairing.
7. Six clickable end-to-end journeys with navigation and recovery branches.
8. Component library, annotations, accessibility tests and developer handoff.
9. Final completeness audit mapping **every architecture domain and entity** to at least one screen or an explicitly backend-only module. List unbuilt frames honestly if output limits arise; never quietly omit a module.

**Begin with the site map + design tokens and build the complete product in a coherent sequence.** Keep the existing DoctorMate working name as a placeholder that can be swapped globally with the final brand without redesigning the experience.
