# ADR-003 · Country isolation: separate data planes

- **Status:** Proposed (P0)

## Decision

India and UAE run as **separate deployments**: separate subscription or resource groups, Azure SQL logical server, keys, Blob, logs, backups, AI endpoints and support access. A tenant is bound to one country when it is created (F-02). Moving it means a new tenant plus an export. There is no shared patient datastore and no cross-border replication. Paired-region DR stays in-country (South India, UAE Central).

## Consequences

- Azure Policy denies resource creation outside the allowed regions (`infra/bicep/policies/deny-cross-region.json`).
- The global control plane (P5 platform console) holds **no PHI**: only tenant catalogue, plan and routing.
- Telemetry and support tooling must be per-country or PHI-free.
