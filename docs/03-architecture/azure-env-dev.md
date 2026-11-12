# Azure environment: dev (India)

Synthetic data only. Template: `infra/bicep/main.bicep` (how to deploy: `infra/bicep/README.md`). Identifiers are not secrets; no secret belongs in this file.

| Item | Value |
|---|---|
| Subscription | `Subscription 1`, id `bbe08354-5dd6-4b0c-a76a-6b5d3a03e999` (development only) |
| Entra tenant | `8ea5d9e7-f960-42e9-8979-b60dd7c08a64` |
| Billing owner | Founder (account owner of the subscription) |
| Region | Central India (disaster recovery: South India). Nothing leaves India (AGENTS.md 1.6) |
| Resource group | `rg-medvya-dev-in` |
| App | `app-medvya-dev-3afs7i` (App Service B1, Linux, .NET 10) |
| Database | `psql-medvya-dev-3afs7i` (PostgreSQL Flexible, Burstable, private, Entra-only) |
| Deploys | Tag-triggered `deploy-dev` workflow, GitHub OIDC, DB steps through `job-db-dev-3afs7i` |
| Cost guard | Monthly budget 3,500 INR, alerts at 50/80/100 % |
| AI services | Not deployed. Azure OpenAI (gpt-4.1, gpt-4o) and AI Speech are listed in Central India (checked 12 Nov 2026); quota is requested when P3 starts |
| Credits | Microsoft for Startups (Founders Hub): not applied for now; revisit before production spend |

Not in dev by design: Front Door, WAF, zone-redundant HA, customer-managed key rotation drills. They are in production ([azure-env-prod.md](azure-env-prod.md)).
