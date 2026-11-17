# Claude Code instructions for Medvya

@AGENTS.md

## Current priority: a pilot-ready doctor clinic flow

**Before anything else, the full doctor clinic flow must work end to end so a pilot customer can use it.** That means: sign up and set up the practice, add doctors and staff, book, check in, run the queue and board, see the patient, remind, and offboard. It must work on the real Azure India dev environment (real database, real Entra sign-in), not only locally.

Rank every task by whether it moves a pilot clinic closer to using this flow safely. Prefer it over new features, polish or later phases. A change counts as done for this goal only when it has:

1. **Tests.** Unit and integration tests against real PostgreSQL (Testcontainers), plus a Playwright journey step (J1–J6 in `docs/02-design/journeys.md`) for the user-visible path.
2. **A security check.** Server-side authorisation on each endpoint, tenant isolation (EF filter and row-level security), no PHI in logs or URLs, and `pnpm audit` clean. Anything touching auth, consent or tenancy gets `ponytail:ponytail-review` and a mutation check. The external review (P1-038) comes before the pilot.
3. **A browser check** in English and Arabic, with axe clean.

Work that needs a person (legal, DLT SMS, pilot agreements) is listed in `docs/05-delivery/session-handoff.md`. Say so early when one of those blocks the pilot.

## Claude-specific rules

1. **Use the Ponytail skill (`ponytail:ponytail`, level `full`) for every development task.** That means writing, fixing, refactoring, reviewing and choosing dependencies. Invoke it at the start of the task, before reading or writing code. Its discipline is the smallest complete change: reuse first, no speculative code, tests for new logic, and `shortcut:` comments for known limits.
2. For a review of a change, use `ponytail:ponytail-review`. For a whole-repo quality pass, use `ponytail:ponytail-audit`. To list recorded shortcuts, use `ponytail:ponytail-debt`.
3. End every development reply with one or two lines saying what was skipped or not checked, and any risk the user must know.
4. Local environment notes:
   - .NET SDK: the version pinned in `global.json` (`dotnet --list-sdks`); if it is in `~/.dotnet`: `export PATH="$HOME/.dotnet:$PATH"`.
   - Docker is Colima or Docker Desktop, with Rosetta. For Testcontainers, set `DOCKER_HOST` to the engine's socket (Colima `unix://$HOME/.colima/default/docker.sock`, Docker Desktop `unix://$HOME/.docker/run/docker.sock`) and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`. Colima's socket can die under parallel test runs: tell the owner; agents never restart it.
   - Parallel worktrees: `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` (shared MSBuild nodes deadlock), `pnpm services:up` (PostgreSQL, Azurite, email sink) from one checkout only, integration tests with `-- xUnit.ParallelizeTestCollections=false`. `dotnet run` of Host.Api from a worktree needs `Storage__ConnectionString` (Azurite) set.
   - e2e uses ports 4173 and 4174: a stale server from another session gives false results (`lsof -iTCP:4173`). The Playwright MCP browser is shared: one agent, one call at a time, never a screenshot batched with a navigation.
   - Shell: zsh does not expand `--include=*.cs` (use `bash -c`); macOS `sed -i ''`; a tool call that starts with `sleep` is blocked (wait with a background `until` loop).
   - Azure: the PostgreSQL admin is not a superuser; migrations run only through the in-VNet database job; extensions are allow-listed on the server before a release is tagged; the job image has no `tar`. Infrastructure applies, Key Vault writes, tenant settings and anything named prod are the owner's.
5. **Merging.** Agents get a PR green and report; they merge only on the owner's explicit word, with `gh pr merge --auto --squash` (never `--admin`; branch protection binds administrators). Required checks are `dotnet`, `web` and `secrets`, and `main` does not require an up-to-date branch, so before merging update the branch onto `main` if `main` moved since the PR's last green run (two PRs green alone broke `main` together, #1311 + #1321). Never merge red or unfinished CI, never weaken a test or lint rule (AGENTS.md 2.7). When `main` is red, fixing it comes before any other merge.
6. **Ticket status** is on the GitHub issue and the Project 1 board (AGENTS.md 2.1): update both when work starts and when it finishes.
