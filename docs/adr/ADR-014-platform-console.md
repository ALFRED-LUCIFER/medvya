# ADR-014 · Platform console: separate host, company sign-on plus our own WebAuthn key

- **Status:** Proposed 7 Nov 2026 (MDV-P5-040). The founder chose the key model ("our own WebAuthn") on 7 Nov 2026.

## Context

The Medvya team needs a console to run the platform (tenants, licence checks, rollouts, plans, releases and more, MDV-P5-040 to P5-049). It must
never show patient records (ADR-003: the control plane holds no PHI), every action must carry a reason and be logged, and access must be
phishing-resistant: company sign-on, then a security key, with lost-key recovery by a second owner and a one-hour link.

## Decision

- **Separate app and host.** `web/platform-admin` is served by `src/Host.Platform`, a composition-only host that references the `Platform`
  module and shared building blocks, never a clinic module (`PlatformConsoleTests.TheConsoleHostCannotReachAnyClinicModule`). Its database
  login holds only `medvya_platform`, which has rights on schema `platform` alone; the clinic app role (`medvya_app`) is not a member, and the
  console login cannot read any clinic schema (`TheConsoleLoginCannotReadClinicData`). A later console page that needs clinic facts (the licence
  queue, MDV-P5-044; the tenant catalogue, MDV-P5-017) gets its own narrow, reviewed read path, not a wider grant.
- **Sign-in = BFF (ADR-004) + WebAuthn.** Company sign-on runs through the host (OIDC code + PKCE, tokens never reach the browser) and starts a
  server-side session that reaches only the key steps and `/v1/me`. A WebAuthn assertion with user verification marks the session
  key-verified; only then do the console policies (also the default and fallback policy) pass. Keys are device-bound: a backup-eligible
  (synced) credential is refused. Checks use the Fido2 library (`Fido2` 4.3.2, MIT), not hand-written attestation code. Each ceremony's options
  live in the session row and are consumed by its answer.
- **One link type for joining and recovery.** An owner invites a person, or recovers another member's key after a video call, and gets a
  single-use link valid for one hour (only its SHA-256 is stored; the token sits in the URL fragment, then in the protected OIDC state, then the
  session row). Recovery revokes the old keys and sessions at once. Nobody recovers their own key, and the last owner cannot be removed. The
  first owner is configured (`Platform:BootstrapOwner`, the Entra object id) and may register a key without a link only while no key exists.
- **Reason on every action.** Every write under `/v1` sits in an action group whose filter refuses a body without a 5 to 500 character reason
  (`EveryConsoleWriteRequiresAReason`); the service saves the change and its audit entry in one transaction.
- **Append-only platform audit log** (`platform.AuditEntries`): insert and read only for `medvya_platform`, and triggers refuse UPDATE, DELETE
  and TRUNCATE for every role. Exported as CSV with formula cells defused; the export is logged first.
- **Roles** (owner, operations, support, finance, reviewer) come from the live member row on every request, never from the cookie.

## Consequences

- A second app and host to deploy: an Entra app registration (workforce tenant) with its secret in Key Vault, an App Service, a database login
  mapped to `medvya_platform`, and the WebAuthn relying party (`Platform:RpId`, `Platform:Origins`) set to the console's domain. This is
  people-and-Azure work, listed in `docs/05-delivery/session-handoff.md`.
- Not hash-chained like the clinic ledger: the clinic digest job (running as `medvya_app`) must not read console data. Add a chain and digests
  when the control plane moves to its own database (MDV-P5-017).
- The development sign-in provider and the security headers moved from `Host.Api` to `Medvya.BuildingBlocks.Security`, so both hosts use one copy (ADR-004's note names the old path).
- Until it is deployed, the console runs locally only: `pnpm dev:db` creates the `medvya_platform` login, `pnpm dev:platform` starts it on
  http://localhost:5174 with a development sign-in provider standing in for the company directory.

## Note, 10 Nov 2026 (MDV-P1-064): organisation approvals use the reviewed review path (option A)

The product owner had not answered how the console reads the approval queue, so the recommendation was taken and is recorded here. **Host.Platform reads the queue through the narrow cross-tenant read path the interim API already uses** (row-level security `medvya.operator` branch on three tables: `Tenants`, `OrganisationRequests`, `OrganisationDocuments`), and **every decision goes through `IOrganisationState`**, the one service that sets an organisation's state, audits it in the organisation and checks the transition and the hospital DPA rule. There is no second copy of the state or the audit logic.

- **A second login, not a wider one.** The console host gets `ConnectionStrings:Review`, a login in the new role `medvya_review` (`infra/postgres/roles.sql`, `table-rights.sql`): read on the organisation, its request, its documents and the practice profile; update of the state and the request's last reason only (column rights: not the name or anything else); insert of the settings change and the audit event of a decision, and read of the audit chain head the ledger trigger needs (ids, sequence and hash, never the content); read of three columns of `Memberships` that the tenant row's read policy needs. It holds no right on patients, scheduling, encounters, identity, staff memberships, licences or invitations, and no schema-wide or default right. `medvya_platform` (the console's own login) is unchanged and still cannot read any clinic schema.
- **The host loads exactly one slice of two clinic modules.** `Host.Platform` references `Tenancy.Infrastructure` and `Audit.Infrastructure` (and what they reference), no other clinic module, no `Tenancy.Api`. `PlatformConsoleTests.TheConsoleHostCannotReachAnyClinicModule` now allows that named list (`ReviewPath`) and still fails on any patient-data module; a new entry is a reviewed change like a new grant. Integration test `TheReviewLoginCannotReadClinicData` shows the database refuses nine clinic tables to the review login even with the operator and tenant settings set.
- **Roles.** The console policies decide: owner, operations and reviewer may act, every console role may read (support and finance read only), enforced on the server and with the buttons absent.
- **Two audit trails.** The decision is audited in the organisation (clinic audit, the operator's platform member id as actor) and appended to `platform.AuditEntries` with the organisation as `SubjectTenantId`. They are in two databases, so not one transaction (a `shortcut:` in the endpoint); when the control plane moves to its own database (MDV-P5-017) the review path becomes a service call and this login goes away.
- **Documents** are fetched by the console host (the storage link never reaches the browser), shown from memory with no download, and logged in both trails. The watermark is drawn over the file, not burned into it (`shortcut:`).

