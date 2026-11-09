# ADR-016 · Organisation onboarding and approval: Medvya approves organisations, never individual doctors

- **Status:** Decided by the product owner on 10 Nov 2026, including the answers of the same day to the open points (set-up-only mode before approval, starter plan cap, document lists, interim approval queue, owner-attested licence). Counsel still reviews the document lists and the terms wording (MDV-P1-044, MDV-P2-022) before they are used with a real clinic. Supersedes the "Medvya operations verify each doctor's licence" default in MDV-P1-007, MDV-P2-023 and MDV-P5-044.
- **Tickets:** MDV-P1-062 to P1-072 (new), MDV-P2-023 and MDV-P5-044 (rewritten); plans and self-service upgrade later in MDV-P5-016 and MDV-P5-042.

## Context

Medvya today treats the individual doctor as the unit of trust. A "solo doctor" signs up alone (`Role.SoloDoctor`, F-04), submits a licence, and recording and signing stay off until a Medvya operations user verifies that licence (`LicenceRules`, `IStaffAccess.LicencePendingAsync`, `POST /v1/operations/licences/...` behind `OperationsPolicies.LicenceReview` and the `Operations:LicenceReviewers` id list, `LicenceReview.cs`). MDV-P1-007 recorded this as a default decision the founder had not confirmed, and left open who checks which register (NMC and state councils, DoH, DHA, MOHAP), the SLA, and the back-office queue (MDV-P2-023, MDV-P5-044).

That model does not scale for a platform and does not match how clinics work. Medvya cannot responsibly be the registrar of every doctor's licence in two countries, and the owner of a practice already decides who practises there.

## Decision (owner decision, 10 Nov 2026)

1. **Medvya is a platform.** It verifies and approves the **organisation**, never individual doctors.
2. **There are exactly two organisation types: CLINIC and HOSPITAL.** There is **no individual or solo-doctor account.** A doctor who works alone registers a clinic (a practice) and is its owner. The owner-doctor is simply the clinic's owner who is also a doctor.
3. **Onboarding is an onboarding request made at sign-up.** The applicant chooses Clinic or Hospital, supplies the documents that type requires, and Medvya reviews it and approves, asks for changes, or rejects.
4. **What happens inside an approved organisation is the organisation's responsibility:** whom it adds as doctors, nurses and staff, and **attesting each doctor's licence**. Medvya does not verify individual doctors and does not gate signing or prescribing on a Medvya licence check.
5. **Subscriptions have seats** (doctors, staff, patients). When a clinic adds more than its plan allows, the owner upgrades themselves (self-service), and what they do is their responsibility. Real plans and the upgrade flow are built later (MDV-P5-042, MDV-P5-016); until then one hard-coded starter plan is enforced (see "Starter plan" below).

## Organisation types and required documents (DECIDED 10 Nov 2026; counsel confirms per country)

One build serves both types: Hospital is the Clinic flow plus extra documents. Field formats reuse `OnboardingRules` (India council list and registration number, UAE DoH/DHA/MOHAP licence, GSTIN optional format, TRN 15 digits). Counsel (MDV-P1-044, MDV-P2-022) confirms the lists per country before they are used with a real clinic.

| Document or item | Clinic | Hospital |
|---|---|---|
| Organisation legal name, address, contact, country (fixes the region, ADR-003) | required | required |
| Clinic registration certificate (India: state clinical establishment registration; UAE: facility licence from DoH, DHA or MOHAP) | required | hospital registration certificate instead |
| Tax registration (India GSTIN, UAE TRN) | required, format-checked | required |
| Owner's government ID | required | the authorised signatory's ID |
| Owner doctor's medical council registration (kept as a document; Medvya does not check it as a doctor check) | required when the owner is a doctor | not asked (the hospital attests its own doctors) |
| Authorised signatory letter | not asked | required |
| IT or security contact | not asked | required |
| Signed data processing agreement (DPA), before approval | accepted through the terms gate (P1-019) | **signed** and uploaded before approval |

Documents are uploaded to private Blob (P0-006) and read only by Medvya operations through the review queue. Hospital connection to a HIS, SSO and multi-facility record authority is **P7** and out of scope; a Hospital request in P1 gets the same clinic features.

## State machine of an onboarding request

`Draft` (autosaved, resumable, existing behaviour) → `Submitted` → `Approved` | `NeedsChanges` | `Rejected`. `NeedsChanges` returns to `Submitted` when the applicant resubmits. `Approved` → `Suspended` (by Medvya operations, with a reason) → `Approved`. `Rejected` is final for that request; the applicant may start a new one. Every transition is audited with the actor and a plain-language reason for `NeedsChanges`, `Rejected` and `Suspended`, and the applicant is emailed the reason (no PHI). The state is a property of the organisation (tenant), checked server-side.

## What an organisation may do before approval (DECIDED: set-up-only mode)

Until `Approved` the owner can sign in and set up the organisation: profile, hours, rooms and locations, and adding staff (within the starter plan). The server refuses patient registration, booking, check-in, notes, prescriptions and signing. Invitations may be sent; invited staff can sign in but see only a "waiting for approval" notice. No patient data can exist in an unapproved organisation, which keeps the review a pure organisation check. The stricter alternative (status page only) was considered and not chosen.

`NeedsChanges` and `Rejected` keep the same set-up-only mode. `Suspended` keeps read access to existing patient records for care already under way and refuses new registration, booking and signing (the dunning rule in MDV-P5-016 applies the same safety principle).

## Starter plan (DECIDED: one hard-coded plan, enforced now)

Until plans exist (MDV-P5-042) every organisation has one hard-coded starter plan: **1 doctor and 2 other staff** (any mix of nurse, receptionist and admin; the owner-doctor is the doctor), counted **per organisation in total across its locations**. **Patients are not capped.** Adding beyond the cap is refused by the server with the message "Your plan allows 1 doctor and 2 staff; contact Medvya to upgrade". The cap lives in one place with a `shortcut:` comment (hard-coded plan, replaced by entitlements when MDV-P5-042 lands). The owner self-service upgrade comes with MDV-P5-042 and MDV-P5-016. Ticket: MDV-P1-069.

## Locations: one organisation, many locations (DECIDED 10 Nov 2026)

- **Chain model.** One organisation has one or more locations (branches or facilities, like a hospital group in Surat, Mumbai, Hyderabad and Bhopal). The organisation is onboarded and approved once; the first location is created at onboarding. In code a location is a facility. Doctors, nurses and staff, and the plan, belong to the **organisation** and may work at one or more of its locations.
- **Separate organisations.** A person may also own several separate organisations. This already works through multiple memberships and the facility picker (P0-012); each organisation is approved separately.
- **Adding a location is the owner's act, with no new Medvya approval.** The owner enters the location (name, address, time zone) and uploads its registration certificate and tax number as a record. That is the owner's responsibility, the same principle as attesting a doctor. The audit log records who added it and when. The location's country must equal the organisation's country (data stays in country, ADR-003).
- **The starter plan counts per organisation**, in total across locations (1 doctor and 2 other staff); a bigger chain needs a bigger plan (MDV-P5-042).
- **Patients are one shared record across the organisation's locations**: one MRN and one chart. Staff at any location may open it under the existing access, care-relationship and audit rules; the access log shows the location where it was read (role and facility at event time are already kept). Row-level security and the EF filter stay on the organisation (tenant), not the facility.
- **Out of scope:** reassigning or walking a patient in between doctors of different locations.
- **Relation to P7.** MDV-P7-003 (organisation, facilities and departments administration) and MDV-P7-002 (record authority per facility) overlap. The clinic-level version is built first (MDV-P1-070 to P1-072); P7-003 later extends it with hospital departments.
- Tickets: MDV-P1-070 (add a location), MDV-P1-071 (staff and doctors across locations), MDV-P1-072 (shared patient record).

## What changes for doctors

- A doctor's licence is a **required record entered and attested by the owner**: number, authority and expiry (the owner-doctor's own, in the wizard; other doctors, in Settings, Doctors, Add doctor). The record stores the attesting owner's identity, time and the values attested (append-only audit). It is printed on prescriptions. Medvya does not check it.
- The Medvya licence gate is **retired**: recording, signing and prescribing no longer depend on `VerifiedAt`. The `Verified`/`Pending`/`Rejected` doctor states, the "Verification pending" notice (`onboarding.licence.notice`, `LicenceNotice`) and the `licencePending` session flag go. The doctor list shows **"Attested by {owner} on {date}"** instead.
- **Expiry reminders stay** (PRD P1-11) and an expired attestation prompts the owner to re-attest. Whether an expired licence blocks signing was not decided; the default is **warn only** (open question).
- Licence data stays validated per country by the existing rules (format, future expiry date).

## Who approves

Medvya operations, in the platform console (ADR-014), through a new **organisation-approval capability** that replaces `Operations:LicenceReviewers` and `OperationsPolicies.LicenceReview`. Roles: operations and reviewer may approve, ask for changes, reject and suspend; support and finance may read. Every action needs a reason (existing rule) and goes to the platform audit log. The approver is a configured Medvya operations user. A simple approval queue is built in the platform console (`Host.Platform`) as part of this work (MDV-P1-064); until the console runs on Azure, a documented API call does the same, and that interim endpoint stays. MDV-P5-044 later extends the queue (documents view, suspension, reviewer roles). The review reads only the control-plane request and documents, never clinic patient data.

## Consequences and risks

- **Clinical safety and liability.** A fake or lapsed doctor licence is now the **organisation's** liability, not a Medvya-checked fact. The Terms of Service and DPA must say so, in plain words, and the owner accepts it when attesting. This must be covered by the legal review (MDV-P1-044, MDV-P2-022) and recorded in the clinical safety case and hazard log (MDV-P2-021).
- **Controlled drugs.** There is no independent Medvya check of a prescriber's licence, including for controlled and scheduled medicines. **The owner accepted this risk on 10 Nov 2026.** It is recorded here once, with no control added.
- The approval review is now the main trust gate. A fraudulent organisation is possible; the documents, the audit trail and suspension are the controls.
- The wizard asks for more at sign-up (type and documents) and a person must review it. The review time is not instant and needs an SLA and a named reviewer (open, MDV-P2-023).
- `Role.SoloDoctor` goes away as a concept; existing owner-doctors stay owners who are doctors.

## Migration of existing tenants

Every tenant that exists when this ships (the dev and pilot tenants, for example "Pilot Walk Clinic") becomes `Approved` in a forward-only migration, with an audit entry naming the migration, and each existing doctor licence is marked attested by the tenant's owner at migration time (the owner re-confirms on next sign-in). Tenants created after the change start at `Draft`.

## Inventory of code that changes

| Area | Where | Change | Size |
|---|---|---|---|
| Onboarding request and sign-up | `Medvya.Tenancy.Api/OnboardingEndpoints.cs`, `Medvya.Identity` onboarding drafts, `PracticeProvisioning.cs`, `web/clinic/src/features/onboarding` (steps, `domain/onboarding.ts`, `LicenceNotice`), public site CTAs (`Host.Api/PublicSite`, F-01) | Type step (Clinic or Hospital), per-type documents, upload to Blob, submit creates the request instead of an active practice, status page | L (5 days) |
| Organisation state and restricted mode | `Medvya.Tenancy.Domain` (new status on the tenant), `IStaffAccess` / `StaffAccess.cs` (replace `LicenceRules.Apply` with an organisation-state check on every capability), a row-level-security-safe read of the state, shell banner | New gate on registration, booking, check-in, notes, signing; mutation-tested | L (4 days) |
| Starter plan cap | `Doctors.cs`, `Invitations.cs`, staff and invitation endpoints in `Medvya.Tenancy` and `Medvya.Identity` (`StaffEndpoints.cs`), web Settings, Staff and Doctors | One hard-coded plan, count doctors and other staff, refuse beyond the cap server-side with the upgrade message | S (2 days) |
| Operations approval | `DoctorEndpoints.cs` (`OperationsPolicies`, `Operations:LicenceReviewers`, licence verify and reject routes), `LicenceReview.cs`, `ILicenceReview`, `Host.Platform` queue page and `Medvya.Platform` read path (ADR-014 says the licence queue gets its own narrow, reviewed read path) | Replace with organisation approve, ask for changes, reject, suspend; interim endpoint, then the console queue (MDV-P5-044) | M (3 days) interim endpoint and simple console queue, M (3 days) later console extension |
| Owner attestation | `Doctors.cs`, `IDoctors.cs`, `Practice.cs` (`DoctorLicence`, `LicenceStatus`, `LicenceRules`), doctor wizard in Settings (step "verification status"), `SessionControl.cs` (`LicencePending`), `web/clinic` session domain (`licencePending`), `App.tsx` notice | Attestation fields and audit, remove the verification states and the gate, show "Attested by", keep expiry reminders | M (3 days) |
| Naming | `Role.SoloDoctor` in `RoleMatrix.cs` (52 files use it, mostly tests and the web `Role` type), `role.SoloDoctor` label already reads "Owner doctor", `StaffPolicies` comments, copy in `packages/i18n` (`onboarding.licence.notice`, `doctors.intro`, `invite.licence`, `inviteForm.licence`, `onboarding.team.doctors`, `staff.doctorNotice`), docs | Rename to an owner-doctor concept or keep the enum value and retire only the wording; a rename touches the JWT role claim and stored memberships, so it needs a migration | M (2.5 days) |
| Locations | `Medvya.Tenancy` facility directory (`FacilityDirectory.cs`), Settings, Locations in `web/clinic`, audit | Owner adds a location with certificate and tax record, same country, audited | M (3 days) |
| Staff and doctors across locations | memberships and facility scope in `Medvya.Tenancy` (`StaffAccess.cs`, `Invitations.cs`), `Medvya.Scheduling` (schedule, board and queue per location), facility picker | A membership can cover several locations of the organisation | M (3 days) |
| Shared patient record | `Medvya.Patients` (`Patient.cs` has `FacilityId`; `PatientRegistry.cs` allocates the MRN per facility via `NextMrnAsync(tenantId, facilityId)`, requires registration at the active facility, and searches and checks duplicates by facility; `PatientDirectory.cs`, `PatientMerges.cs` filter by `FacilityId`; `RegistrationCareRelationship.cs`, `AccessRequestEndpoints.cs`), Scheduling, Encounters, the access log | One MRN sequence and one duplicate scope per organisation, search across locations, facility kept as where the person was registered or seen | L (5 days) |
| Terms | `ITermsAcceptance.cs` / `TermsPolicy` version, legal text (P1-044) | New version stating the organisation's licence responsibility; owners re-accept | S (0.5 day) plus counsel |
| Migration | New forward-only migration, ERD regeneration (`tools/erd/generate.py`) | Existing tenants Approved, licences attested | S (1 day) |
| Tests and journeys | Tenancy integration tests (licence gate tests become approval gate tests), `web/clinic` tests, `tests/e2e` onboarding spec and J1 | Rewrite to the new flow | included above |

## Status of work

- **Done so far:** this ADR and the ticket sources (MDV-P1-062 to P1-069, rewritten MDV-P2-023 and MDV-P5-044, notes on related tickets), journey J1, personas and the handoff note. The location decisions of the second pass are recorded too. **Implemented since:** the organisation state and set-up-only gate (MDV-P1-063, `OrganisationRules`, `IOrganisationState` as the seam MDV-P1-064 calls), owner attestation with the Medvya licence gate and `Operations:LicenceReviewers` endpoint retired (MDV-P1-065), and the migration of existing tenants to Approved with licences attested by the owner (MDV-P1-068). Until the onboarding request and approval queue exist (MDV-P1-062, MDV-P1-064) sign-up still opens a new organisation as Approved (`shortcut:` in `PracticeProvisioning`).
- **Not done:** all implementation: the onboarding request, the organisation state and set-up-only gate, the approval queue and interim endpoint, owner attestation and the printed licence, the starter plan cap, the rename, the terms text and the migration.
- **Open questions:** (1) whether an expired attested licence blocks signing (default: warn only); (2) the approval SLA and the named Medvya reviewer; (3) hospital specifics beyond the four extra documents; (4) counsel review of the document lists and the terms wording; (5) whether a doctor's fees and hours differ per location; (6) whether a location's tax number may differ from the organisation's (assumed yes, kept as a record); (7) whether the plan cap should ever differ per location (decided: no, per organisation).

## Not decided here

Real plans, prices and the self-service upgrade flow (MDV-P5-016, MDV-P5-042); the hospital HIS, SSO and multi-facility model (P7); whether an expired attestation blocks signing.
