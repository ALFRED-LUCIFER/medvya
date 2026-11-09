# ADR-016 · Organisation onboarding and approval: Medvya approves organisations, never individual doctors

- **Status:** Decided by the product owner on 10 Nov 2026 (direction); the document lists, the pre-approval mode and the hospital specifics below are **proposed** and need legal review (MDV-P1-044, MDV-P2-022). Supersedes the "Medvya operations verify each doctor's licence" default in MDV-P1-007, MDV-P2-023 and MDV-P5-044.
- **Tickets:** MDV-P1-062 to P1-068 (new), MDV-P2-023 and MDV-P5-044 (rewritten); seats and plans later in MDV-P5-016 and MDV-P5-042.

## Context

Medvya today treats the individual doctor as the unit of trust. A "solo doctor" signs up alone (`Role.SoloDoctor`, F-04), submits a licence, and recording and signing stay off until a Medvya operations user verifies that licence (`LicenceRules`, `IStaffAccess.LicencePendingAsync`, `POST /v1/operations/licences/...` behind `OperationsPolicies.LicenceReview` and the `Operations:LicenceReviewers` id list, `LicenceReview.cs`). MDV-P1-007 recorded this as a default decision the founder had not confirmed, and left open who checks which register (NMC and state councils, DoH, DHA, MOHAP), the SLA, and the back-office queue (MDV-P2-023, MDV-P5-044).

That model does not scale for a platform and does not match how clinics work. Medvya cannot responsibly be the registrar of every doctor's licence in two countries, and the owner of a practice already decides who practises there.

## Decision (owner decision, 10 Nov 2026)

1. **Medvya is a platform.** It verifies and approves the **organisation**, never individual doctors.
2. **There are exactly two organisation types: CLINIC and HOSPITAL.** There is **no individual or solo-doctor account.** A doctor who works alone registers a clinic (a practice) and is its owner. The owner-doctor is simply the clinic's owner who is also a doctor.
3. **Onboarding is an onboarding request made at sign-up.** The applicant chooses Clinic or Hospital, supplies the documents that type requires, and Medvya reviews it and approves, asks for changes, or rejects.
4. **What happens inside an approved organisation is the organisation's responsibility:** whom it adds as doctors, nurses and staff, and **attesting each doctor's licence**. Medvya does not verify individual doctors and does not gate signing or prescribing on a Medvya licence check.
5. **Later, subscriptions have seats** (doctors, staff, patients). When a clinic adds more than its plan allows, the owner upgrades themselves (self-service), and what they do is their responsibility. This is direction only; plans and seats are built later (MDV-P5-042, MDV-P5-016).

## Organisation types and required documents (PROPOSED, for legal review)

Built from what the sign-up wizard and Settings already collect (`OnboardingRules`: India council list and registration number, UAE DoH/DHA/MOHAP licence, GSTIN optional format, TRN 15 digits; practice profile registration numbers). Counsel (MDV-P1-044, MDV-P2-022) confirms the list per country before it is enforced.

| Item | Clinic | Hospital |
|---|---|---|
| Organisation legal name, address, contact, country (fixes the region, ADR-003) | required | required |
| Clinic or facility registration number and its validity date (India: state clinical establishment registration; UAE: facility licence from DoH, DHA or MOHAP) | required | required (hospital registration) |
| Tax number (India GSTIN, UAE TRN) | required where the organisation is registered; format-checked | required |
| Owner identity: name, work email and phone through MFA sign-in; no national ID copy is stored unless counsel requires it | required | authorised signatory instead of "owner" |
| Owner doctor's medical council or authority, registration number and expiry (only when the owner practises) | required if the owner is a doctor | not asked (the hospital attests its own doctors) |
| Authorised signatory: name, designation, a letter of authority | not asked | required |
| IT contact (security and integration contact) | not asked | required |
| Acceptance of the Terms of Service and DPA by the owner or signatory (existing terms gate, P1-019) | required | required |

Documents are uploaded to private Blob (P0-006) and read only by Medvya operations in the console. Hospital connection to a HIS, SSO and multi-facility record authority is **P7** and out of scope here; a Hospital request in P1 gets the same clinic features with the extra documents above.

## State machine of an onboarding request

`Draft` (autosaved, resumable, existing behaviour) → `Submitted` → `Approved` | `NeedsChanges` | `Rejected`. `NeedsChanges` returns to `Submitted` when the applicant resubmits. `Approved` → `Suspended` (by Medvya operations, with a reason) → `Approved`. `Rejected` is final for that request; the applicant may start a new one. Every transition is audited with the actor and a plain-language reason for `NeedsChanges`, `Rejected` and `Suspended`, and the applicant is emailed the reason (no PHI). The state is a property of the organisation (tenant), checked server-side.

## What an organisation may do before approval (PROPOSED)

**Proposal:** after `Submitted` the owner can sign in and set up the organisation (profile, hours, locations, staff invitations, doctors with their attestation) in a **restricted mode**, so approval turns on a clinic that is ready. Until `Approved` the server refuses patient registration, booking, check-in, notes, prescriptions and signing, and invitations stay pending; staff can sign in but see only a "waiting for approval" notice. No patient data can exist in an unapproved organisation, which keeps the review a pure organisation check.

**Alternative (not chosen unless legal prefers it):** nothing but the request status page until `Approved`. Simpler and stricter, but the owner cannot prepare, and a day of review delays the first booking.

`NeedsChanges` and `Rejected` keep the same restricted mode. `Suspended` keeps read access to existing patient records for care already under way and refuses new registration, booking and signing (the dunning rule in MDV-P5-016 applies the same safety principle).

## What changes for doctors

- When the owner adds a doctor (Settings, Doctors, and the owner's own entry in the sign-up wizard) they enter the licence authority, number and expiry and **attest** that they have checked it. The record stores the attesting owner's identity, time and the values attested (audit, append-only).
- The Medvya licence gate is **retired**: recording, signing and prescribing no longer depend on `VerifiedAt`. The `Verified`/`Pending`/`Rejected` doctor states, the "Verification pending" notice (`onboarding.licence.notice`, `LicenceNotice`) and the `licencePending` session flag go. The doctor list shows **"Attested by {owner} on {date}"** instead.
- **Expiry reminders stay** (PRD P1-11) and an expired attestation prompts the owner to re-attest; whether an expired licence blocks signing is a decision for the owner to take at implementation (default: warn the owner and the doctor, do not block).
- Licence data stays validated per country by the existing rules (format, future expiry date).

## Who approves

Medvya operations, in the platform console (ADR-014), through a new **organisation-approval capability** that replaces `Operations:LicenceReviewers` and `OperationsPolicies.LicenceReview`. Roles: operations and reviewer may approve, ask for changes, reject and suspend; support and finance may read. Every action needs a reason (existing rule) and goes to the platform audit log. Until the console exists (MDV-P5-040, P5-044, weeks 26 to 29), MDV-P1-064 delivers a minimal operations endpoint and the same capability so the pilot is not blocked. The review reads only the control-plane request and documents, never clinic patient data.

## Consequences and risks

- **Clinical safety and liability.** A fake or lapsed doctor licence is now the **organisation's** liability, not a Medvya-checked fact. The Terms of Service and DPA must say so, in plain words, and the owner accepts it when attesting. This must be covered by the legal review (MDV-P1-044, MDV-P2-022) and recorded in the clinical safety case and hazard log (MDV-P2-021).
- **Controlled drugs.** There is no independent Medvya check of a prescriber's licence, including for controlled and scheduled medicines. **The owner accepted this risk on 10 Nov 2026.** It is recorded here once; whether any control is wanted (for example, the formulary's regulatory class from ADR-015 requiring a second attestation) is an open question for the owner and counsel, not decided here.
- The approval review is now the main trust gate. A fraudulent organisation is possible; the documents, the audit trail and suspension are the controls.
- The wizard asks for more at sign-up (type and documents) and a person must review it. The review time is not instant and needs an SLA (to be set in MDV-P2-023).
- `Role.SoloDoctor` goes away as a concept; existing owner-doctors stay owners who are doctors.

## Migration of existing tenants

Every tenant that exists when this ships (the dev and pilot tenants, for example "Pilot Walk Clinic") becomes `Approved` in a forward-only migration, with an audit entry naming the migration, and each existing doctor licence is marked attested by the tenant's owner at migration time (the owner re-confirms on next sign-in). Tenants created after the change start at `Draft`.

## Inventory of code that changes

| Area | Where | Change | Size |
|---|---|---|---|
| Onboarding request and sign-up | `Medvya.Tenancy.Api/OnboardingEndpoints.cs`, `Medvya.Identity` onboarding drafts, `PracticeProvisioning.cs`, `web/clinic/src/features/onboarding` (steps, `domain/onboarding.ts`, `LicenceNotice`), public site CTAs (`Host.Api/PublicSite`, F-01) | Type step (Clinic or Hospital), per-type documents, upload to Blob, submit creates the request instead of an active practice, status page | L (5 days) |
| Organisation state and restricted mode | `Medvya.Tenancy.Domain` (new status on the tenant), `IStaffAccess` / `StaffAccess.cs` (replace `LicenceRules.Apply` with an organisation-state check on every capability), a row-level-security-safe read of the state, shell banner | New gate on registration, booking, check-in, notes, signing; mutation-tested | L (4 days) |
| Operations approval | `DoctorEndpoints.cs` (`OperationsPolicies`, `Operations:LicenceReviewers`, licence verify and reject routes), `LicenceReview.cs`, `ILicenceReview`, `Host.Platform` and `Medvya.Platform` read path (ADR-014 says the licence queue gets its own narrow, reviewed read path) | Replace with organisation approve, ask for changes, reject, suspend; interim endpoint, then the console queue (MDV-P5-044) | M (3 days) interim, M (3 days) console |
| Owner attestation | `Doctors.cs`, `IDoctors.cs`, `Practice.cs` (`DoctorLicence`, `LicenceStatus`, `LicenceRules`), doctor wizard in Settings (step "verification status"), `SessionControl.cs` (`LicencePending`), `web/clinic` session domain (`licencePending`), `App.tsx` notice | Attestation fields and audit, remove the verification states and the gate, show "Attested by", keep expiry reminders | M (3 days) |
| Naming | `Role.SoloDoctor` in `RoleMatrix.cs` (52 files use it, mostly tests and the web `Role` type), `role.SoloDoctor` label already reads "Owner doctor", `StaffPolicies` comments, copy in `packages/i18n` (`onboarding.licence.notice`, `doctors.intro`, `invite.licence`, `inviteForm.licence`, `onboarding.team.doctors`, `staff.doctorNotice`), docs | Rename to an owner-doctor concept or keep the enum value and retire only the wording; a rename touches the JWT role claim and stored memberships, so it needs a migration | M (2.5 days) |
| Terms | `ITermsAcceptance.cs` / `TermsPolicy` version, legal text (P1-044) | New version stating the organisation's licence responsibility; owners re-accept | S (0.5 day) plus counsel |
| Migration | New forward-only migration, ERD regeneration (`tools/erd/generate.py`) | Existing tenants Approved, licences attested | S (1 day) |
| Tests and journeys | Tenancy integration tests (licence gate tests become approval gate tests), `web/clinic` tests, `tests/e2e` onboarding spec and J1 | Rewrite to the new flow | included above |

## Not decided here

Seats, plan limits and the upgrade flow (MDV-P5-016, MDV-P5-042); the hospital HIS, SSO and multi-facility model (P7); whether an expired attestation blocks signing; any extra control for controlled drugs.
