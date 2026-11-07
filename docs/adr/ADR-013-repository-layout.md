# ADR-013 · Repository layout: one monorepo, existing folder names kept

- **Status:** Accepted, 6 Nov 2026 (founder). Ticket: MDV-P0-040.
- **Note:** ADR-011 is the mobile apps decision ([ADR-011](ADR-011-mobile-apps.md), MDV-P5-031); the PostgreSQL decision is ADR-012.

## Decision

One repository holds backend, web, mobile, shared packages, infrastructure, tools and docs.

```text
src/            .NET: Host.Api (composition, BFF, public site), BuildingBlocks, Modules, Workers
web/            clinic (exists), platform-admin, portal
mobile/         clinician, patient (Expo)
packages/       ui, i18n (exist); tokens, api-client, domain, auth-client, ui-native, config, test-fixtures (planned)
tests/          Medvya.ArchitectureTests, Medvya.IntegrationTests (exist); Medvya.SecurityTests, Medvya.ContractTests, e2e, mobile-e2e, ai-evals (planned)
infra/          bicep (exists); postgres, monitoring (planned)
tools/  docs/   (docs also holds tickets/ and the design sources, see below)
```

Rules:

1. **Keep existing folder names.** `src/`, `web/`, `tests/` and `infra/` stay. Renaming to `backend/` or `apps/` would change hundreds of references for no gain.
2. **No empty scaffolding.** A planned folder or module is created by the ticket that builds it, not before. The README marks planned ones.
3. **Host.Api is composition only:** `Program.cs`, auth/BFF, context, middleware, observability, the public site and module registration. Feature endpoints live in the module's Api project (architecture test).
4. **A separate platform host** (`Host.PlatformApi`) is deferred. The Medvya team console starts as an isolated endpoint group with its own policy set and moves to its own host if the security review asks for it (MDV-P5-040).
5. **Mobile in the same workspace.** `mobile/*` shares `packages/tokens`, `packages/i18n`, `packages/api-client`, `packages/domain` and `packages/auth-client`. `packages/ui` is web only; native uses `packages/ui-native`. No package imports an app.
6. **One API contract.** Host.Api exports its OpenAPI document at build time; `packages/api-client` is generated from it and CI fails on drift.
7. **Docs hold the documents.** `tickets/` moves to `docs/05-delivery/tickets/` and `design/` to `docs/02-design/source/`, with a CI link check.
8. **Per-area CI** with path filters (backend, web, mobile, packages, infra, docs). Mobile store builds run only when mobile or shared packages change.
9. **Separate repositories only** for a reason other than taste: a separate team, an external party needing partial access, or a public SDK.

## Alternatives rejected

- `backend/` + `apps/` rename: cosmetic, high churn.
- Separate repos for mobile, backend and web: shared tokens, i18n and contract would need publishing and version sync; atomic cross-stack changes become ordered PRs.
- Scaffolding every planned folder now: empty projects drift and violate the smallest-change rule.

## Consequences

- pnpm and React Native need care (hoisting). Prove it with a throwaway Expo build in MDV-P5-032 before relying on it.
- Moving `tickets/` and `design/` breaks links unless every reference is rewritten; the link check guards it.
