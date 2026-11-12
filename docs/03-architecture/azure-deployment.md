# Azure deployment

Sources: PRD v2 §07/§07b (authoritative), Azure Architecture (stages, residency, ops), Software Architecture §9.

## P0–P6: one deployment per country (PRD v2)

| Resource | Setting |
|---|---|
| App Service plan (Linux) + Web App | .NET 10. Hosts Host.Api + static SPA assets. VNet integration. Staging slot for swap. Managed identity |
| Azure Database for PostgreSQL Flexible Server (ADR-012, MDV-P0-038) | **Entra-only auth** (password login off). VNet-integrated with a private DNS zone, public access **disabled**. Storage encrypted with a customer-managed key. pgaudit (write, ddl, role; `read` is off to stay inside the free Log Analytics tier, patient reads are in the app audit ledger) and connection logs to Log Analytics. PITR 14 days (prod) / 7 (dev), backups kept in country (geo-redundant only to a paired region in the same country). Burstable for dev/staging; General Purpose with zone-redundant HA for prod. Timed restore drill: `docs/runbooks/postgres-restore-drill.md` |
| Storage account (Blob) | Private endpoint, no public access, CMK, user-delegation SAS only, lifecycle rules (audio purge, cool/archive), immutable container for ledger digests |
| Key Vault | RBAC, soft delete + **purge protection**, private endpoint. Holds CMKs, the Always Encrypted column master key, and remaining secrets |
| Application Insights + Log Analytics | OpenTelemetry. **PHI-scrubbing telemetry processor.** Retention by data class |
| Defender for Cloud | All plans relevant to App Service, PostgreSQL, Storage, Key Vault |
| Azure Communication Services | Email and SMS (P1). Region- and DLT-aware |
| Service Bus (P3+) | AI and integration queues, dead-letter, private endpoint |
| Azure OpenAI + AI Speech (P3+) | In-region deployment only. Quota alerts |
| Budgets + cost alerts | Per environment and resource group from day one |
| GitHub OIDC federation | One app identity per environment. **No stored cloud secrets** |

**Environments:** Local (Docker Compose) → preview per PR (optional) → staging (synthetic data) → production per country. Production deploys only from tags. Create staging before the pilot. Shut dev down at night (Build Plan cost advice).

**Regions:** India: Central India (DR South India). UAE: UAE North (DR UAE Central). Paired-region failover must not move data out of the country.

## Later stages (Azure Architecture)

| Stage | What | When (PRD v2) |
|---|---|---|
| 1 Single customer | One subscription, one app, one DB per country | P0–P5 |
| 2 Landing zone + control plane | Management group per country, tenant catalogue, onboarding automation, stamp routing | P5 (platform console) |
| 3 Pooled stamps | IN-01/AE-01 for one-doctor clinics and clinics, RLS. New stamp at ~2,000 tenants or 60% sustained DB CPU | On evidence |
| 4 Dedicated hospital stamps | Own compute + DB + Key Vault + storage, CMK (HSM), private link to the HIS, own upgrade ring | P7-06 |

Azure Front Door Premium + WAF is the production edge from the pilot ([azure-env-prod.md](azure-env-prod.md)). Container Apps/AKS, Fleet Manager, Flux GitOps, KEDA and Sentinel come in with stages 3–4 (P7). They are not used earlier.

Environment records: [dev](azure-env-dev.md), [production target](azure-env-prod.md).

## Targets

- API p95 < 300 ms reads, < 600 ms writes. Page interactive < 2.5 s (p75). Booking API p95 < 300 ms.
- RPO ≤ 15 min, RTO ≤ 4 h (rehearsed) from P5. 99.5% monthly availability target with a status page.
- Daily encrypted backups, PITR, monthly restore test.

## Cost guidance (Build Plan, Software Architecture)

- Start with dev + prod. Add staging before the pilot. Burstable PostgreSQL outside production.
- Pay-as-you-go Azure OpenAI and Speech. Switch to provisioned throughput only when usage is steady.
- Apply for Microsoft for Startups (Founders Hub) credits.
- Private networking adds material cost, so get a regional quote before the first patient.
- AI cost model: `minutes = doctors × consults/day × recorded min × clinic days` (e.g. 3 × 15 × 10 × 22 = 9,900 min/month).
- Azure API for FHIR was retired on 30 Sep 2026. Use Azure Health Data Services FHIR only if a managed FHIR server is truly needed.
