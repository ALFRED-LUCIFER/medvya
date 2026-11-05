# Session handoff — 5 Nov 2026 (evening)

For the next person or session. No secrets here. Passwords, keys and tokens live in Azure (Key Vault) and the GitHub `dev` environment. **Read first:** `CLAUDE.md`, `AGENTS.md` (use the `ponytail:ponytail` skill at level `full` before any development task), then this file, then `docs/releases/r0.md` and `docs/runbooks/restore.md`.

## 0 · Start here (paste into the new session)

> Read CLAUDE.md, AGENTS.md and docs/05-delivery/session-handoff.md. Then: (1) finish the restore drill in section 3 (the firewall rule `restore-me` is still open and must be removed), (2) start the P1 sequence in section 6 (the label cleanup in section 4 and the ticket-source update in section 5 are done), running independent items as parallel agents in worktrees. GitHub issues are the live record of ticket status (CLAUDE.md rule 6).

## 1 · State of `main` and Azure

- `main` is `57e2398` (backups fix #715). No open PRs. Everything below is merged and deployed except where section 3 says otherwise.
- India dev environment, synthetic data only: resource group `rg-medvya-dev-in` (Central India). Web app `app-medvya-dev-3afs7i` (F1, no staging slot: each deploy has a ~2 min outage), Azure SQL `sql-medvya-dev-3afs7i`, database `medvya` (free serverless, auto-pauses, Entra-only), Key Vault `kv-medvya-dev-3afs7i`, storage `stmedvyadev3afs7i`, Azure Communication Services Email `acs-medvya-dev-3afs7i` (data location India, Azure-managed sender), Application Insights + Log Analytics `log-medvya-dev-3afs7i`.
- The API serves the built web app under **`/app/`** (the public marketing site keeps `/`). Deploy: `gh workflow run deploy-dev.yml --ref main` (builds web, migrates the database as the migration identity, ships the API). Also deploys from a `v*` tag (`v0.0.1` is the proven tagged deploy).
- Database: roles via `tools/db-migrate admin` (done), migrations and Hangfire tables on every deploy. Always Encrypted with a Key Vault key (round trip proven live). Row counts for a restore drill: `dotnet run --project tools/db-migrate -- counts "<connection>"` (read-only, metadata only).
- SQL firewall: 31 `app-outbound-*` rules (the web app's addresses) only. **`restore-me` (the owner's IP) is open and must be deleted after the drill.** The deploy job opens and closes its own temporary rule.
- Sign-in: Entra app "Medvya Clinic (dev, India)" (single tenant). Two **manual tenant settings that are not in Bicep** (documented in `infra/bicep/README.md`): per-user MFA set to Enforced for the test user, and the `amr` optional claim on the app registration's ID token. Without both, every sign-in is refused ("the provider did not report multi-factor authentication").
- Test accounts: owner/doctor `alfred@alfredvpaulhotmail.onmicrosoft.com` (native tenant user, Authenticator registered); admin `alfredvpaul@hotmail.com` (personal account as guest; Entra SQL administrator; protect it with the personal account's own two-step verification). Never type credentials or MFA codes for the owner: ask them.
- Data in Azure is synthetic: clinic "Test Clinic (synthetic)", patients MRN 000001 to 000004, sessions, appointments, queue visits, invitations to `@example.com` addresses (mail there cannot be read).
- Config worth knowing: `Web__AppPath=/app/`, `Email__Provider=Acs` (the app also supports `Smtp`/SendGrid by setting; see `docs/03-architecture/email-providers.md`), `Jobs__Enabled` is NOT set in Azure, so background jobs (appointment reminders) do not run there.
- Backups (Bicep, `docs/runbooks/restore.md`): PITR 7 days (35 in prod), backup storage `Local` (never Geo), blob soft delete + versioning on. **Long-term retention is off in dev**: Azure refuses it on an auto-pausing serverless database; needs an always-on tier before real data.

## 2 · Done today (what changed since the 4 Nov handoff)

Azure database made real; Entra sign-in with MFA works end to end; web app served under `/app/`; Azure email with a provider switch (SMTP or ACS); add doctor, add staff (with invitation email), resend, revoke, offboarding (#687) and practice access log (#689) verified live; walk-through findings fixed (#708: role labels, audit order, clinic time zone, email-failure message, Patients menu); seven more findings fixed by parallel agents (#709 audit board reads once per window, #710 registration screen and clinic time, #711 onboarding creates the owner doctor's sessions and the wizard enforces the 5-minute grid, #712 past slots show "Passed" and the sessions panel says "Saved."); backups as code and the restore runbook (#713, #715); R0 gate draft `docs/releases/r0.md` (#714).

## 3 · In flight: the restore drill (closes P0-019 chunk 2/2 and R0 exit criterion 2)

Runbook: `docs/runbooks/restore.md`. State: **not started successfully.** The database is quiet (the QA agent stopped writing around 11:55 UTC). Capturing the source counts failed twice with `TaskCanceledException` after ~2 minutes in Azure.Identity's `DefaultAzureCredential`, although `az account get-access-token --resource https://database.windows.net/` works. Untested ideas, try in this order: set `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` for the command; or add `az login` freshness (`az account get-access-token` first); or give `db-migrate` an access-token path (small change, needs a test). Then: counts → wait 10 min with no app use → `az sql db restore … --dest-name medvya-restore-drill --time <T+1min>` timed → counts again → `diff` → delete `medvya-restore-drill` and the firewall rule `restore-me` → fill the drill table in the runbook and the R0 notes in one PR; comment on #141 and #143.

## 4 · DONE (5 Nov, evening): issue label cleanup

Applied from the Gherkin tags, chunk lists and merged PRs. **Do not run `gh_issues.py --refresh`** (it overwrites issues from source). Kept here as a record of what was changed and why.

- **Close as done (all acceptance scenarios delivered; leftovers stay in comments):** #201 (P1-004), #225 (P1-009; leftovers: re-activation, paired-device revocation, open-item reassignment), #232 (P1-013; its one `@todo` scenario, consent receipt, was delivered by #678), #270 (P1-032; all 5 scenarios done, 4 chunks closed; note Azure has jobs off and the provider is now ACS, not SendGrid).
- **Keep open, labels already right:** #207 (logo upload #211 open), #233 (P1-014: HIE label by emirate, UAE tenant not creatable in India), #236, #244, #254 to #257, #275, #280 (p95 on pilot volume).
- **Stale closed duplicates carrying wrong labels:** #226 (dup of #227), #250 (dup of #252), #253 (dup of #254), #258 (dup of #259): remove the `status:` label and comment "duplicate of #N". #237/#238 and #263/#265 are closed and done: leave.
- **P1-011 (#230)** is closed done but one Gherkin scenario ("Admin edits the weekly template in Settings → Hours") is tagged `@todo`: the UI exists (verified live); the tag is stale. Comment, do not hand-edit the tag.
- **Chunk issues** all say `status: to do`. Set closed chunks to `status: done`: #202–#206, #208, #209, #212, #215–#220, #222–#224, #245, #271–#274, #276, #277, #281; close duplicate chunks #210 (dup #211), #217 (dup #218). P1-019 has two chunk sets: keep #680–#682 (current) and close #246–#248 as duplicates; set #680, #681 and #282 to `status: partly done` (each has one delivered and one open scenario); #682 stays `to do`.
- Parent `status: partly done` for P1-019 (#244), P1-035 (#280, p95 and CSV export pending) stay.

## 5 · DONE (5 Nov, evening): ticket source updates (PR with the source change; issues created once)

Decided by the owner on 5 Nov and now in the source, regenerated and created as GitHub issues (#717 epic E11 Connectivity and resilience; #718 P1-047 with chunks #719, #720; #721 P1-049; #722 P1-050; #723 P1-051 with chunks #724, #725; #726 P1-052 walk-through fixes with chunks #727 to #734):
1. **P1-047 and P1-048 merge into one ticket P1-047** "Safe retries: idempotency keys, timeouts and bounded retry" with two chunks (server: `Idempotency-Key` on booking, check-in and registration POSTs; client: shared API client with 15 s timeout and bounded jittered retry), about 5 days. The id P1-048 is retired. Source text: `docs/03-architecture/india-connectivity-power.md` section 6; Gherkin in `tickets/_source/gherkin/MDV-P1-047.feature`. For any future ticket creation use `gh_issues.py --apply --only <keys>` after a dry run, and never retry a create call (it duplicated issues once).
2. **P1-049 (service worker for static assets), P1-050 (printable day sheet), P1-051 (encrypted schedule cache, needs an ADR first)** are added as `COULD`, marked deferred until pilot interviews say outages are a real problem.
3. **P1-019 "activation" = one accepted-terms timestamp** (acceptance criteria and Gherkin updated in the source; comment on #244) (`AcceptedAt`) on the tenant that gates booking and sending for the production tenant, built on the draft wording (`2026-11-draft`); final wording remains P1-044 (legal).
4. **Still to write into existing tickets (not done):** extensions noted in the research doc: P1-036 offline banner and unsaved-changes warning (+2 d), P1-037 slow-4G budget and bundle limit (+2 d), P1-041 connectivity telemetry (+2 d), P1-040 internet-down desk card (+1 d).

## 6 · P1 sequence (agreed order)

Status: 46 tickets, 12 done, 34 open. Owner-only items sit on the critical path: P1-043 pilot agreements → P1-039 production deploy → P1-040 pilot → P1-042 two-clinic pilot; P1-045 DLT SMS has weeks of lead time, start it now.

1. Housekeeping (sections 4 and 5), docs and issue edits only, alone (it regenerates `tickets/README.md`).
2. In parallel as separate agents (no overlapping files): **P1-047** (API client, `Host.Api` middleware, Scheduling/Patients POSTs), **P1-017** merge review (Patients module and web patients feature), **P1-037** load test, local part (new perf folder and CI), **Notifications** serial chain P1-034 → P1-006 → P1-010.
3. Then **P1-012** leave and exceptions (Scheduling + migration), then **P1-026** recurring (same booking files), then **P1-036** tablet views, **P1-019** accepted-terms gate, **P1-027** waitlist, **P1-041** metrics.
4. Merge PRs one at a time (i18n JSON, migrations and `tickets/README.md` conflict).

## 7 · QA findings (live walk-through, comment on #125): now ticket **P1-052** (#726, chunks #727 to #734)

- **Queue display screen exists at `/app/admin/displays` but nothing links to it** (requires the staff-config capability); `/app/queue/tv` tells staff to add a screen "under Queue displays" and shows a stuck "Connecting…" for an invalid token. Link it from Settings and fix the copy; then test tokens-only content, live updates and revocation (not yet tested).
- **Cancelled card covers the freed slot in the Day view** (mouse clicks are intercepted; stays in the tab order after re-booking until reload). Major.
- Walk-in: same patient can be checked in repeatedly with no warning; no-slot walk-ins jump the "Next patients" order; success screen does not show the token; quick registration validates one field at a time and a mobile of "12345" passes the client check (server 400, vague message).
- Schedule: 30-minute ruler vs 20-minute slots; selected date not in the URL; stale appointment panel stays open on day/view change.
- Audit log: flooded by `notification.read` and `schedule.read`; action codes untranslated in hi/ar.
- Reminders: panel says "No messages for this visit" and never says reminders are scheduled; quick registration has no reminder consent or email field and there is no way to add consents later; patient portal consent is pre-ticked (matches the docs' "Invite on", confirm in P1-044 review).
- Invitations: a revoked invitation still shows status "Invited" with small "Invitation revoked" text; no in-app invitation link (accepting an invitation needs a real mailbox; **not yet tested**).
- Every full page load shows "Loading…" for 5 to 12 s; "Data map" developer text on registration; "1 visits" plural.
- **Not yet tested:** mark no-show, reassign a waiting patient (only one doctor), a real reminder or invitation mail, accepting an invitation as nurse or receptionist, every screen in Hindi and Arabic.

## 8 · Needs a person (not code)

Legal review of consent wording and ToS/DPA (P1-044); India DLT sender and templates and the SMS provider decision (P1-045, Q-03); pilot agreements (P1-043); external security reviewer (P1-038); ADR sign-offs 001/003–006 (P0-002); launch-market confirmation (P0-003); clinical advisor walks the six journeys and signs off (P0-023, R0 exit criterion 1); a real mailbox the owner can read, to test invitation acceptance; review of the Hindi and Arabic strings added recently; UAE regulator applications (P1-046, later). Azure actions the assistant cannot do under its permission settings (the owner runs them): infrastructure apply (`az deployment group create …`), merging PRs, one-off database grants (put grants in `infra/sql/roles.sql` instead), tenant security settings.

## 9 · Known limits and shortcuts (each is written where it lives)

Audit window is per-instance memory (`AuditWindow`); move to the database before a second API instance. Hangfire runs inside `Host.Api` and jobs are off in Azure. No private endpoints, no Defender, no staging slot; SQL reachable via the app's outbound addresses. Long-term backup retention off in dev. Guest-account MFA lives in the personal Microsoft account; production moves staff sign-in to a dedicated Entra External ID tenant. Consent wording is DRAFT. The 5-minute grid rule is written in both `OnboardingRules` and `ScheduleRules` (a test keeps them in step). Free tier: restore drill creates a billed serverless database for a short time.

## 10 · Working rules and gotchas that bit us

- **Shell:** `dotnet` is in `~/.dotnet`: `export PATH="$HOME/.dotnet:$PATH"`. Docker: `DOCKER_HOST=unix://$HOME/.colima/default/docker.sock TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`. Several agents in parallel: `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` and `-- xUnit.ParallelizeTestCollections=false`. zsh does not expand `--include=*.cs` globs (use `bash -c`); macOS `sed -i ''`. A `!(command)` in the prompt does not run: use `! command`. `gh workflow run deploy-dev.yml --ref main` with nothing after `main`.
- **Process:** never merge with red CI; never `--admin`; never weaken a test or guard. Every safety test is mutation-checked (the change is removed and the test must fail). Warnings are errors. Synthetic data only (names end "(synthetic)", mails `@example.com`).
- **Git:** do not hand-edit `tickets/_source/*.tickets` progress lines, Gherkin tags or generated `tickets/`; edit the source only to change scope or acceptance criteria. A shared checkout can be switched by another session: check `git branch --show-current` before committing.
- **Agents:** give each agent non-overlapping files; tell them not to edit `tickets/**`; ask for mutation checks and exact commands; they often skip the full suite (CI runs it) and manual browser checks. The Playwright MCP browser is shared: one agent at a time; call tools one at a time and never batch a screenshot with a navigation.
- **Live testing:** sessions expire after idle (designed): click "Sign in again", pick the clinic. `/app/` pages show "Loading…" for several seconds after a full load. The telemetry scrubber redacts logged values: one fixed message per reason, never `{Reason}` arguments.
- **Deploy side effects:** a deploy restarts the app (~2 min of 502). An infrastructure apply that changes app settings also restarts it.
