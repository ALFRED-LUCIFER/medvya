# Sprint plan and roadmap: India first, whole app by end of January 2027

Generated 6 Nov 2026 from the ticket sources and issue statuses. **Seq** is the priority order; **Start / Target** are dates from a day-level schedule that assumes **14 agents working in parallel**, one ticket each, dependencies strict, start Mon 9 Nov 2026. Fewer agents push the dates out (see the table). Live board: GitHub Project 'Medvya: doctor path to pilot, then the rest'. Status of record stays on the GitHub issues.

Fixed dates and waits:
- **India DLT sender and SMS templates (MDV-P1-045) land end of December 2026.** Every ticket that needs SMS starts on or after 4 Jan 2027.
- R1 pilot (MDV-P1-042) is a 2-week calendar wait; the 30-day availability window (MDV-P5-027) is a 22-working-day wait.
- Legal wording (P1-044), pilot agreements (P1-043), clinical sign-off (P1-073, moved from P0-023) and the external security reviews are people items with no date yet: any slip moves everything after them.

| Journey | Open days | Starts | Ends |
|---|---|---|---|
| J1 Pilot clinic path (India) | 116 | 2026-11-09 | 2027-01-20 |
| J2 Doctor sees the patient | 59.5 | 2026-11-10 | 2026-12-09 |
| J3 AI notes under review | 57.5 | 2026-11-17 | 2026-12-30 |
| J4 Billing and patient portal | 71.5 | 2026-11-25 | 2026-12-31 |
| J5 Platform admin and SaaS ops | 45.5 | 2026-12-16 | 2027-01-08 |
| J6 Mobile apps | 33 | 2026-12-22 | 2027-01-15 |
| J7 Launch and scale (India) | 55 | 2026-12-25 | 2027-01-26 |
| J8 Later - advanced and hospital | 130 | 2027-01-06 | 2027-02-01 |

## Order


### J1 Pilot clinic path (India)

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 1 | 2026-11-09 | 2026-12-31 | MDV-P1-045 Register India DLT sender ID and SMS templates (and UAE SMS sender if applicable | 0.5 | To do | India + UAE | MUST |  |
| 2 | 2026-11-09 | 2026-11-09 | MDV-P0-002 Record and accept ADR-001 (stack) and supporting ADRs 003–006 | 0.5 | Partly done | India | MUST |  |
| 3 | 2026-11-09 | 2026-11-09 | MDV-P0-003 Decide launch market (ADR-002) and confirm Azure subscription and region | 0.5 | Partly done | India | MUST |  |
| 4 | 2026-11-10 | 2026-11-11 | MDV-P0-006 Azure environments as code (Bicep): App Service, Azure SQL private endpoint, Blo | 3 | Partly done | India | MUST | MDV-P0-003 |
| 5 | 2026-11-12 | 2026-11-12 | MDV-P0-007 GitHub OIDC federation to Azure and tagged deploy to staging | 1.5 | Partly done | India | MUST | MDV-P0-004, MDV-P0-006 |
| 6 | 2026-11-12 | 2026-11-12 | MDV-P0-008 Database security baseline part 1: Entra-only auth, managed identity, least-priv | 1.5 | Partly done | India | MUST | MDV-P0-006 |
| 7 | 2026-11-13 | 2026-11-16 | MDV-P0-009 Database security baseline part 2: Always Encrypted (national/insurance IDs), dy | 2.5 | Partly done | India | MUST | MDV-P0-008 |
| 8 | 2026-11-13 | 2026-11-16 | MDV-P0-011 Staff sign-in through the BFF with Entra External ID: MFA (TOTP), passkeys and r | 3 | Partly done | India | MUST | MDV-P0-007 |
| 9 | 2026-11-17 | 2026-11-17 | MDV-P0-012 Session control: facility picker, idle timeout on shared clinic PCs, device/sess | 2 | Partly done | India | MUST | MDV-P0-011, MDV-P0-010 |
| 10 | 2026-11-09 | 2026-11-10 | MDV-P0-014 Server-side authorisation from the Foundations role-permission matrix | 3 | Partly done | India | MUST | MDV-P0-010 |
| 11 | 2026-11-09 | 2026-11-10 | MDV-P0-017 Append-only audit log on Azure SQL ledger tables with audit middleware | 2.5 | Partly done | India | MUST | MDV-P0-010, MDV-P0-016 |
| 12 | 2026-11-09 | 2026-11-09 | MDV-P0-018 Observability with OpenTelemetry and PHI redaction (structured logs, telemetry p | 1.5 | Partly done | India | MUST | MDV-P0-016 |
| 13 | 2026-11-17 | 2026-11-17 | MDV-P0-013 Invite a colleague and accept the invitation (membership invited → active) | 2 | Partly done | India | MUST | MDV-P0-011, MDV-P0-014 |
| 14 | 2026-11-09 | 2026-11-09 | MDV-P0-015 System state screens: session expired, 403 no facility access, 404, offline | 1 | Partly done | India | MUST | MDV-P0-020 |
| 15 | 2026-11-12 | 2026-11-12 | MDV-P0-019 Backups, point-in-time restore and a timed restore drill | 1 | To do | India | MUST | MDV-P0-006 |
| 16 | 2026-11-11 | 2026-11-11 | MDV-P0-027 Synthetic data generator and seeded staging tenant | 1 | Partly done | India | MUST | MDV-P0-010, MDV-P0-014 |
| 17 | 2026-11-12 | 2026-11-12 | MDV-P0-023 Six end-to-end journeys as Playwright specs and clinical advisor sign-off (X-03) | 2 | Partly done | India | MUST | MDV-P0-022, MDV-P0-027 |
| 18 | 2026-11-09 | 2026-11-09 | MDV-P0-028 Per-tenant feature flags with owner and kill switch | 1 | Partly done | India | MUST | MDV-P0-010 |
| 19 | 2026-11-13 | 2026-11-13 | MDV-P0-029 R0 exit gate review and release notes (internal platform) | 0.5 | To do | India | MUST | MDV-P0-007, MDV-P0-010, MDV-P0-019, MDV-P0-023, MDV-P0-024 |
| 20 | 2026-11-09 | 2026-11-09 | MDV-P1-002 Country and data-residency selector that binds a new tenant to one data plane | 1.5 | Partly done | India | SHOULD | MDV-P1-001 |
| 21 | 2026-11-10 | 2026-11-11 | MDV-P1-004 Solo doctor and clinic sign-up with the six-step onboarding wizard (autosave) | 3 | Partly done | India | MUST | MDV-P1-002 |
| 22 | 2026-11-09 | 2026-11-09 | MDV-P1-043 Sign pilot agreements with two partner clinics | 1 | To do | India | MUST |  |
| 23 | 2026-11-09 | 2026-11-09 | MDV-P1-044 Legal review of privacy notice, ToS, DPA and consent wording (DPDP India; UAE he | 1 | To do | India + UAE | MUST | MDV-P0-024 |
| 24 | 2026-11-12 | 2026-11-13 | MDV-P1-005 Practice settings: profile, locations, operating hours and holidays (F-08) | 2.5 | Partly done | India | MUST | MDV-P1-004 |
| 25 | 2026-11-17 | 2026-11-18 | MDV-P1-013 Patient registration — India full flow (identity, ABHA optional, Aadhaar last 4, | 4 | Partly done | India | MUST | MDV-P0-009, MDV-P0-017 |
| 26 | 2026-11-19 | 2026-11-20 | MDV-P1-019 Legal terms and separate versioned consents at registration (treatment, reminder | 3 | Partly done | India | MUST | MDV-P1-013, MDV-P1-044 |
| 27 | 2026-11-18 | 2026-11-18 | MDV-P1-009 Staff offboarding removes access immediately and ends all sessions | 1 | Partly done | India | MUST | MDV-P1-008, MDV-P0-012 |
| 28 | 2026-11-19 | 2026-11-19 | MDV-P1-015 Quick registration for a busy desk with a completion task | 1.5 | Partly done | India | MUST | MDV-P1-013 |
| 29 | 2026-11-09 | 2026-11-10 | MDV-P1-021 Create an appointment from the calendar with a short slot hold | 2.5 | Partly done | India | MUST | MDV-P1-011, MDV-P1-018 |
| 30 | 2026-11-11 | 2026-11-12 | MDV-P1-012 Leave and schedule exceptions with re-offer of affected appointments | 1.5 | To do | India | MUST | MDV-P1-011, MDV-P1-021 |
| 31 | 2026-11-19 | 2026-11-25 | MDV-P1-047 Safe retries: idempotency keys, request timeouts and bounded retry (merges the p | 5 | To do | India | SHOULD | MDV-P1-021, MDV-P1-013, MDV-P1-028 |
| 32 | 2026-11-09 | 2026-11-10 | MDV-P1-023 Calendar views: day, week and list (per doctor and resource), mobile/tablet resp | 3 | Partly done | India | MUST | MDV-P1-011 |
| 33 | 2026-11-19 | 2026-11-19 | MDV-P1-036 Tablet-responsive reception views (desk tablet) | 1 | To do | India | MUST | MDV-P1-028, MDV-P1-023, MDV-P1-013 |
| 34 | 2026-11-09 | 2026-11-11 | MDV-P1-017 Duplicate / merge review with second approver when both records hold clinical da | 2.5 | To do | India | MUST | MDV-P1-016 |
| 35 | 2026-11-11 | 2026-11-11 | MDV-P1-022 No double booking under concurrency (unique index, serializable transaction, doc | 2 | Partly done | India | MUST | MDV-P1-021 |
| 36 | 2026-11-12 | 2026-11-12 | MDV-P1-024 Reschedule (incl. drag with confirm), cancel with reason, mark no-show with rebo | 2 | Partly done | India | MUST | MDV-P1-021, MDV-P1-022 |
| 37 | 2026-11-20 | 2026-11-20 | MDV-P1-025 Walk-in registration and booking straight to the queue | 1 | Partly done | India | MUST | MDV-P1-021, MDV-P1-015 |
| 38 | 2026-11-23 | 2026-11-24 | MDV-P1-032 Email confirmations and 24 h / 2 h reminders via SendGrid and Hangfire | 2.5 | Partly done | India | MUST | MDV-P1-021, MDV-P1-019 |
| 39 | 2026-11-25 | 2026-11-26 | MDV-P1-034 Notification centre for staff (D-03), push-safe content | 1.5 | To do | India | MUST | MDV-P1-032, MDV-P0-022 |
| 40 | 2027-01-04 | 2027-01-04 | MDV-P1-033 SMS confirmations and reminders with India DLT templates and delivery receipts | 2 | Partly done | India | MUST | MDV-P1-032, MDV-P1-045 |
| 41 | 2026-11-25 | 2026-11-26 | MDV-P1-006 Practice settings: notification preferences and data tab | 1.5 | To do | India | MUST | MDV-P1-005, MDV-P1-032 |
| 42 | 2026-11-25 | 2026-11-26 | MDV-P1-010 Licence/registration expiry reminders and quarterly access review prompt | 1.5 | To do | India | MUST | MDV-P1-007, MDV-P1-032 |
| 43 | 2026-11-12 | 2026-11-13 | MDV-P1-026 Recurring appointments (series) with per-occurrence exceptions | 1.5 | To do | India | MUST | MDV-P1-022 |
| 44 | 2027-01-05 | 2027-01-06 | MDV-P1-027 Waitlist with "offer earlier slot" | 1.5 | To do | India | MUST | MDV-P1-024, MDV-P1-033 |
| 45 | 2026-11-11 | 2026-11-11 | MDV-P1-035 Practice access log for owners (F-12): who opened which record and when | 1.5 | Partly done | India | MUST | MDV-P0-017 |
| 46 | 2026-11-25 | 2026-12-02 | MDV-P1-052 Front-desk fixes from the live walk-through, round 2 (queue displays, cancelled  | 6 | To do | India | MUST | MDV-P1-029, MDV-P1-031, MDV-P1-025, MDV-P1-023, MDV-P1-035, MDV-P1-032 |
| 47 | 2026-11-12 | 2026-11-13 | MDV-P1-037 Performance budget and load test for booking and calendar | 1.5 | To do | India | MUST | MDV-P1-022, MDV-P1-023 |
| 48 | 2026-11-23 | 2026-11-24 | MDV-P1-038 P1 security review: threat-model update, ASVS checks, authorisation tests, depen | 2 | To do | India | MUST | MDV-P1-019, MDV-P1-009, MDV-P1-035 |
| 49 | 2026-11-25 | 2026-11-26 | MDV-P1-039 Production deployment for the launch country and a production tenant per pilot c | 1.5 | To do | India | MUST | MDV-P1-038, MDV-P1-043 |
| 50 | 2026-11-27 | 2026-11-30 | MDV-P1-040 Reception training (30 min), one-page desk card and support channel with named c | 1.5 | To do | India | MUST | MDV-P1-039 |
| 51 | 2027-01-05 | 2027-01-06 | MDV-P1-041 Baseline metrics capture and in-product measurement (check-in time, no-shows, do | 1.5 | To do | India | MUST | MDV-P1-028, MDV-P1-033 |
| 52 | 2026-11-10 | 2026-11-10 | MDV-P2-023 Define and operate the doctor licence verification process | 0.5 | To do | India | MUST | MDV-P1-007 |
| 53 | 2027-01-07 | 2027-01-20 | MDV-P1-042 R1 pilot (2 consecutive weeks of real bookings), exit gate review and release no | 2 | To do | India | MUST | MDV-P1-039, MDV-P1-040, MDV-P1-041, MDV-P1-022, MDV-P1-038 |
| 54 | 2026-11-26 | 2026-11-27 | MDV-P1-049 Service worker for static assets only (app shell loads with no network, no patie | 2 | To do | India | COULD | MDV-P1-047 |
| 55 | 2026-11-11 | 2026-11-13 | MDV-P1-050 Printable day sheet for the desk (no phone numbers or national IDs) | 3 | To do | India | COULD | MDV-P1-023 |
| 56 | 2026-11-16 | 2026-11-20 | MDV-P1-051 Encrypted read-only schedule cache for outages (needs an ADR first) | 5 | To do | India | COULD | MDV-P1-050 |
| 57 | 2026-11-17 | 2026-11-20 | MDV-P5-040 Platform console shell (`web/admin`): company sign-on plus security key, platfor | 4 | To do | India | MUST | MDV-P0-011 |
| 58 | 2026-11-23 | 2026-11-25 | MDV-P5-044 Licence verification console: reviewer queue, certificate view, approve or ask f | 2.5 | To do | India | MUST | MDV-P5-040, MDV-P1-007, MDV-P2-023 |

### J2 Doctor sees the patient

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 59 | 2026-11-10 | 2026-11-11 | MDV-P2-001 Encounters module: start a consultation from the board/Today, bound to one patie | 2 | To do | India | MUST | MDV-P1-028 |
| 60 | 2026-11-12 | 2026-11-16 | MDV-P2-002 Doctor Today (D-01): schedule, patient flags, pending notes, inbox counts | 3 | To do | India | MUST | MDV-P2-001 |
| 61 | 2026-11-11 | 2026-11-12 | MDV-P2-021 Clinical safety case and hazard log with a named clinical safety reviewer | 2 | To do | India | MUST | MDV-P0-024 |
| 62 | 2026-11-11 | 2026-11-11 | MDV-P2-022 Legal review of clinical data processing before the first real note | 0.5 | To do | India | MUST | MDV-P1-044 |
| 63 | 2026-11-12 | 2026-11-13 | MDV-P2-024 Clinical authorisation: care relationship and purpose checks, request access for | 2 | To do | India | MUST | MDV-P0-014 |
| 64 | 2026-11-16 | 2026-11-17 | MDV-P2-003 Privacy-safe global search (⌘K) with care-relationship check and "Request access | 2 | To do | India | MUST | MDV-P1-018, MDV-P2-024 |
| 65 | 2026-11-16 | 2026-11-18 | MDV-P2-004 Patient 360 (E-01): banner with identity and allergies always visible; problems, | 3 | To do | India | MUST | MDV-P2-001, MDV-P2-024 |
| 66 | 2026-11-12 | 2026-11-16 | MDV-P2-007 Nurse intake queue and vitals with out-of-range re-measure and escalation (N-01) | 3 | To do | India | MUST | MDV-P2-001, MDV-P1-028 |
| 67 | 2026-11-19 | 2026-11-23 | MDV-P2-005 Longitudinal timeline and trends with source labels and filters (E-02) | 2.5 | To do | India | MUST | MDV-P2-004 |
| 68 | 2026-11-19 | 2026-11-23 | MDV-P2-006 Documents: upload, malware scan, secure viewer with time-limited links, scan to  | 3 | To do | India | MUST | MDV-P2-004 |
| 69 | 2026-11-17 | 2026-11-18 | MDV-P2-008 Allergy verification and patient-reported medicines with handoff to the doctor ( | 2 | To do | India | MUST | MDV-P2-007 |
| 70 | 2026-11-16 | 2026-11-18 | MDV-P2-014 Country formulary and order sets: India (Schedule H/H1/X) and UAE drug codes, ad | 3 | To do | India + UAE | MUST | MDV-P1-005 |
| 71 | 2026-11-16 | 2026-11-17 | MDV-P2-025 Note template engine and specialty template pack for the partner clinics | 1.5 | To do | India | MUST | MDV-P1-004 |
| 72 | 2026-11-19 | 2026-11-23 | MDV-P2-009 Consultation note editor: chief complaint, SOAP, specialty templates, manual not | 3 | To do | India | MUST | MDV-P2-004, MDV-P2-025 |
| 73 | 2026-11-24 | 2026-11-25 | MDV-P2-010 Diagnosis coding with ICD-10 and problem verification states (E-07) | 2 | To do | India | MUST | MDV-P2-009 |
| 74 | 2026-11-19 | 2026-11-23 | MDV-P2-011 Medication reconciliation: chart vs patient-reported vs other source, keep / cha | 2.5 | To do | India | MUST | MDV-P2-008 |
| 75 | 2026-11-24 | 2026-11-26 | MDV-P2-012 Sign and finalise: immutable signed note v1 with re-authentication (E-06) | 2.5 | To do | India | MUST | MDV-P2-009, MDV-P1-007 |
| 76 | 2026-11-24 | 2026-11-26 | MDV-P2-015 Prescription composer with dose builder and add-medicine-while-prescribing (E-05 | 3 | To do | India | MUST | MDV-P2-014, MDV-P2-011 |
| 77 | 2026-11-27 | 2026-11-30 | MDV-P2-013 Addenda, version history and print/PDF of the signed note (E-06) | 2 | To do | India | MUST | MDV-P2-012 |
| 78 | 2026-11-27 | 2026-12-01 | MDV-P2-016 Allergy and interaction safety checks before authorisation | 3 | To do | India | MUST | MDV-P2-015 |
| 79 | 2026-12-02 | 2026-12-04 | MDV-P2-017 Rx authorisation (separate from note), server-side multilingual printed Rx A4/A5 | 3 | To do | India | MUST | MDV-P2-016, MDV-P1-007 |
| 80 | 2026-11-19 | 2026-11-20 | MDV-P2-018 Investigation orders composer with Draft → Ordered states (E-05) | 2 | To do | India | MUST | MDV-P2-014 |
| 81 | 2026-11-23 | 2026-11-24 | MDV-P2-019 Referrals out and in with status (E-09) | 2 | To do | India | MUST | MDV-P2-018 |
| 82 | 2026-12-07 | 2026-12-08 | MDV-P2-020 Share Rx with patient/pharmacy (e-Rx PDF/link) and pharmacy dispatch record (E-1 | 1.5 | To do | India | MUST | MDV-P2-017 |
| 83 | 2026-12-07 | 2026-12-07 | MDV-P2-026 Doctor and nurse training (30 min each) and R2 materials | 1 | To do | India | MUST | MDV-P2-017 |
| 84 | 2026-12-07 | 2026-12-07 | MDV-P2-027 Print, accessibility and zoom verification across clinical screens | 1 | To do | India | MUST | MDV-P2-013, MDV-P2-017 |
| 85 | 2026-12-08 | 2026-12-09 | MDV-P2-028 R2 pilot, exit gate review (≥ 20 signed visits per doctor) and release notes | 1.5 | To do | India | MUST | MDV-P2-012, MDV-P2-016, MDV-P2-017, MDV-P2-021, MDV-P2-022, MDV-P2-026 |

### J3 AI notes under review

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 86 | 2026-11-17 | 2026-11-18 | MDV-P3-001 Spike: Azure AI Speech and Azure OpenAI in-region capability check and ADR for s | 2 | To do | India | MUST | MDV-P0-003 |
| 87 | 2026-11-23 | 2026-11-24 | MDV-P3-002 Consent templates and recording policy administration (H-09) | 2 | To do | India | MUST | MDV-P1-019 |
| 88 | 2026-11-25 | 2026-11-27 | MDV-P3-003 Per-encounter recording consent with device selection; decline goes straight to  | 3 | To do | India | MUST | MDV-P3-002, MDV-P2-009 |
| 89 | 2026-11-24 | 2026-11-24 | MDV-P3-022 AI vendor DPA, no-training guarantee and regional processing confirmed per count | 0.5 | To do | India | MUST | MDV-P3-001 |
| 90 | 2026-11-25 | 2026-11-25 | MDV-P3-023 Legal review of recording consent in each market | 0.5 | To do | India | MUST | MDV-P1-044 |
| 91 | 2026-11-30 | 2026-12-02 | MDV-P3-005 Capture on phone (mobile web) or desktop mic with timer, pause, consent indicato | 3 | To do | India | MUST | MDV-P3-003 |
| 92 | 2026-11-25 | 2026-11-27 | MDV-P3-007 Azure Service Bus and .NET AI worker skeleton without database credentials (scop | 3 | To do | India | MUST | MDV-P3-001 |
| 93 | 2026-12-03 | 2026-12-04 | MDV-P3-004 Consent revoked mid-visit stops capture and discards later audio (A-09) | 1.5 | To do | India | MUST | MDV-P3-005 |
| 94 | 2026-12-03 | 2026-12-07 | MDV-P3-006 Encrypted chunked upload with resume; upload progress, failed/partial recovery s | 3 | To do | India | MUST | MDV-P3-005 |
| 95 | 2026-12-08 | 2026-12-10 | MDV-P3-008 Transcription with diarisation and language identification (in-region Azure AI S | 3 | To do | India | MUST | MDV-P3-007, MDV-P3-006 |
| 96 | 2026-12-11 | 2026-12-15 | MDV-P3-009 Multi-speaker transcript review with speaker confirmation (A-04) | 2.5 | To do | India | MUST | MDV-P3-008 |
| 97 | 2026-12-16 | 2026-12-17 | MDV-P3-010 Overlapping speech and unconfirmed speaker handling (A-10) | 1.5 | To do | India | MUST | MDV-P3-009 |
| 98 | 2026-12-11 | 2026-12-16 | MDV-P3-011 Draft note generation with JSON schema, evidence links and "unknown stays blank" | 4 | To do | India | MUST | MDV-P3-008, MDV-P2-025 |
| 99 | 2026-12-17 | 2026-12-22 | MDV-P3-018 De-identified evaluation set and CI gate before any prompt or model change | 4 | To do | India | MUST | MDV-P3-011 |
| 100 | 2026-11-30 | 2026-12-01 | MDV-P3-016 AI unavailable → editable manual note with no data loss (A-06) | 1.5 | To do | India | MUST | MDV-P3-007, MDV-P2-009 |
| 101 | 2026-12-17 | 2026-12-22 | MDV-P3-012 Note review with evidence panel and audio replay; accept/edit per field; sign ga | 4 | To do | India | MUST | MDV-P3-011, MDV-P2-012 |
| 102 | 2026-12-11 | 2026-12-14 | MDV-P3-015 Emergency-symptom warning as a prompt, not triage (A-08) | 1.5 | To do | India | MUST | MDV-P3-008 |
| 103 | 2026-12-11 | 2026-12-14 | MDV-P3-017 Wrong-patient suspicion locks the session; re-binding needs a reason and is audi | 1.5 | To do | India | MUST | MDV-P3-008 |
| 104 | 2026-12-23 | 2026-12-24 | MDV-P3-013 Template switch and regeneration with confirmation; clinician edits tracked | 1.5 | To do | India | MUST | MDV-P3-012 |
| 105 | 2026-12-17 | 2026-12-21 | MDV-P3-014 Pre-visit brief: verified chart facts visibly separate from AI summary (A-01) | 2.5 | To do | India | SHOULD | MDV-P3-011, MDV-P2-004 |
| 106 | 2026-12-08 | 2026-12-09 | MDV-P3-019 Audio retention purge and legal hold | 1.5 | To do | India | MUST | MDV-P3-006, MDV-P3-002 |
| 107 | 2026-12-23 | 2026-12-25 | MDV-P3-021 Mobile capture and note review (swipe to accept, evidence bottom sheet) (X-01) | 2.5 | To do | India | MUST | MDV-P3-012, MDV-P3-005 |
| 108 | 2026-12-23 | 2026-12-24 | MDV-P3-020 Dictation of note sections with the same review-and-approve rule | 2 | To do | India | SHOULD | MDV-P3-012 |
| 109 | 2026-12-23 | 2026-12-23 | MDV-P3-024 AI safety card, waiting-room consent notice (EN/HI/AR) and AI incident procedure | 1 | To do | India | MUST | MDV-P3-018, MDV-P3-023 |
| 110 | 2026-12-17 | 2026-12-18 | MDV-P3-025 Performance: draft ready ≤ 60 s (p50) after recording stops | 1.5 | To do | India | MUST | MDV-P3-011 |
| 111 | 2026-12-25 | 2026-12-28 | MDV-P3-026 Clinician quality metrics: minor-edit rate, trust survey and note time vs P2 bas | 1.5 | To do | India | MUST | MDV-P3-013 |
| 112 | 2026-12-29 | 2026-12-30 | MDV-P3-027 R3 pilot with 5–10 opt-in clinicians, exit gate review and release notes | 1.5 | To do | India | MUST | MDV-P3-018, MDV-P3-024, MDV-P3-026, MDV-P3-003 |

### J4 Billing and patient portal

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 113 | 2026-11-25 | 2026-11-27 | MDV-P4-002 Services and fees catalogue with price versions, India GST/SAC, UAE CPT/VAT, pay | 3 | To do | India + UAE | MUST | MDV-P4-001 |
| 114 | 2026-11-26 | 2026-11-26 | MDV-P4-032 Apply for payment gateway merchant accounts and WhatsApp Business template appro | 0.5 | To do | India | MUST |  |
| 115 | 2026-11-30 | 2026-12-01 | MDV-P4-003 Price list import tool (billing setup service) | 1.5 | To do | India | MUST | MDV-P4-002 |
| 116 | 2026-11-30 | 2026-12-02 | MDV-P4-004 Charge capture and invoice lifecycle (draft → issued → paid/failed/refunded), se | 3 | To do | India | MUST | MDV-P4-002 |
| 117 | 2026-12-03 | 2026-12-07 | MDV-P4-005 Receipts, tax invoices, credit notes, advance receipts for India GST and UAE VAT | 3 | To do | India + UAE | MUST | MDV-P4-004 |
| 118 | 2026-12-03 | 2026-12-07 | MDV-P4-007 India payments with Razorpay (UPI collect/QR, card, refunds) and idempotent webh | 3 | To do | India | MUST | MDV-P4-004, MDV-P4-032 |
| 119 | 2026-11-27 | 2026-12-01 | MDV-P4-014 Results intake and inbound report identity match (manual upload/entry; low-confi | 3 | To do | India | MUST | MDV-P2-006, MDV-P2-018 |
| 120 | 2026-11-30 | 2026-12-02 | MDV-P4-020 Patient portal authentication: OTP sign-in, portal invitations, patient session  | 2.5 | To do | India | MUST | MDV-P1-019 |
| 121 | 2026-12-08 | 2026-12-09 | MDV-P4-006 Refunds and credit notes | 1.5 | To do | India | MUST | MDV-P4-005, MDV-P4-007 |
| 122 | 2026-12-02 | 2026-12-04 | MDV-P4-015 Clinical results inbox: severity in words, review/acknowledge, assign follow-up, | 3 | To do | India | MUST | MDV-P4-014 |
| 123 | 2026-12-07 | 2026-12-08 | MDV-P4-022 Release rules: the doctor controls what patients see and when | 1.5 | To do | India | MUST | MDV-P4-015, MDV-P2-012 |
| 124 | 2026-12-09 | 2026-12-11 | MDV-P4-021 Portal home, released visit summaries (with checked translations), prescriptions | 3 | To do | India | MUST | MDV-P4-020, MDV-P4-022 |
| 125 | 2026-12-08 | 2026-12-10 | MDV-P4-034 UPI AutoPay for packages, secure pay link by WhatsApp, Apple Pay and Google Pay  | 3 | To do | India + UAE | SHOULD | MDV-P4-007, MDV-P4-008 |
| 126 | 2026-12-08 | 2026-12-09 | MDV-P4-009 Split and partial payments with advance usage; balance moves to dues (P-10) | 2 | To do | India | MUST | MDV-P4-007 |
| 127 | 2026-12-10 | 2026-12-11 | MDV-P4-010 Packages and advances with session redemption (P-11) | 2 | To do | India | MUST | MDV-P4-002, MDV-P4-009 |
| 128 | 2026-12-07 | 2026-12-09 | MDV-P4-016 Care plans and tasks with owners, due dates, stage tracks and evidence to close  | 3 | To do | India | MUST | MDV-P4-015, MDV-P2-012 |
| 129 | 2026-12-09 | 2026-12-09 | MDV-P4-031 Invoice and receipt formats reviewed by a chartered accountant (India) and tax a | 0.5 | To do | India + UAE | MUST | MDV-P4-005 |
| 130 | 2026-12-10 | 2026-12-11 | MDV-P4-011 Dues, ageing and patient statement with no-PHI balance reminders (P-12) | 2 | To do | India | MUST | MDV-P4-009 |
| 131 | 2026-12-10 | 2026-12-14 | MDV-P4-012 Cash reconciliation and day-end close with denominations, second-person confirma | 3 | To do | India | MUST | MDV-P4-009, MDV-P4-008 |
| 132 | 2026-12-10 | 2026-12-11 | MDV-P4-017 Patient contact log and reminders: outcome required; "delivered" is not a contac | 2 | To do | India | MUST | MDV-P4-016 |
| 133 | 2026-12-14 | 2026-12-15 | MDV-P4-018 Overdue escalation: escalate to doctor, reassign, or close with reason (C-04) | 1.5 | To do | India | MUST | MDV-P4-017 |
| 134 | 2026-12-10 | 2026-12-14 | MDV-P4-023 Portal booking and reschedule with fee and cancellation policy shown (R-02) | 2.5 | To do | India | MUST | MDV-P4-020, MDV-P1-022 |
| 135 | 2026-12-15 | 2026-12-17 | MDV-P4-028 WhatsApp reminders and confirmations (EN/AR/HI), opt-in records, delivery status | 3 | To do | India | MUST | MDV-P1-032, MDV-P4-023 |
| 136 | 2026-12-11 | 2026-12-11 | MDV-P4-029 Spike: reporting store that keeps PHI out of analytics (read replica vs warehous | 1 | To do | India | MUST | MDV-P0-016 |
| 137 | 2026-12-15 | 2026-12-16 | MDV-P4-013 Doctor revenue and payout report (P-09) | 1.5 | To do | India | MUST | MDV-P4-012 |
| 138 | 2026-12-14 | 2026-12-16 | MDV-P4-019 Clinic messages inbox (patient ↔ clinic) with AI reply draft approved by staff ( | 3 | To do | India | SHOULD | MDV-P4-020, MDV-P3-007 |
| 139 | 2026-12-14 | 2026-12-15 | MDV-P4-024 Portal consent and privacy controls with policy version and change log (R-03) | 1.5 | To do | India | MUST | MDV-P4-020, MDV-P1-019 |
| 140 | 2026-12-15 | 2026-12-17 | MDV-P4-025 Caregiver proxy authorisation with scopes, expiry, OTP verification and revocati | 2.5 | To do | India | MUST | MDV-P4-020 |
| 141 | 2026-12-15 | 2026-12-16 | MDV-P4-026 Portal Arabic RTL and patient help pages in three languages | 1.5 | To do | India | MUST | MDV-P4-021 |
| 142 | 2026-12-16 | 2026-12-18 | MDV-P4-027 Public doctor and clinic profiles with live availability, indexed for search (N- | 3 | To do | India | SHOULD | MDV-P4-023 |
| 143 | 2026-12-29 | 2026-12-31 | MDV-P4-030 Clinic reports and owner dashboard: appointments, no-shows, revenue, follow-up c | 3 | To do | India | MUST | MDV-P4-029, MDV-P4-013, MDV-P3-026 |
| 144 | 2026-12-18 | 2026-12-21 | MDV-P4-033 R4 pilot, exit gate review and release notes | 1.5 | To do | India | MUST | MDV-P4-012, MDV-P4-015, MDV-P4-031, MDV-P4-028, MDV-P4-023 |

### J5 Platform admin and SaaS ops

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 145 | 2026-12-16 | 2026-12-21 | MDV-P5-001 Design the 11 new P5 screens (N-04 … N-14) before build | 4 | To do | India | MUST |  |
| 146 | 2026-12-22 | 2026-12-25 | MDV-P5-016 Medvya subscription: plans, trial, subscription invoices with GST/VAT, seat coun | 4 | To do | India | MUST | MDV-P5-001, MDV-P4-001 |
| 147 | 2026-12-22 | 2026-12-25 | MDV-P5-017 Platform operations console: tenant catalogue, onboarding automation, per-tenant | 4 | To do | India | MUST | MDV-P5-001, MDV-P0-028, MDV-P5-040 |
| 148 | 2026-12-28 | 2026-12-30 | MDV-P5-042 Plans and entitlements: versioned plans, India and UAE prices, seats, trial days | 2.5 | To do | India + UAE | MUST | MDV-P5-016, MDV-P5-040 |
| 149 | 2027-01-07 | 2027-01-08 | MDV-P5-050 Public pricing page with 14-day trial, monthly and yearly toggle and country cur | 1.5 | To do | India | SHOULD | MDV-P5-042, MDV-P1-027 |
| 150 | 2026-12-17 | 2026-12-21 | MDV-P5-041 Feature rollouts: staged release (internal, beta tenants, percentage, everyone), | 3 | To do | India | MUST | MDV-P5-040, MDV-P0-028 |
| 151 | 2026-12-28 | 2026-12-31 | MDV-P5-043 Subscription billing in the console and for owners: invoices, payment method, ad | 3.5 | To do | India | MUST | MDV-P5-016, MDV-P5-040 |
| 152 | 2026-12-17 | 2026-12-18 | MDV-P5-046 Legal documents: versioned terms, privacy notice and DPA, re-acceptance gate tha | 2 | To do | India | MUST | MDV-P5-040, MDV-P1-044 |
| 153 | 2026-12-22 | 2026-12-24 | MDV-P5-018 In-app role guides, help centre, feedback and public status page (N-13) | 3 | To do | India | MUST | MDV-P5-001 |
| 154 | 2026-12-25 | 2026-12-30 | MDV-P5-047 Service requests: Help in the clinic app, support inbox with SLA, internal notes | 3.5 | To do | India | MUST | MDV-P5-040, MDV-P5-018 |
| 155 | 2026-12-22 | 2026-12-24 | MDV-P5-019 Full clinic data export in FHIR and CSV (no lock-in) (N-14) | 3 | To do | India | MUST | MDV-P5-001 |
| 156 | 2026-12-17 | 2026-12-21 | MDV-P5-025 Retention and deletion jobs per country; patient access/correction/erasure reque | 3 | To do | India | MUST | MDV-P3-019, MDV-P0-024 |
| 157 | 2026-12-22 | 2026-12-24 | MDV-P5-045 Releases and What's new, maintenance and incident banners, integrations register | 2.5 | To do | India | SHOULD | MDV-P5-040, MDV-P5-041 |
| 158 | 2026-12-25 | 2026-12-29 | MDV-P5-049 Data requests and offboarding runs from the console without opening records | 2.5 | To do | India | MUST | MDV-P5-040, MDV-P5-019, MDV-P5-025 |
| 159 | 2026-12-21 | 2026-12-24 | MDV-P5-048 Platform analytics, practice health score and scheduled reports with small-count | 3.5 | To do | India | SHOULD | MDV-P5-040, MDV-P4-029 |

### J6 Mobile apps

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 160 | 2026-12-22 | 2026-12-23 | MDV-P5-031 Mobile ADR and threat model: two Expo store apps (Medvya Clinic, Medvya for pati | 1.5 | To do | India | MUST |  |
| 161 | 2026-12-24 | 2026-12-29 | MDV-P5-032 Mobile foundation: `mobile/` Expo workspace, shared tokens and i18n, RTL, CI and | 4 | To do | India | MUST | MDV-P5-031 |
| 162 | 2026-12-24 | 2026-12-29 | MDV-P5-033 Mobile backend: native token sign-in, device registry, push registry and remote  | 3.5 | To do | India | MUST | MDV-P5-031, MDV-P0-011 |
| 163 | 2026-12-30 | 2027-01-04 | MDV-P5-034 Staff app: sign-in, 15-minute biometric lock, Today, queue, patient search and p | 4 | To do | India | MUST | MDV-P5-032, MDV-P5-033, MDV-P2-001 |
| 164 | 2026-12-30 | 2027-01-05 | MDV-P5-036 Patient app: SMS sign-in, family profiles, home, booking, check-in and reschedul | 5 | To do | India | MUST | MDV-P5-032, MDV-P5-033, MDV-P4-020, MDV-P4-023 |
| 165 | 2026-12-30 | 2026-12-31 | MDV-P5-038 Private push notifications for both apps (IDs only, no health text on the lock s | 2 | To do | India | MUST | MDV-P5-033 |
| 166 | 2027-01-05 | 2027-01-11 | MDV-P5-035 Staff app: consent script, background recording with offline queue, AI note revi | 5 | To do | India | MUST | MDV-P5-034, MDV-P3-001 |
| 167 | 2027-01-06 | 2027-01-12 | MDV-P5-037 Patient app: results, prescriptions, messages, consents, payments and video visi | 5 | To do | India | MUST | MDV-P5-036, MDV-P4-007 |
| 168 | 2027-01-13 | 2027-01-15 | MDV-P5-039 Store release: accounts, privacy labels, MASVS test, accessibility and RTL check | 3 | To do | India | MUST | MDV-P5-035, MDV-P5-037, MDV-P5-038 |

### J7 Launch and scale (India)

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 169 | 2026-12-25 | 2026-12-29 | MDV-P5-002 Migration: read-only source connection via self-hosted integration runtime (Orac | 3 | To do | India | MUST | MDV-P5-001 |
| 170 | 2026-12-30 | 2027-01-04 | MDV-P5-003 Migration mapping: AI-proposed column and code mappings (LOINC, ICD-10, formular | 4 | To do | India | MUST | MDV-P5-002 |
| 171 | 2026-12-30 | 2027-01-01 | MDV-P5-006 Data-quality queue: problems grouped by type and fixed by rule; reversible (N-05 | 3 | To do | India | MUST | MDV-P5-002 |
| 172 | 2027-01-05 | 2027-01-07 | MDV-P5-004 Migration dry run into a test tenant and owner-signed dry-run report (N-04) | 2.5 | To do | India | MUST | MDV-P5-003, MDV-P5-006 |
| 173 | 2027-01-04 | 2027-01-06 | MDV-P5-007 Duplicate scoring incl. Arabic–English name spellings, bulk merge for high confi | 3 | To do | India | MUST | MDV-P5-006, MDV-P1-017 |
| 174 | 2027-01-08 | 2027-01-13 | MDV-P5-005 Day-one load, change-data-capture parallel run and background backfill (N-04) | 4 | To do | India | MUST | MDV-P5-004 |
| 175 | 2026-12-30 | 2027-01-01 | MDV-P5-008 Insurance coverage and live eligibility with co-pay estimate at the desk (P-08) | 3 | To do | India | MUST | MDV-P1-013, MDV-P4-004 |
| 176 | 2026-12-30 | 2027-01-01 | MDV-P5-024 Security operations: incident response with DPDP/UAE breach notification, Defend | 3 | To do | India + UAE | MUST | MDV-P0-024 |
| 177 | 2026-12-31 | 2026-12-31 | MDV-P5-028 Commercial readiness: order form and SLA signed with the first paying clinic | 1 | To do | India | MUST | MDV-P5-016 |
| 178 | 2027-01-04 | 2027-01-07 | MDV-P5-009 Pre-authorisation, claims, resubmission and remittance matching — India TPA/PMJA | 4 | To do | India | MUST | MDV-P5-008, MDV-P2-010 |
| 179 | 2027-01-01 | 2027-01-05 | MDV-P5-014 Patient report upload with AI extraction marked "Unverified · patient upload" un | 3 | To do | India | SHOULD | MDV-P5-001, MDV-P3-007, MDV-P2-006 |
| 180 | 2026-12-25 | 2027-01-25 | MDV-P5-027 Availability monitoring: 30 days at ≥ 99.5 % before GA | 1 | To do | India | MUST | MDV-P5-018 |
| 181 | 2027-01-04 | 2027-01-06 | MDV-P5-013 Regulator e-prescription and e-sick leave; letters and certificates from templat | 3 | To do | India | MUST | MDV-P5-001, MDV-P2-017 |
| 182 | 2027-01-04 | 2027-01-11 | MDV-P5-015 Voice appointment assistant v1 (inbound): book/move/cancel from the live calenda | 6 | To do | India | SHOULD | MDV-P5-001, MDV-P1-022, MDV-P3-007 |
| 183 | 2027-01-04 | 2027-01-05 | MDV-P5-020 External penetration test and remediation — no open high/critical findings | 2 | To do | India | MUST | MDV-P5-017 |
| 184 | 2027-01-14 | 2027-01-15 | MDV-P5-029 First paying clinic cut-over without closing (week 29) with go-live runbook | 2 | To do | India | MUST | MDV-P5-005, MDV-P5-028 |
| 185 | 2027-01-05 | 2027-01-06 | MDV-P5-021 Disaster-recovery drill proving RPO ≤ 15 min and RTO ≤ 4 h | 1.5 | To do | India | MUST | MDV-P0-019 |
| 186 | 2027-01-05 | 2027-01-06 | MDV-P5-022 Accessibility audit and load test before GA | 2 | To do | India | MUST | MDV-P5-018 |
| 187 | 2027-01-06 | 2027-01-07 | MDV-P5-023 Dark mode and high-contrast themes (X-02) | 2 | To do | India | MUST | MDV-P0-020 |
| 188 | 2027-01-06 | 2027-01-06 | MDV-P5-026 Support model: first response under 4 business hours, on-call rota and runbooks | 1 | To do | India | MUST | MDV-P5-018 |
| 189 | 2027-01-26 | 2027-01-26 | MDV-P5-030 v1.0 general availability gate and release notes | 1 | To do | India | MUST | MDV-P5-020, MDV-P5-027, MDV-P5-029 |

### J8 Later - advanced and hospital

| Seq | Start | Target | Ticket | Days | Status | Market | Priority | Depends on |
|---|---|---|---|---|---|---|---|---|
| 190 | 2027-01-06 | 2027-01-07 | MDV-P1-014 Patient registration — UAE flow (Emirates ID manual entry, Arabic name RTL, emir | 2.5 | Partly done | UAE later | MUST | MDV-P1-013 |
| 191 | 2027-01-07 | 2027-01-07 | MDV-P1-046 Start UAE regulator onboarding applications early (risk mitigation) | 0.5 | To do | UAE later | SHOULD | MDV-P0-003 |
| 192 | 2027-01-07 | 2027-01-08 | MDV-P4-008 UAE payments (Network International or Stripe) and card-on-terminal recording (P | 2 | To do | UAE later | MUST | MDV-P4-007 |
| 193 | 2027-01-08 | 2027-01-12 | MDV-P5-011 Emirates ID card-reader check-in under 30 seconds and UAE PASS (N-06) | 3 | To do | UAE later | MUST | MDV-P5-001, MDV-P1-014 |
| 194 | 2027-01-13 | 2027-01-19 | MDV-P5-012 Health-exchange pull and push for the launch emirate (Malaffi / NABIDH / Riayati | 5 | To do | UAE later | MUST | MDV-P5-011, MDV-P1-046 |
| 195 | 2027-01-08 | 2027-01-13 | MDV-P5-010 UAE claims via eClaimLink (Dubai) / Shafafiya (Abu Dhabi) for the launch emirate | 4 | To do | UAE later | MUST | MDV-P5-009, MDV-P1-046 |
| 196 | 2027-01-07 | 2027-01-08 | MDV-P6-001 Hardware partner, device certification and regulated-device classification advic | 2 | To do | India | MUST |  |
| 197 | 2027-01-11 | 2027-01-14 | MDV-P6-002 Clip pairing with code match and microphone self-test (W-01) | 4 | To do | India | MUST | MDV-P6-001 |
| 198 | 2027-01-07 | 2027-01-12 | MDV-P6-007 Multilingual notes: Arabic, English, Hindi and Malayalam incl. mixed-language co | 4 | To do | India | MUST | MDV-P3-018 |
| 199 | 2027-01-15 | 2027-01-20 | MDV-P6-003 Clip capture bound to encounter and consent with visible recording light (captur | 4 | To do | India | MUST | MDV-P6-002, MDV-P3-006 |
| 200 | 2027-01-13 | 2027-01-15 | MDV-P6-008 Stronger AI QA: per-language evaluation sets, clinician trust metrics, red-team  | 3 | To do | India | MUST | MDV-P6-007, MDV-P5-014 |
| 201 | 2027-01-07 | 2027-01-11 | MDV-P7-001 Hospital procurement, HIS vendor engagement and integration contract | 3 | To do | India | MUST |  |
| 202 | 2027-01-21 | 2027-01-26 | MDV-P6-004 Device health, battery, encrypted offline buffer, upload conflicts and staged fi | 4 | To do | India | MUST | MDV-P6-003 |
| 203 | 2027-01-21 | 2027-01-26 | MDV-P6-006 Private voice assistant through earpiece only (A-07) | 4 | To do | India | SHOULD | MDV-P6-003 |
| 204 | 2027-01-27 | 2027-01-28 | MDV-P6-005 Loss/theft revoke effective in under one minute (W-03) | 2 | To do | India | MUST | MDV-P6-004 |
| 205 | 2027-01-12 | 2027-01-14 | MDV-P6-009 Outbound voice reminder and recall calls; waitlist fill from cancellations | 3 | To do | India | SHOULD | MDV-P5-015 |
| 206 | 2027-01-12 | 2027-01-14 | MDV-P7-002 Hospital onboarding wizard and record authority per facility (H-08) | 3 | To do | India | MUST | MDV-P7-001 |
| 207 | 2027-01-15 | 2027-01-18 | MDV-P7-003 Organisation, facilities and departments administration (H-02) | 2 | To do | India | MUST | MDV-P7-002 |
| 208 | 2027-01-29 | 2027-02-01 | MDV-P6-010 Device kit, quick-start guide, fleet guide and R6 gate | 2 | To do | India | MUST | MDV-P6-004, MDV-P6-005, MDV-P6-008 |
| 209 | 2027-01-19 | 2027-01-22 | MDV-P7-004 SSO (SAML/OIDC via Entra federation), SCIM provisioning and facility/department  | 4 | To do | India | MUST | MDV-P7-003 |
| 210 | 2027-01-12 | 2027-01-14 | MDV-P7-005 Hospital integration console: connection tiles for HIS, LIS, ABDM/HIE, SSO, mess | 3 | To do | India | MUST | MDV-P7-001 |
| 211 | 2027-01-15 | 2027-01-26 | MDV-P7-006 FHIR R4 HIS interface and write-back queue with conflict review and retry (H-04) | 8 | To do | India | MUST | MDV-P7-005, MDV-P7-002 |
| 212 | 2027-01-15 | 2027-01-21 | MDV-P7-007 Lab feeds (HL7 v2 ORU/ORM over MLLP/VPN) and imaging viewer links (N-15) | 5 | To do | India | MUST | MDV-P7-005, MDV-P4-014 |
| 213 | 2027-01-08 | 2027-01-12 | MDV-P7-008 Audit log and break-glass review (H-05) | 3 | To do | India | MUST | MDV-P0-017 |
| 214 | 2027-01-12 | 2027-01-15 | MDV-P7-009 Retention and residency policies (H-06) and data subject requests with tamper-ev | 4 | To do | India | MUST | MDV-P5-025 |
| 215 | 2027-01-12 | 2027-01-19 | MDV-P7-011 Dedicated hospital stamp: own compute, database, Key Vault (CMK/HSM), storage, p | 6 | To do | India | MUST | MDV-P7-001 |
| 216 | 2027-01-13 | 2027-01-13 | MDV-P8-001 Feature intake: adoption target, evidence review and regulatory classification c | 1 | To do | India | MUST |  |
| 217 | 2027-01-14 | 2027-01-21 | MDV-P8-002 Teleconsultation: video visit with identity check and ambient notes (A-11) | 6 | To do | India | SHOULD | MDV-P8-001, MDV-P3-012 |
| 218 | 2027-01-13 | 2027-01-15 | MDV-P7-010 Full enterprise analytics (throughput, open loops, adoption) with authorised dri | 3 | To do | India | MUST | MDV-P4-030 |
| 219 | 2027-01-15 | 2027-01-20 | MDV-P8-003 Vaccinations and recalls: child, travel and adult schedules with reminders (E-08 | 4 | To do | India | COULD | MDV-P8-001 |
| 220 | 2027-01-15 | 2027-01-18 | MDV-P7-012 SIEM export and privileged access review | 2 | To do | India | MUST | MDV-P5-024 |
| 221 | 2027-01-18 | 2027-01-25 | MDV-P8-004 India ABDM: ABHA consent requests and linked records from other facilities | 6 | To do | India | SHOULD | MDV-P8-001 |
| 222 | 2027-01-20 | 2027-01-26 | MDV-P8-005 Remaining UAE health exchanges (the two not used at launch) (N-07) | 5 | To do | UAE later | SHOULD | MDV-P5-012 |
| 223 | 2027-01-27 | 2027-01-28 | MDV-P7-013 R7 hospital pilot, clinical sign-off and release | 2 | To do | India | MUST | MDV-P7-006, MDV-P7-008, MDV-P7-012 |
| 224 | 2027-01-18 | 2027-01-25 | MDV-P8-006 Front-desk extras: kiosk/QR self check-in, clinic letter inbox, offline read and | 6 | To do | India | COULD | MDV-P8-001 |
| 225 | 2027-01-19 | 2027-01-25 | MDV-P8-007 Platform: public FHIR APIs, saved migration templates per vendor, specialty temp | 5 | To do | India | COULD | MDV-P8-001, MDV-P5-019 |
