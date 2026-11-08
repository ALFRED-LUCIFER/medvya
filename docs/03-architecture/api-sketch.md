# API sketch and rules

Minimal APIs grouped per module, with OpenAPI generated from code and a TS client generated into `packages/api-client`. Staff calls go through the BFF (same origin, cookie + antiforgery). Portal calls use the `/p/v1` prefix and a patient session.

## Rules (Software Architecture §12, PRD v2 X1)

- Every endpoint runs central authentication, authorisation and audit middleware, **plus** resource-specific authorisation inside the use case (tenant, facility, role, relationship, purpose).
- No GET has side effects (except the audit write of the read itself).
- Public URLs never contain storage names or clinical text. No PHI in query strings: searches by name, phone or ID are `POST` with the text in the body.
- Idempotency keys on all POSTs that create money, messages, orders or integration deliveries.
- FluentValidation on every input. Problem Details (RFC 9457) for errors. 403 maps to the F-05b screen.
- Rate limiting on sign-in, OTP, booking, search and portal endpoints.

## MVP endpoints (P0–P3)

```text
GET    /v1/me/context                              # user, memberships, active facility, permissions, flags
POST   /v1/invitations                             # P0/P1
POST   /v1/invitations/{token}/accept
POST   /v1/sessions/{id}/revoke
GET    /v1/admin/audit?scope=authorized            # P0/P1 (F-12)
POST   /v1/patients                                # P1 (P-02)
POST   /v1/patients/search                         # privacy-safe results (D-02); text in the body, never the URL (P1-018)
POST   /v1/patients/lookup                         # ⌘K results (D-02): initials, age, MRN tail + open/request-access verdict per row (P2-003)
GET    /v1/patients/{id}                           # open a record (audited read, purpose registration); national/insurance numbers masked to the last 4
POST   /v1/patients/{id}/identifiers/reveal        # full numbers on request (Register / book at Full), audited, rate-limited
POST   /v1/patients/duplicate-check                # while typing (P1-05)
POST   /v1/patients/{id}/completion                # add address, payer and/or emergency contact to a quick registration (P1-015, P1-020); active facility's patients only, else 404
POST   /v1/patients/merge-requests                 # P-03
GET    /v1/schedule?facilityId=&date=                # day of every doctor and resource (P-04 day and list views); the client filters a column
GET    /v1/schedule/week?facilityId=&calendarId=&date= # Monday–Sunday of one doctor or resource, each day as in the day view plus capacity (P1-023)
GET|POST /v1/schedule/exceptions  # P1-012: leave (from–to), extra session or shortened day (one date + window); affected booked visits need an action each (offer|manual) or 409 actions_required + the list, nothing saved
PUT|DELETE /v1/schedule/exceptions/{id}  # P1-012: edit (same body and action rules) / cancel; deleted leave gives the slots back, visits already "needs rebooking" are never re-booked; an extra session with booked visits cannot be deleted (400 id:has_bookings). Leave stays capped at 60 days per save: each doctor-day is locked while visits move, so longer leave is saved in parts (lift with a set-based move)
POST   /v1/slot-holds                              # short hold before confirm (P1-07)
POST   /v1/appointments                            # serializable + app lock
POST   /v1/appointments/walk-in                    # P1-025: next free slot today on the booking path, else queued unscheduled (no slot); then check-in
POST   /v1/appointments/{id}/reschedule|cancel|no-show   # P1-024: booking path (doctor-day locks, slot rows freed and taken); cancel body = reason + optional note
POST   /v1/appointments/series/preview {facilityId, calendarId, firstStart, count, intervalWeeks, monthly}  # P1-026: occurrences (same facility wall-clock time, every N weeks or on the same date each month) with the conflict code of each, read-only
POST   /v1/appointments/series {…, patientId, starts[]}   # P1-026: each confirmed start booked on the booking path as its own appointment (one SeriesId); a start taken meanwhile is reported, not fatal
POST   /v1/appointments/{id}/cancel-following      # P1-026: this and later Booked/Checked-in visits of the series, one reason; the first visit's event notifies, the rest are quiet
GET    /v1/waitlist?facilityId=                    # P1-027: waiting and offered patients, oldest request first, each waiting one with the earliest free slot that suits it (14 days)
POST   /v1/waitlist {facilityId, patientId, calendarId?, visitType, reasonCategory, days[], from?, until?}   # add; one open entry per patient
POST   /v1/waitlist/{id}/offer {calendarId, start}  # holds that slot (keyed by the entry, 30 min) on the booking path and queues a WaitlistOffer message (clinic, date, time only)
POST   /v1/waitlist/{id}/accept|decline             # accept books the held slot and ends the entry; decline releases it. An offer that runs out frees the slot by itself
DELETE /v1/waitlist/{id}                            # off the list, any hold released
POST   /v1/appointments/{id}/check-in              # P1-028; a repeat returns the visit unchanged
POST   /v1/appointments/{id}/status                # next board column only (state machine), else 409
POST   /v1/appointments/{id}/reassign  {calendarId, reason}   # P1-030: a waiting visit to another doctor today; reason is a code
GET    /v1/front-desk/overview?facilityId=         # P1-030 D-04: doctors (counts per state, delay, next) and the waiting room
GET    /v1/front-desk/board?facilityId=            # P-01 today, nonclinical; SignalR hub for live updates (P1-029)
HUB    /v1/hubs/board  Watch(facilityId) → BoardChanged{appointmentId,state,changedAt}  # P1-029; session's facility only, boards refetch
POST   /v1/queue/tv/session  {token}               # P1-031: pairs a TV (token from the link's #fragment) → HttpOnly display cookie
GET    /v1/queue/tv                                # display cookie only: {now:[{token,room,doctor}], next:[{token,waitMinutes}]}
HUB    /v1/queue/hub  Watch() → QueueChanged()     # display only; the push has no arguments, the TV refetches
GET|POST /v1/queue/displays · DELETE /v1/queue/displays/{id}   # staff with practice configuration: add (token shown once), list, revoke
GET    /v1/notifications
POST   /v1/encounters                              # P2
GET    /v1/patients/{id}/timeline
POST   /v1/encounters/{id}/observations            # N-01
POST   /v1/notes/{id}/sign  · POST /v1/notes/{id}/addenda
POST   /v1/prescriptions/{id}/authorise · GET /v1/prescriptions/{id}/pdf
POST   /v1/encounters/{id}/consents                # P3 recording consent
POST   /v1/encounters/{id}/recordings/start
POST   /v1/recordings/{id}/chunks/authorize · /chunks/commit · /pause · /stop
GET    /v1/encounters/{id}/transcript · /notes/draft
PATCH  /v1/notes/{draftId}
```

Later phases add `/v1/invoices`, `/v1/payments/webhooks/{provider}`, `/v1/inbox`, `/v1/care-tasks`, `/p/v1/*`, `/v1/eligibility-checks`, `/v1/migrations`, `/v1/devices`, `/v1/integrations/*`.
