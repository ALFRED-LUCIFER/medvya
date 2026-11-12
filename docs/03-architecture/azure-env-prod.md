# Azure environment: production (India), target

**Status: planned, not deployed.** There is no production subscription yet; the founder creates it before the pilot and the ids go into the table below (they are not secrets). Until then dev ([azure-env-dev.md](azure-env-dev.md)) is the only environment. Production deploys only from tags, never from a laptop.

| Item | Value |
|---|---|
| Subscription | _to be created by the founder_ (separate from dev, billing owner: founder) |
| Entra tenant | Entra External ID tenant for staff sign-in (dev uses the workforce tenant) |
| Region | Central India (disaster recovery: South India, same country) |
| Resource group | `rg-medvya-prod-in` |
| Template | `infra/bicep/main.bicep` and `postgres.bicep` with `env=prod`; the edge below is added by the ticket that builds it |

## Same as dev, but stronger (already in the templates)

Zone-redundant General Purpose PostgreSQL, 14-day point-in-time restore, ledger digests kept 3,650 days and the immutability policy locked, App Service plan with a staging slot, Defender for Cloud, budget alerts.

## Edge: Azure Front Door Premium + WAF (to build)

| Control | Setting |
|---|---|
| Entry | Front Door is the only public entry. The web app accepts traffic only from Front Door (service tag and `X-Azure-FDID` check) |
| WAF | Prevention mode, Microsoft default rule set and bot manager rule set |
| Rate limits | WAF custom rules per client IP: sign-in and invitation paths (`/signin-oidc`, `/auth/*`, `/api/invitations/*`) strict, for example 20 requests per minute; `/api/*` 600 per minute; static assets unrestricted. Over the limit returns 429. Numbers are tuned on pilot traffic |
| TLS | TLS 1.2+, managed certificate on the custom domain, HSTS |
| Logs | Front Door and WAF logs to Log Analytics (no request bodies, no query strings with PHI) |
| Residency | Front Door is a global service but holds no patient data. Origin, database, storage and logs stay in Central India |

shortcut: the Front Door template is not written or deployed; build it before the first real patient (pilot gate), with a what-if run and a rate-limit test (a burst of sign-in requests gets 429).
