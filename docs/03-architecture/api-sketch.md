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
GET    /v1/patients/{id}                           # open a record (audited read, purpose registration)
POST   /v1/patients/duplicate-check                # while typing (P1-05)
POST   /v1/patients/merge-requests                 # P-03
GET    /v1/schedule?view=day|week|list&date=&doctor=
POST   /v1/slot-holds                              # short hold before confirm (P1-07)
POST   /v1/appointments                            # serializable + app lock
POST   /v1/appointments/{id}/reschedule|cancel|no-show|check-in
GET    /v1/board?date=                             # SignalR hub /hubs/board for live updates
GET    /v1/queue/tv?display_token=                 # tokens only
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
