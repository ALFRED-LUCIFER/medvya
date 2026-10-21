# ADR-001 · Stack: .NET 10 modular monolith, React/MUI, Azure SQL

- **Status:** Proposed. To be accepted in P0 week 1 (ticket MDV-P0-002).
- **Date:** 2026-10-20

## Context

Three source documents proposed three stacks:
- Architecture v2: .NET modular monolith
- Build Plan: Vite + NestJS + Python + PostgreSQL + AKS
- October 2026 plan: Next.js 16 + App Service

A single owner-developer must build and secure the product. It handles health data in two jurisdictions and needs column encryption that database administrators cannot read, plus tamper evidence.

## Decision

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core on **.NET 10 LTS** (C# 14), modular monolith, minimal APIs + OpenAPI |
| Session | ASP.NET Core **BFF** (HTTP-only secure cookies, antiforgery; no tokens in the browser) |
| Web | **React 19 + TypeScript + Vite**, shadcn/ui + Radix + Tailwind 4 (ADR-010 supersedes the original MUI choice), TanStack Query, React Hook Form + Zod, React Router (SPA) |
| Public site | ASP.NET Core Razor Pages (landing, trust centre, doctor profiles) |
| Database | **Azure SQL Database** (one logical server per country), EF Core 10 migration bundles, Dapper for reporting |
| Identity | Microsoft Entra External ID (staff and patients), Entra federation for hospitals |
| Jobs | Hangfire on Azure SQL (P1). Azure Service Bus + .NET workers (P3+). Redis only on measured need |
| AI | .NET worker with Microsoft.Extensions.AI, Azure AI Speech, Azure OpenAI in region. **Python only for offline evaluation** |
| Realtime | SignalR (board, queue TV, live transcript) |
| Files | Private Blob (CMK), malware scan, user-delegation SAS. QuestPDF (check licence tier) |
| Interop | Firely .NET SDK (FHIR), HL7 v2 (P5, P7) |
| Hosting | Azure App Service (Linux) per country. Container Apps/AKS only from P7 stamps |
| Quality | xUnit, Testcontainers, Vitest, Playwright. OpenTelemetry → Application Insights. GitHub Actions with OIDC, CodeQL, Dependabot, secret scanning |

## Consequences

- One production language for backend, workers and jobs. Shared tooling.
- .NET 10 is supported to November 2028. Plan the move to the next LTS in 2027–28.
- ERD and Build Plan Postgres assumptions must be mapped before P0 ends (MDV-P0-016).
- Kubernetes, microservices, Kafka, GraphQL and Elasticsearch are not used in P0–P5.
- Not chosen: NestJS (second language), Next.js server components (extra server attack surface with patient data), Cosmos DB (relational data), PostgreSQL (strong alternative, but no built-in admin-proof column encryption or ledger).
