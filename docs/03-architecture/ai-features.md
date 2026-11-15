# AI features: what belongs where

Every AI ticket carries the `ai` label plus one **area label**, on the ticket source and on its GitHub issue. Filter GitHub by the area label to see one feature's work.

| Area label | Feature | Where the AI runs | Where the records live | Tickets |
|---|---|---|---|---|
| `ai-notes` | **AI notes** (scribe): consented recording → transcript → evidence-linked draft → the doctor accepts each field and signs | AI service `src/AiService` ([ADR-021](../adr/ADR-021-ai-service.md)), pipeline in [ai-pipeline.md](ai-pipeline.md) | Encounters module (`ConsultNote`, signed versions) | MDV-P3-001 to MDV-P3-030, MDV-P1-055, MDV-P5-035, MDV-P6-007, MDV-P6-008 |
| `ai-platform` | **Communication middleware**: one conversation engine, channel adapters, tenant resolution, AI turns with tools run by Medvya, integration health | AI service (turns, knowledge index) | Conversations module ([ADR-022](../adr/ADR-022-ai-communication-middleware.md)) | MDV-P4-035, MDV-P4-036, MDV-P4-038, MDV-P4-043 |
| `ai-whatsapp` | **WhatsApp AI**: book, reschedule, cancel, questions, notifications | AI service through the middleware | Conversations, Scheduling (one source of truth) | MDV-P4-037, MDV-P4-040 (channel reminders: MDV-P4-028; Meta approvals: MDV-P4-032) |
| `ai-chat` | **Clinic AI chat**: a clinic-specific assistant on the public page, answering from the clinic's own knowledge | AI service through the middleware | Conversations, Tenancy (services, doctors, hours) | MDV-P4-039, MDV-P4-041 |
| `ai-support` | **Support desk**: patient-to-clinic tickets with AI drafts staff approve; clinic-to-Medvya technical tickets, kept apart | AI service (drafts) | Conversations; platform console for Medvya tickets | MDV-P4-019, MDV-P4-042, MDV-P5-047 |
| `ai-voice` | **Voice**: inbound booking assistant, earpiece assistant for the doctor, outbound reminder calls | AI service (planned) | Scheduling, Encounters | MDV-P5-015, MDV-P6-006, MDV-P6-009 |
| `ai-records` | **Outside records**: AI-proposed migration mappings, extraction from patient uploads, each approved by a person | AI service (planned) | Patients, Migration | MDV-P5-003, MDV-P5-014 |

Rules that hold for every area:

- AI output is a draft until a person accepts it (AGENTS.md 1.5). A tool the AI requests is checked and run by Medvya, never by the model.
- Only Medvya's API calls the AI service. The browser and the apps never do (ADR-004, ADR-021).
- Tenant isolation and the PHI rules apply on both sides. WhatsApp and chat carry appointment facts and secure links only.

The clip hardware (MDV-P6-001 to MDV-P6-005, MDV-P6-010) records for AI notes, but it is device work, so it has no AI label.
