# Gap analysis: what a clinic would still miss (UAE + India)

Source: `Medvya Gap Analysis.dc.html`. The review was done from three seats: the doctor in the room, the receptionist at the desk, and the owner deciding whether to buy. **MUST** blocks a sale, **SHOULD** wins the sale, **LATER** can wait. The phase is assigned by PRD v2.

## 1 · Patient history from outside the clinic (G-01)

| Item | Priority | PRD phase | How it connects |
|---|---|---|---|
| UAE health exchange pull and push: search by EID at check-in; pull allergies, diagnoses, meds, labs and discharge summaries; push every signed visit | MUST | P5 (launch emirate), P8 (others) | HL7 v2 and FHIR after regulator onboarding and conformance testing. One module, three configurations |
| Old reports uploaded by the patient (portal or WhatsApp link). OCR/AI extraction into trends tagged "Unverified · patient upload" until the doctor accepts | MUST (gap) / SHOULD (PRD P5-05) | P5 | AI worker queue + Azure Document Intelligence. Original kept |
| India ABHA consent-based records (Scan & Share, consent request, linked records) | SHOULD | P8 | ABDM HIP/HIU APIs (FHIR bundles), sandbox certification |
| External lab and imaging feeds | SHOULD | P7 | HL7 ORU for labs, DICOMweb viewer links |
| Clinic inbox for letters (dedicated email address, auto-match, "needs filing" queue) | LATER | P8 | Inbound mail parser + matching service |

## 2 · Moving old data from Oracle and other systems (G-02)

**Recommended: Option B, recent data first + live sync (go live in 1–2 weeks).** Option A (full migration first) is the slowest and riskiest. Option C (link only) is a fallback.

Steps:
1. **Connect read-only.** Self-hosted integration runtime on a clinic PC, encrypted tunnel, no open ports. Data lands in raw staging in-country.
2. **Map with AI, approve by hand.** Column and code mappings (LOINC, ICD-10, formulary). Approved mappings are saved as a template per legacy vendor.
3. **Dry run and report.** Trial load into a test tenant. The owner signs a report before load: counts, duplicates, missing EID/ABHA, failed checks. Can be re-run any number of times.
4. **Load what day one needs.** Last 24 months of patients, future appointments, balances, active meds, allergies, problems, last results.
5. **Parallel run with live sync.** CDC via Debezium/LogMiner or GoldenGate for 1–2 weeks. Fallback to the old system is always possible.
6. **Backfill the rest.** Overnight batches. Paper/PDF indexed first, OCR on open. Legacy stays read-only for an agreed period.

Corrections:
- **Fix by rule, not by row.**
- Duplicate scoring includes Arabic–English spellings (Mohammed/Muhammad). High-confidence pairs merge in bulk; borderline ones are confirmed at the desk.
- Every imported value is badged "Imported" with its source. Merges and fixes are reversible and audited.
- The same pipeline handles SQL Server, MySQL, Excel/CSV, HL7/FHIR exports.

→ PRD v2 P5-01, P5-02 (screens N-04, N-05, design needed).

## 3 · Doctor in the room (G-03)

| Item | Priority | PRD phase |
|---|---|---|
| One timeline for all sources, filterable, source-labelled | MUST | P2 (E-02) |
| Medication reconciliation in one pass | MUST | P2 (E-07) |
| Regulator e-prescription and e-sick leave | MUST | P5 (N-09) |
| Pre-visit summary of outside history (2 min) | SHOULD | P3 (A-01) |
| Ambient notes in Arabic, English and Indian languages (mixed) | SHOULD | P6 |
| Letters and certificates from templates | SHOULD | P5 (N-09) |
| Vaccination records and recalls | LATER | P8 (E-08) |
| Offline mode (read today's charts, draft notes) | LATER | P8 (N-16) |

## 4 · Receptionist at the desk (G-04)

| Item | Priority | PRD phase |
|---|---|---|
| Emirates ID card reader check-in (< 30 s) | MUST | P5 (N-06) |
| Live insurance eligibility + co-pay estimate | MUST | P5 (P5-03) |
| Duplicate warning while typing | MUST | **P1** (P1-05) |
| WhatsApp reminders/confirmations (AR/EN/HI), one-tap reschedule, running-late broadcast | SHOULD | P4 (P4-09) |
| Queue and token screen on the waiting-room TV | SHOULD | **P1** (P1-08, P-05) |
| Scan to patient file (desk scanner / phone camera) | SHOULD | P2 (E-03) |
| Self check-in kiosk or QR | LATER | P8 (N-16) |

## 5 · What makes an owner say yes (G-05)

Migration included (dry-run report before signing) · No downtime (parallel run) · Regulator-ready (exchange certified, claims built in, ADHICS, residency) · Learn in one shift (30-minute role training, in-app guides) · Works on what they already have (browser, existing PCs; only an ID reader to buy) · No lock-in (full FHIR + CSV export any time).

## 6 · Voice appointment assistant (G-06), MUST · NEW

- Answers the clinic number and WhatsApp voice notes 24/7 or on overflow. Arabic, English, Hindi and Malayalam, with mid-call language switching.
- Books, moves and cancels from the live calendar and holds the slot.
- Identifies callers by phone + DOB, or the last 4 digits of EID/ABHA, **before** sharing details.
- Checks insurance and co-pay. Handles waitlist fill and outbound reminder/recall calls (P6). Answers routine questions.
- **Safety:** no medical advice, triage or results by phone. Emergency words trigger the emergency script and an immediate transfer. Always says it is automated.
- **Handover:** on request, after 2 failed attempts, for complaints, billing disputes or VIPs. Staff see the transcript and summary first.
- **Owner controls:** on/off per branch and hour, bookable doctors, greeting, voice, recording-consent wording, weekly report.
- **Connects via** Azure Communication Services (number + SIP trunk), in-region speech and LLM, the same PMS appointment API.
- **Reception console:** live calls (listen / take over), handed-to-staff list, daily counts.

→ PRD v2 P5-06 (inbound), P6-04 (outbound). Screen N-10 (design needed).

## 7 · Suggested order (gap analysis), mapped to PRD

- **Now · before first sale:** migration pipeline + dry run; duplicate matching + DQ queue; EID reader; exchange for the launch emirate; live eligibility. → P5
- **Next · first 5 clinics:** report upload + extraction; unified timeline + med reconciliation (P2); eRx + sick leave; CDC parallel run; WhatsApp + queue screen (P4/P1); voice assistant inbound. → P5
- **Later · scale:** other two UAE exchanges and ABHA; saved mapping templates; multilingual ambient notes; outbound voice; letter inbox, kiosk; vaccinations, offline. → P6–P8

> Regulator programmes and interface specs change. Confirm current Malaffi, NABIDH, Riayati, eClaimLink, Shafafiya and ABDM onboarding requirements before committing dates.
