# ADR-009 · Front-end architecture: domain-driven features and controller hooks

- **Status:** Accepted (founder requirement, 21 Oct 2026)
- **Numbering:** ADR-007 and ADR-008 are reserved by tickets MDV-P3-001 (speech/LLM providers) and MDV-P4-029 (reporting store).

## Context

The clinic and portal apps will grow to dozens of clinical screens. Without a rule, data fetching, business rules and rendering mix inside components. That makes patient-data access hard to review, rules hard to test, and code hard to share with a future mobile app.

## Decision

- Features are organised by domain capability: `features/<feature>/{domain,services,hooks,components}`.
- **Controller hooks** (`useXxxController`) are the only bridge between UI and services. They own server state (TanStack Query), form state (React Hook Form + Zod) and orchestration, and return a view model plus handlers.
- **Components are presentational:** props in, events out. No services, no I/O.
- **Services** do I/O only. **Domain** is pure.
- `app/` composes: it calls controllers and passes their results to components.
- The rule is **enforced by lint** (`no-restricted-imports`, `no-restricted-globals` per layer in `.oxlintrc.json`), so CI fails on a violation.

Details and examples: [docs/03-architecture/frontend-architecture.md](../03-architecture/frontend-architecture.md).

## Consequences

- Every patient-data call is findable in `services/`, which keeps security review focused.
- Slightly more files per feature; accepted for clarity and testability.
- Shared logic for portal and mobile lives in `domain/` and `services/`, or moves into a package when two apps need it.
