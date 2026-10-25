# State machines

Every clinically meaningful state is explicit, stored and shown with an icon and label. Sources: Software Architecture §3, §8. Foundations §08. Clinician App (board, invoices, inbox). Enterprise Admin.

## Appointment / visit (P1)

```
BOOKED ──check-in──▶ CHECKED_IN ──▶ WITH_NURSE ──▶ READY_FOR_DOCTOR ──▶ IN_CONSULT ──▶ BILLING ──▶ DONE
  │  ╰─reschedule─▶ BOOKED (new slot, history kept)
  ├─cancel(reason)─▶ CANCELLED
  ╰─mark no-show──▶ NO_SHOW ──offer rebook──▶ (new BOOKED)
WALK_IN ──▶ CHECKED_IN
```
- Check-in is only allowed from BOOKED. No-show is only allowed from BOOKED. Cancel needs a reason and is not allowed once Completed.
- Statuses visible to reception are **nonclinical**.
- Call in (READY_FOR_DOCTOR → IN_CONSULT) records the room of the doctor's session from the schedule template (MDV-P1-029).
- Every committed move is pushed to the facility's open boards as ids and state only (SignalR, MDV-P1-029); boards refetch the board.

## Membership (P0/P1)

`INVITED → ACTIVE → SUSPENDED → OFFBOARDED`, plus `EXPIRED` for an unaccepted invite. Offboarding revokes sessions immediately (P1-11). Licence expiry generates reminders.

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

## Result loop (C-01, display states)

`Ordered → Sample collected → Result received → Reviewed by doctor → (Assigned) → Released to patient → Patient contacted → Follow-up completed`. "Message delivered ≠ done" is its own visual state.

## Invoice (P4)

`DRAFT → ISSUED → PENDING (provider) → PAID`. `ISSUED/PENDING → FAILED → (retry) PENDING`. `PAID → REFUNDED` (credit note). `ISSUED → PARTIAL` (balance → DUE). `INSURER` (claim pending, co-pay collected).

## Integration delivery (P7)

`QUEUED → SENT → DELIVERED (receipt)`. `SENT → FAILED → RETRYING → (paused after N) → manual retry`. `SENT → CONFLICT → SENT_TO_CLINICIAN → RESOLVED (keep HIS + addendum | resend)`.

## Merge request (P1/P5)

`PROPOSED → NOT_SAME (closed)` or `PROPOSED → AWAITING_APPROVAL (second approver if both records have clinical data) → MERGED (reversible) → UNMERGED`.

## Failure modes the UI must show (Foundations)

No consent (E-04) · Clip disconnected (W-02) · Low audio (A-06d) · Uncertain speaker (A-10) · Wrong patient (A-09) · AI outage (A-06c) · Record conflict (H-04 / E-07) · Late result (C-04) · Emergency symptom (A-08).
