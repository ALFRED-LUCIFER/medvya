# PRD v2 — phase by phase

PRD v2 (20 Oct 2026) consolidates every design in the project, Architecture v2, the UI/UX master prompt, the gap analysis, the Azure architecture, the Build Plan and the October 2026 stack plan into **nine phases (P0–P8)**. Each phase ends with a working release handed to a named group of users.

| File | Content |
|---|---|
| [roadmap.md](roadmap.md) | Phases, weeks, **calendar dates**, milestones, day-one capability, calendar risks |
| [requirements-catalogue.md](requirements-catalogue.md) | All **77 functional requirements** verbatim (P0-01 … P8-06), plus each phase's NFRs, exit gate, delivery package, dependencies and exclusions |
| [cross-cutting-metrics-process.md](cross-cutting-metrics-process.md) | X1–X8 cross-cutting requirements (part of every Definition of Done), success metrics, release process |
| [technology-and-db-security.md](technology-and-db-security.md) | Stack decisions, database comparison, the 12 layers of data protection |
| [risks-and-decisions.md](risks-and-decisions.md) | Risk register, open decisions with defaults, postponed scope |
| [traceability.md](traceability.md) | Screen traceability matrix (96 rows), October plan → phase, Architecture v2 inventory → phase, gap items → phase |
| [completeness-and-findings.md](completeness-and-findings.md) | PRD audit findings and the completeness check (Covered / Added v2 / Open) |
| [source-conflicts.md](source-conflicts.md) | Every conflict between source documents and how it is resolved, plus open questions |
| [data/prd-v2.json](data/prd-v2.json) | Machine-readable PRD data extracted from the design file. `tools/tickets/build.py` uses it to verify ticket coverage |

The generated files are rebuilt with `python3 -I tools/prd-extract/render_docs.py`. The data is re-extracted with `node tools/prd-extract/extract.mjs "design/Medvya PRD v2.dc.html" > docs/01-prd/data/prd-v2.json`.

## Phase overview

| Phase | Release | Weeks | Recipients | Requirements |
|---|---|---|---|---|
| P0 Foundation and safety | R0 Internal platform | 1–3 | Founder + clinical advisor (staging, synthetic) | P0-01 … P0-10 |
| P1 Front desk and appointments MVP | R1 Front Desk | 4–10 | Reception, doctors (schedule view), owners at 2 design-partner clinics | P1-01 … P1-12 |
| P2 Doctor workspace and EMR core | R2 Consult | 9–15 | Doctors and nurses at the 2 partner clinics | P2-01 … P2-08 |
| P3 AI-drafted notes under clinician review | R3 Scribe | 13–20 | 5–10 opt-in clinicians | P3-01 … P3-08 |
| P4 Billing, care follow-up, patient portal | R4 Clinic Ops | 17–26 | Owners/admins, care coordinators, patients | P4-01 … P4-10 |
| P5 Commercial launch v1.0 | R5 v1.0 GA | 24–32 | First paying clinic (wk 29), then the first 5 | P5-01 … P5-12 |
| P6 Wearable capture and advanced AI | R6 Clip | 33–42 | Doctors issued a clip, their admins | P6-01 … P6-05 |
| P7 Hospital enterprise and integrations | R7 Enterprise | 36–60 | First hospital: IT, admins, clinicians | P7-01 … P7-06 |
| P8 Scale and specialty | R8 Ongoing | 52+ | All customers, by demand | P8-01 … P8-06 |
