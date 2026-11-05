# Claude Code instructions for Medvya

@AGENTS.md

## Claude-specific rules

1. **Use the Ponytail skill (`ponytail:ponytail`, level `full`) for every development task.** That means writing, fixing, refactoring, reviewing and choosing dependencies. Invoke it at the start of the task, before reading or writing code. Its discipline is the smallest complete change: reuse first, no speculative code, tests for new logic, and `shortcut:` comments for known limits.
2. For a review of a change, use `ponytail:ponytail-review`. For a whole-repo quality pass, use `ponytail:ponytail-audit`. To list recorded shortcuts, use `ponytail:ponytail-debt`.
3. End every development reply with one or two lines saying what was skipped or not checked, and any risk the user must know.
4. Local environment notes:
   - .NET SDK: the version pinned in `global.json` must be installed (`dotnet --list-sdks`). If it is in `~/.dotnet`: `export PATH="$HOME/.dotnet:$PATH"`.
   - Docker is Colima or Docker Desktop, with Rosetta. For Testcontainers, set `DOCKER_HOST` to the running engine's socket (Colima `unix://$HOME/.colima/default/docker.sock`, Docker Desktop `unix://$HOME/.docker/run/docker.sock`) and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.
   - Several agents building in parallel worktrees: use `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` (shared MSBuild nodes deadlock), run `pnpm services:up` from one checkout only, and run integration tests with `-- xUnit.ParallelizeTestCollections=false` when Docker memory is shared.
   - Local services: `pnpm services:up` (SQL Server, Azurite, email sink).
5. Never merge a PR with red CI, never use `--admin` to skip a failing check, and never weaken a test or lint rule to get green (AGENTS.md 2.7). `--admin` is only for the "branch must be up to date" requirement, and only after the PR's own CI is green and `main` is verified green afterwards.
6. **GitHub issues are the live record of ticket status. Update the issue whenever work starts or finishes, not the ticket files.** When you start a ticket, set its issue label to `status: in progress` and comment what you are doing. When a PR merges (or work stops short), set `status: done` / `status: partly done` / `status: blocked`, comment what was delivered and what is still pending, and close the issue only when the acceptance criteria are met. Tick the chunk sub-issues the same way. Do not hand-edit `tickets/_source/*.tickets` progress lines, the Gherkin tags or the generated `tickets/` files just to record status, and do not run `tools/tickets/gh_issues.py --refresh` (it overwrites issues from the source files). Edit the source only to change what a ticket asks for (scope or acceptance criteria). This overrides AGENTS.md 2.1 for status tracking only.
