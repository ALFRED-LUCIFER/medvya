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
- **Amended 10 Nov 2026 (MDV-P1-064, owner decision): the organisation review path.** The console's organisation approval queue reuses
  Tenancy's request, state and review services (the same ones behind Host.Api's interim API, so the state machine, the DPA rule, the clinic
  audit and the applicant's email exist once). `Host.Platform` may therefore reference `Medvya.Tenancy` and `Medvya.Audit` and no other clinic
  module (`TheConsoleHostReachesNoClinicModuleButTheOrganisationReview`), and registers only the review services (`AddOrganisationReview`,
  no staff, facility or clinical-access service). They run on a **second login, `medvya_review`**, not on `medvya_platform` and not a member
  of `medvya_app`: `SELECT` on the tenant, request, document, profile and settings-history tables, `UPDATE` of the state and last reason only,
  `INSERT` of the settings-history and audit rows, and the few columns row-level security and the audit chain read as the caller
  (`Memberships` ids and status, the audit chain head). No patient, encounter, scheduling, identity, invitation or audit-content right
  (`TheReviewLoginCannotReadClinicData`). Reads use the `medvya.operator` row-level security branch; a decision runs inside the organisation's
  tenant. Roles: operations and reviewer decide, support reads, owner and finance get 403 (least access: the documents include the owner's
  government ID). Each decision is in the clinic audit, naming the console member, and in the platform audit log.
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
