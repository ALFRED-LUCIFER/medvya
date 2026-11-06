# Repository structure

**Status:** accepted, 6 Nov 2026 ([ADR-013](../adr/ADR-013-repository-layout.md)); built by ticket MDV-P0-040.

The tree, with what exists and what is planned, is kept in one place: the **"Repository structure"** section of the [README](../../README.md). It is not repeated here so the two cannot drift.

## Rules

1. One monorepo: backend (`src/`), web (`web/`), mobile (`mobile/`), shared TypeScript (`packages/`), tests (`tests/`), infrastructure (`infra/`), tools (`tools/`) and docs (`docs/`). Existing folder names stay.
2. A planned module, app or package is created by the ticket that builds it, never scaffolded empty.
3. `src/Host.Api` is the composition root and web BFF only. Feature endpoints live in the module's `Api` project. `tests/Medvya.ArchitectureTests/HostCompositionTests.cs` enforces it.
4. A module is `src/Modules/<Module>/Medvya.<Module>.{Domain,Application,Infrastructure,Api}` and never touches another module's tables or DbContext (talk through application services, contracts or events).
5. Web and mobile share `packages/tokens`, `i18n`, `api-client`, `domain` and `auth-client`. `packages/ui` is web only; `ui-native` is native only; no package imports an app.
6. `packages/api-client` is generated from the OpenAPI document Host.Api exports; `tools/check-contract.sh` and the `contract` CI job fail on drift.
7. Tickets (`docs/05-delivery/tickets/`) and the imported design sources (`docs/02-design/source/`) live under docs. `tools/check-links/check.py` keeps every relative Markdown link valid in CI.
8. CI runs per area (a `changes` job decides which of `dotnet` and `web` run; docs checks always run). Pushes to `main` run everything.

## Adding something

| To add | Do |
|---|---|
| A backend module | Create the four projects under `src/Modules/<Module>/`, add them to `Medvya.sln`, register in `Host.Api`, add the schema, regenerate the ERD, add the README row |
| A web app | `web/<name>/` as a pnpm workspace member, with the ADR-009 feature layers and the shared lint config |
| A mobile app | `mobile/<name>/` (Expo), reading shared packages through the workspace; point lint at `packages/.oxlintrc.json` |
| A shared package | `packages/<name>/` with its own tests; it must not import an app |
| A document | Under `docs/`; run the link check |
