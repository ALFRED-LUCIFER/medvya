# Runbook: approving an organisation (interim API)

Medvya approves **organisations**, never individual doctors (ADR-016). Until the platform console runs on Azure, Medvya operations review and decide through the API of `Host.Api` described here. This interim API stays after the console arrives (MDV-P1-064).

## Who may call it

Two lists of Medvya staff user ids in configuration (Key Vault on Azure, user secrets locally). A practice role never grants either.

| Setting | May | Console role it stands in for |
|---|---|---|
| `Operations:OrganisationApprovers` | list, open documents, approve, ask for changes, reject, suspend | operations, reviewer |
| `Operations:OrganisationReaders` | list and open documents only | support, finance |

Each value is a JSON array of ids, for example `Operations__OrganisationApprovers__0=<user id>`. The id is the operator's Medvya user id (the `NameIdentifier` of their session). The operator signs in to the clinic app's BFF like any staff member, so they need a Medvya account: invite them to any organisation once, or create the account the way the owner's sign-up does. Anyone not listed gets 403; the check reads the configuration on every request.

shortcut: ids in configuration stand in for the console's operations and reviewer roles; the console queue (MDV-P5-044) calls the same service and reads the platform member roles instead.

## Endpoints

All under `/v1/operations/organisations`, all need a signed-in session and, for writes, the header `X-Medvya-Csrf: 1`.

| Call | Does |
|---|---|
| `GET /?filter=Waiting` | The queue, oldest submission first: organisation id, name, country, type, state, submitted time, last reason, hospital IT contact, document list (id, kind, type, size). `filter` is the console's: `Waiting` (Submitted), `NeedsChanges`, `Decided` (Approved, Rejected, Suspended) or `All` (the default). No patient or staff data is part of it. |
| `GET /{tenantId}` | The detail for one organisation: profile (name, address, contact email, registration and tax number, hospital IT contact), documents and the history of decisions with the operator's name and reason. |
| `GET /{tenantId}/documents/{documentId}` | `{ "url", "expiresAt" }`: a read-only link to one document that expires in 5 minutes. Opening a document is audited in the organisation (this is the log behind the design's "every view is logged") (`organisation.document.view`, naming the operator and the document). |
| `POST /{tenantId}/approve` | Submitted to Approved. A **Hospital without its signed DPA on file cannot be approved** (400 `document.Dpa:required`): ask for changes instead. |
| `POST /{tenantId}/request-changes` | Submitted to NeedsChanges. The owner edits and sends the request again. |
| `POST /{tenantId}/reject` | Submitted to Rejected. Final for that request; the applicant may start a new one. |
| `POST /{tenantId}/reinstate` | Suspended back to Approved, with a reason (400 `state:transition` for any other state). |
| `POST /{tenantId}/suspend` | Approved to Suspended. Existing records stay readable for care under way; new registration, booking and signing are refused. |

Every `POST` takes `{ "reason": "..." }` of **5 to 500 characters**, in plain words and never personal details (400 with `reason:required`, `reason:too_short` or `reason:too_long` otherwise). A move the state machine does not allow answers 400 with `state:transition`; an unknown organisation id answers 404.

Example, with a session cookie from signing in as the operator:

```bash
curl -X POST "https://<api host>/v1/operations/organisations/<tenant id>/approve" \
  -H "Cookie: <session cookie>" -H "X-Medvya-Csrf: 1" -H "Content-Type: application/json" \
  -d '{"reason":"Registration certificate and GSTIN checked"}'
```

## What a decision does

1. Changes the organisation's state in one transaction with its audit rows: the clinic audit event `organisation.state.<State>` and a settings change row with the actor (the operator), the before and after state and the reason.
2. Clears or sets the reason the owner sees on the status page (`NeedsChanges`, `Rejected` and `Suspended` keep it).
3. Emails the practice contact address (the one given at sign-up) the outcome and the reason, with no patient data. The email is sent once after the change is saved; if the mail server is down the decision stands, the failure is logged by type only, and the owner reads the reason on the status page. shortcut: no retry until a durable outbox exists.

## What the review reads

Only the control-plane request: the organisation's name, country, type, state, hospital IT contact and the documents the owner uploaded to private Blob. The queue read widens row-level security on three tables (`Tenants`, `OrganisationRequests`, `OrganisationDocuments`) for a connection that sets `medvya.operator` and no tenant; every other table still shows that connection nothing. A decision runs inside the organisation's tenant scope.

## Review checklist (the owner's request, ADR-016)

- Clinic: registration certificate, tax registration (GSTIN or TRN), the owner's government ID and, when the owner is a doctor, the medical council registration (kept as a document; Medvya does not check it as a doctor check).
- Hospital: hospital registration certificate, tax registration, the authorised signatory's ID, the signatory letter, the signed data processing agreement and an IT or security contact.
- Counsel confirms these lists per country before they are used with a real clinic (MDV-P1-044, MDV-P2-022).

shortcut: uploaded files are checked by file signature (PDF, PNG, JPEG) and size (5 MB) but not malware-scanned; add the Defender scan before the first real applicant uploads.

## Not built yet (console design of 10 Nov 2026)

The queue and detail pages of the platform console (filters, profile grid, the private watermarked document viewer with no download, the automatic document checks such as "name and address match", the email preview after a decision) are not built: ADR-014 keeps the console login away from clinic schemas, so the console first needs a reviewed path to this data. The API above has what those pages need (filters, detail with history, logged document links, reinstate); the viewer, the checks and the pages belong with the console work (MDV-P5-044).
