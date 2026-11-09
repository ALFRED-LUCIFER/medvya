# State machines

Every clinically meaningful state is explicit, stored and shown with an icon and label. Sources: Software Architecture §3, §8. Foundations §08. Clinician App (board, invoices, inbox). Enterprise Admin.

## Appointment / visit (P1)

```
BOOKED ──check-in──▶ CHECKED_IN ──▶ WITH_NURSE ──▶ READY_FOR_DOCTOR ──▶ IN_CONSULT ──▶ BILLING ──▶ DONE
  │  ╰─reschedule─▶ BOOKED (new slot, history kept)
  ├─cancel(reason)─▶ CANCELLED
  ├─mark no-show──▶ NO_SHOW ──offer rebook──▶ (new BOOKED)
  ╰─leave / shortened day─▶ NEEDS_REBOOKING ──reschedule──▶ BOOKED   (or cancel)
WALK_IN ──▶ CHECKED_IN
```
- Check-in is only allowed from BOOKED, on the visit's facility-local day.
- No-show is only allowed from BOOKED, once the start time has passed.
- Reschedule is only allowed from BOOKED, to a free slot of a calendar of the same kind. History is kept: the old slot, the new slot, who and when.
- Cancel needs a reason from a short list, and Other needs a note. It is allowed from BOOKED, or from CHECKED_IN when the patient leaves before being seen. From WITH_NURSE on, the visit is finished on the board, and a completed, cancelled or no-show visit cannot be cancelled. This is narrower than the design's "not when Completed" (MDV-P1-024).
- Reschedule, cancel and no-show free the visit's slots in the same transaction, under the doctor-day lock of every day they touch (MDV-P1-022, MDV-P1-024).
- Leave or a shortened day (MDV-P1-012) moves each affected BOOKED visit to NEEDS_REBOOKING, freeing its slots in the same transaction as the leave. Saving needs an action for every affected visit: offer (AppointmentNeedsRebooking.v1 makes the patient's rebooking message, clinic, date and time only) or manual (no message). Reschedule and cancel are allowed from NEEDS_REBOOKING; the visit shows in the board's "needs rebooking" section.
- Statuses visible to reception are **nonclinical**.
- Call in (READY_FOR_DOCTOR → IN_CONSULT) records the room of the doctor's session from the schedule template (MDV-P1-029).
- Every committed move is pushed to the facility's open boards as ids and state only (SignalR, MDV-P1-029); boards refetch the board.
- A waiting visit (CHECKED_IN, WITH_NURSE, READY_FOR_DOCTOR) can move to another doctor's queue the same day without changing state (MDV-P1-030): one of four listed reasons, history kept, its booked slot freed, its token kept.

## Membership (P0/P1)

`INVITED → ACTIVE → SUSPENDED → OFFBOARDED`, plus `EXPIRED` for an unaccepted invite. Offboarding revokes sessions immediately (P1-11). Licence expiry generates reminders.

## Encounter (P2, MDV-P2-001)

`ACTIVE → ENDED`. There is one encounter per visit, led by the doctor who started it, and authority is Medvya (a hospital HIS comes in P7).
- **Start** is allowed from a waiting visit (CHECKED_IN, WITH_NURSE, READY_FOR_DOCTOR) on the visit's facility-local day. It moves the visit to IN_CONSULT with the room of the doctor's session, in the same transaction as the encounter. Starting a visit already IN_CONSULT just creates the encounter. A second start, or one at the same moment, returns the same encounter.
- **One open consultation per doctor.** To start another, the doctor ends the open one or explicitly leaves it open (`leaveOpen`).
- **End** moves the visit IN_CONSULT → BILLING. A visit the desk already moved on stays where it is. An ended encounter is never reopened.
- Start and end are pushed to the facility's open boards after the commit, as ids and state only (as in MDV-P1-029).
- Every call needs clinical access (MDV-P2-024): Patient 360 at Full to start or end, any treating level to read. Leading an encounter is a care relationship under "assigned only".

## Nurse intake (P2, MDV-P2-007)

One intake per visit, from the nurse's first save; the visit must be WITH_NURSE to record, keep, escalate or hand off. Vitals are OBSERVATIONs (source Nurse intake, recorded and verified by the nurse), one row per reading; a re-measure is a new row and the latest of each kind is current.
- A value no living person can have is refused (400, `implausible`). A usual-range miss is saved with a flag (`High`, `Low`, `Changed` for weight) the screen says in words.
- **Handoff** (WITH_NURSE → READY_FOR_DOCTOR) is refused (409 `handoff_blocked`) while a flagged reading is neither re-measured to an unflagged value nor kept on purpose (blocker `vitals_undecided`). It moves the board card in the same transaction and is pushed to the boards after the commit. Repeating it changes nothing.
- With the tenant flag `clinical.nurse-intake` on, the board's generic move WITH_NURSE → READY_FOR_DOCTOR is refused (409 `intake_required`) until the intake was handed off, so the handoff cannot be done around the checks. With the flag off the board moves as before.
- **Allergy gate (MDV-P2-008).** Handoff is also refused while any allergy of the patient has no status (`allergy_status_required`), or the patient has none on record and the nurse has not confirmed "no known allergies" for this visit (`allergies_not_confirmed`). That confirmation is explicit, never a default, is refused while an allergy is on record (`has_allergies`), and counts for one visit. Statuses are Confirmed, Patient reported, Unknown, Not an allergy; a new allergy is added with a status. Each allergy and medicine statement keeps its source (Nurse intake or Patient) and the user who set it. Medicines are patient-reported; a name the nurse could not identify is kept as said and marked Needs clarification.
- **Escalation** needs a reason and creates an open item for the treating doctor (`GET /v1/intake-escalations`), open until the doctor starts the consultation.
- Every call needs clinical access for Vitals and allergies entry at Full (role, facility, care relationship, MDV-P2-024) and is audited with the patient.

## Access request (P2, MDV-P2-024)

`PENDING → GRANTED (1, 7 or 30 days, then expired) | DECLINED`. There is one PENDING request per clinician, patient and facility; asking again returns it. Only an owner of that facility (Staff, roles, integrations: Config) decides, never the requester, and the first decision wins. A grant is a care relationship until it expires. After an expiry or a decline, the clinician asks again.

## Capture FSM (P3, P6)

```
UNPAIRED → PAIRED → IDLE → CONSENT_VERIFIED → CAPTURING ⇄ PAUSED → STOPPED → SYNCING → READY
→ PROCESSING → DRAFT_READY → REVIEWED → SIGNED → [EMR_PENDING → EMR_CONFIRMED | EMR_FAILED]
```
Exceptions:
- `CONSENT_REVOKED` terminates capture. Audio after revocation is discarded and a receipt is recorded.
- `PATIENT_CHANGED` always requires a new encounter and a new consent check.
- `TRANSFER_INTERRUPTED` resumes only the missing sequence-numbered chunks.
- `DEVICE_LOST` revokes trust and keys.

No recording can be attached to two active encounters.

## Note

`DRAFT (AI) → IN_REVIEW → SIGNED v1 (immutable) → ADDENDUM v2 (signed) …`. Field states inside a draft: `ai` → `accepted` | `edited`. Sign is blocked while any field is `ai` or flagged.

## Care task lifecycle (P4)

```
PROPOSED → PHYSICIAN_APPROVED → ASSIGNED → AWAITING_TEST/AWAITING_REPORT → REPORT_RECEIVED
→ AWAITING_CLINICIAN_REVIEW → REVIEWED → PATIENT_CONTACT_PENDING → PATIENT_CONTACTED → FOLLOWUP_BOOKED → CLOSED
```
`BLOCKED`, `OVERDUE`, `CANCELLED` and `UNREACHABLE` each require an explicit reason and an accountable actor. **A sent reminder never closes a task.**

## Investigation order (E-05, MDV-P2-018)

`DRAFT ⇄ NOT_INCLUDED` (the doctor switches a test off or on while the consultation is open), then `DRAFT → ORDERED` only by "Place orders" (one audited call for every included draft; never by signing a note). `ORDERED` is final in P2; results and review are the result loop below (P4). Orders of an ended consultation cannot change.

## Result loop (C-01, display states)

`Ordered → Sample collected → Result received → Reviewed by doctor → (Assigned) → Released to patient → Patient contacted → Follow-up completed`. "Message delivered ≠ done" is its own visual state.

## Invoice (P4)

`DRAFT → ISSUED → PENDING (provider) → PAID`. `ISSUED/PENDING → FAILED → (retry) PENDING`. `PAID → REFUNDED` (credit note). `ISSUED → PARTIAL` (balance → DUE). `INSURER` (claim pending, co-pay collected).

## Integration delivery (P7)

`QUEUED → SENT → DELIVERED (receipt)`. `SENT → FAILED → RETRYING → (paused after N) → manual retry`. `SENT → CONFLICT → SENT_TO_CLINICIAN → RESOLVED (keep HIS + addendum | resend)`.

## Merge request (P1/P5)

`PROPOSED → NOT_SAME (closed)` or `PROPOSED → AWAITING_APPROVAL (second approver if both records have clinical data) → MERGED (reversible) → UNMERGED`.

As built (MDV-P1-017): the older record survives and the other is mapped to it (`MergedIntoPatientId`); nothing is deleted. NOT_SAME suppresses the pair for good. A merge runs only from AWAITING_APPROVAL, after a verified clinician approves (Clinical notes: Sign); when both records hold clinical data a second, different clinician must approve too. Identifiers and visits move with a marker of where they came from, so an admin (Staff, roles, integrations: Config) can undo within the retention window (fixed 90 days until practice settings hold it). Each step is audited in the same transaction; a failing module rolls the whole merge back.

## Failure modes the UI must show (Foundations)

No consent (E-04) · Clip disconnected (W-02) · Low audio (A-06d) · Uncertain speaker (A-10) · Wrong patient (A-09) · AI outage (A-06c) · Record conflict (H-04 / E-07) · Late result (C-04) · Emergency symptom (A-08).
