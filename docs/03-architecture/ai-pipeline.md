# AI pipeline (P3 onward)

Sources: PRD v2 P3, Azure Architecture "Ambient note request flow", Software Architecture §3 and §10.

## Flow

1. **Capture.** Starts only after per-encounter recording consent. The browser/phone records encrypted chunks and uploads them via a short-lived scoped SAS (one encounter, one blob prefix). Chunk manifest has sequence numbers and SHA-256.
2. **Enqueue.** On stop, the API marks the session READY and writes an outbox event. A Service Bus message carries the **opaque job ID only**.
3. **Authorise.** The AI worker re-checks consent, tenant and retention policy through a restricted internal API, then fetches time-limited audio.
4. **Transcribe.** Azure AI Speech (in-region) with diarisation and language identification (English / Hindi / Tamil in P3; Arabic and Malayalam mixed in P6).
5. **Draft.** Azure OpenAI (in-region) through Microsoft.Extensions.AI, using a versioned prompt and template. Output is **JSON-schema constrained**. Every statement carries evidence (transcript segment IDs, or verified chart/document references).
6. **Check.**
   - Schema and provenance validators.
   - Unknowns left blank, never guessed.
   - No invented negatives, medicines or doses.
   - Conflicts with the chart are flagged (e.g. statin active vs stopped).
   - Speaker uncertainty is flagged.
   - Emergency-symptom prompt (A-08) is **not triage**.
7. **Callback.** Restricted, authenticated internal callback. The API validates job, tenant and consent and saves the draft. The doctor is notified with uncertainty visible.
8. **Review & sign.** The doctor accepts or edits each field against its evidence. Sign is blocked until there are 0 unresolved fields. AI never signs.
9. **Retain.** Audio is purged per policy (default 30 days, documentation purpose). Transcripts and drafts are kept with the signed note. An audit entry is written.

## Worker privileges

- **No database credentials.** No broad roles. No arbitrary tool calls. One permitted AI provider per environment.
- Transcripts and documents are **untrusted input** (prompt-injection defence). Instructions inside them are ignored.
- Telemetry carries job IDs, timings and error types only.

## Quality gate (P3-07, P6-05)

- A de-identified evaluation set runs in CI **before any prompt or model change** (Python allowed here only).
- Report these metrics separately: critical omission, unsupported clinical assertion, speaker attribution, wrong-patient attach, dose error, review time.
- Release gates (P3 exit): **0 fabricated medicines or doses** across the evaluation set. ≥ 80% of drafts signed with only minor edits. 100% of recordings have consent.
- Draft ready ≤ 60 s (p50) after recording stops.
- No training on customer data without a separate lawful basis. AI vendor DPA and regional processing are confirmed per country.

## Synthetic test pack must cover

Hindi/English code-switching · doctor/patient/relative voices · noisy rooms · missing allergies · medication strengths · negation · interrupted recording · contradictory statements · report-to-patient mismatch.
