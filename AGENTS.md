# Rules for AI coding assistants (and humans)

1. **Synthetic data only.** Never put real patient data in code, tests, fixtures, logs, CI output or AI evaluation sets. Use the synthetic cast in `docs/00-overview/personas.md`.
2. **No PHI in logs or telemetry.** Log opaque ids, timings and error types only. Never names, phone numbers, national IDs (Aadhaar, ABHA, Emirates ID), MRNs or clinical text.
3. **Module boundaries.** A module never touches another module's tables or EF context. Talk through application services, contracts or domain events (`docs/03-architecture/modules.md`).
4. **Country logic** lives in country rule classes, not scattered `if (country == …)` checks.
5. **Authorisation on the server**, on every endpoint: tenant, facility, role, relationship, purpose. The UI hiding something is never the control.
6. **Clinical safety:** AI output is a draft until a clinician signs it. Unknowns stay blank. Signed records are never edited, only amended by addendum.
7. **Work from tickets.** Each change references its ticket key (e.g. `MDV-P0-001`) and meets the Definition of Done in `docs/05-delivery/README.md`.
8. **Versions** of NuGet packages live only in `Directory.Packages.props`. Shared build settings live in `Directory.Build.props`.
9. **Front-end layering (hard rule, ADR-009).** In `web/*/src/features/<feature>/`: `domain/` is pure, `services/` does I/O only, `hooks/` holds controller hooks (`useXxxController`, TanStack Query, React Hook Form) and `components/` is presentational (props in, events out). `app/` wires controllers to components. Lint enforces it; see `docs/03-architecture/frontend-architecture.md`.
