# Claude Code instructions for Medvya

@AGENTS.md

## Current priority: a pilot-ready doctor clinic flow

**Before anything else, the full doctor clinic flow must work end to end so a pilot customer can use it.** That means: sign up and set up the practice, add doctors and staff, book, check in, run the queue and board, see the patient, remind, and offboard. It must work on the real Azure India dev environment (real database, real Entra sign-in), not only locally.

Rank every task by whether it moves a pilot clinic closer to using this flow safely. Prefer it over new features, polish or later phases. A change counts as done for this goal only when it has:

1. **Tests.** Unit and integration tests against real SQL Server, plus a Playwright journey step (J1–J6 in `docs/02-design/journeys.md`) for the user-visible path.
2. **A security check.** Server-side authorisation on each endpoint, tenant isolation (EF filter and row-level security), no PHI in logs or URLs, and `pnpm audit` clean. Anything touching auth, consent or tenancy gets `ponytail:ponytail-review` and a mutation check. The external review (P1-038) comes before the pilot.
3. **A browser check** in English and Arabic, with axe clean.

Work that needs a person (legal, DLT SMS, pilot agreements) is listed in `docs/05-delivery/session-handoff.md`. Say so early when one of those blocks the pilot.

## Claude-specific rules

1. **Use the Ponytail skill (`ponytail:ponytail`, level `full`) for every development task.** That means writing, fixing, refactoring, reviewing and choosing dependencies. Invoke it at the start of the task, before reading or writing code. Its discipline is the smallest complete change: reuse first, no speculative code, tests for new logic, and `shortcut:` comments for known limits.
2. For a review of a change, use `ponytail:ponytail-review`. For a whole-repo quality pass, use `ponytail:ponytail-audit`. To list recorded shortcuts, use `ponytail:ponytail-debt`.
3. End every development reply with one or two lines saying what was skipped or not checked, and any risk the user must know.
4. Local environment notes:
   - .NET SDK: the version pinned in `global.json` must be installed (`dotnet --list-sdks`). If it is in `~/.dotnet`: `export PATH="$HOME/.dotnet:$PATH"`.
   - Docker is Colima or Docker Desktop, with Rosetta. For Testcontainers, set `DOCKER_HOST` to the running engine's socket (Colima `unix://$HOME/.colima/default/docker.sock`, Docker Desktop `unix://$HOME/.docker/run/docker.sock`) and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.
   - Several agents building in parallel worktrees: use `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` (shared MSBuild nodes deadlock), run `pnpm services:up` from one checkout only, and run integration tests with `-- xUnit.ParallelizeTestCollections=false` when Docker memory is shared.
   - Local services: `pnpm services:up` (PostgreSQL, Azurite, email sink).
5. Never merge a PR with red CI, never use `--admin` to skip a failing check, and never weaken a test or lint rule to get green (AGENTS.md 2.7). `--admin` is only for the "branch must be up to date" requirement, and only after the PR's own CI is green and `main` is verified green afterwards.
6. **GitHub issues are the live record of ticket status. Update the issue whenever work starts or finishes, not the ticket files.** When you start a ticket, set its issue label to `status: in progress` and comment what you are doing. When a PR merges (or work stops short), set `status: done` / `status: partly done` / `status: blocked`, comment what was delivered and what is still pending, and close the issue only when the acceptance criteria are met. Tick the chunk sub-issues the same way. Do not hand-edit `docs/05-delivery/tickets/_source/*.tickets` progress lines, the Gherkin tags or the generated `tickets/` files just to record status, and do not run `tools/tickets/gh_issues.py --refresh` (it overwrites issues from the source files). Edit the source only to change what a ticket asks for (scope or acceptance criteria). This overrides AGENTS.md 2.1 for status tracking only.
