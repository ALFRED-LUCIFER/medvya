# ADR 0002: Original-language transcript is immutable; translation is a separate artifact

**Status:** accepted (2026-11-09)

## Context

Hindi, Marathi and Hinglish consultations are transcribed by Azure Speech fast transcription with
candidate locales (`SPEECH_LOCALES`) and diarization. The LLM "normalise" step produces an English
rendering used for extraction. A silently "fixed" transcript would hide ASR errors from the
clinician and make evidence quotes unstable.

## Decision

- `transcript_segment.original_text` is what the provider heard, in the original language. It is
  never updated. `english_text` is a separate column filled by the normalise step (provider,
  model and token counts are recorded on the job, `processing_job.usage`).
- Clinician corrections are append-only rows in `transcript_correction`; the API shows the latest
  correction as `text` next to the untouched `original_text` (`edited = true`). Fact evidence keeps
  pointing at the original turn, so quotes stay verifiable; the correction response lists the
  facts that cite the turn so the clinician re-reviews them.
- Provider speaker numbers are anonymous and per request. A missing number is `unknown`, never
  "Speaker 1". With several recording parts the label carries the part (`Speaker 1 (part 2)`)
  until the clinician maps it with the relabel endpoint. Timestamps are global:
  `start_ms = recording_segment.start_offset_ms + phrase offset`.
- Extraction runs on the English text but every quote must be found verbatim (normalised
  whitespace and case) in the cited turn's English or original text. Values (numbers, doses,
  routes, frequencies) are kept only when the cited text contains them, and a fact the cited text
  negates (English, romanised Hindi, Devanagari cues) is stored as `uncertain` and listed under
  "to confirm". Rejected proposals are kept in `extraction_rejection` for evaluation, never shown
  as facts.
- Marathi (`mr-IN`) stays a gated pilot locale: add it to `SPEECH_LOCALES` only for clinics in the
  evaluation programme; language identification picks one dominant locale per file
  (`src/AiService/docs/provider-notes.md`).

## Consequences

Hinglish quality depends on the normalise model and must be measured on consented or synthetic
audio (`tests/evals`, live mode) before any clinical claim. The eval harness reports WER/CER per
slice, negation fidelity and unsupported-fact counts; the gate is zero unsupported critical facts.
