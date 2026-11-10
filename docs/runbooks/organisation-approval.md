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

## The platform console (MDV-P1-064, ADR-014 note)

The console (`Host.Platform`, `web/platform-admin`, page **Approvals**) does the same job with its own sign-on: company sign-on, then the security key. It is not on Azure yet; locally `pnpm dev:platform`.

| Console role | Approvals |
|---|---|
| Owner, Operations, Reviewer | read, and approve, ask for changes, reject, suspend, reinstate |
| Support, Finance | read only: the buttons are absent and the server answers 403 |

- **Queue:** filters Waiting, Needs changes, Decided, All, oldest first. **Detail:** profile, tax number format check, documents with a file check, history with the operator, decision.
- **Documents** open in a private viewer: shown from memory, no download button or menu, the operator's name and time drawn over the file. The console host fetches the file, so no storage link reaches the browser. Every view is audited in the organisation and in the platform audit log.
- **A Hospital without its signed DPA cannot be approved:** the Approve button is off and the screen offers Ask for changes; the server refuses too (`document.Dpa:required`).
- **Reason** (5 to 500 characters) on every action; the box starts with suggested wording you can edit. After the decision the screen shows the email the applicant was sent (no patient data).
- **Two logs per action:** the organisation's own audit (clinic audit and settings history, the operator's console member id as actor) and the platform audit log (`organisation.approve`, `organisation.request-changes`, `organisation.reject`, `organisation.suspend`, `organisation.reinstate`, `organisation.document.view`, with the organisation). The console is the Audit log page's source for the second.
- **Configuration:** `ConnectionStrings:Review` (a database login in the role `medvya_review`, created like the console's own: `db-migrate admin ... medvya_review=<password>`), plus the storage and email settings the clinic host uses (`Storage:BlobEndpoint` or `Storage:ConnectionString`, `Email:*`). Without `ConnectionStrings:Review` the approvals endpoints fail closed.
- **What the review login can read:** the request, its documents and the practice profile of the organisation named, nothing clinic-side (`TheReviewLoginCannotReadClinicData`).

The interim API above stays: it is what to use when the console is not available.

shortcut: the automatic document checks are the file type and size and the tax number's format; "name and address match" needs reading the document (OCR), not built.
shortcut: the watermark is drawn on screen over the document, not burned into the file; burn it in when anyone outside the reviewer role may open documents.
shortcut: the state change and the platform audit entry are in two databases and not one transaction; the organisation's own audit records the decision first.
shortcut: the decision email is English only until templates are localised (MDV-P1-032); the preview shows exactly that text.
