# ADR 0003: Processing region and approved processors

**Status:** proposed, decision required before a real-patient pilot (2026-11-09)

## Context

Audio is stored in Azure Blob and processed by Azure Speech (`SPEECH_REGION`, currently
`centralindia`) and an Azure AI Foundry model deployment (`FOUNDRY_ENDPOINT`). The audit at hand
reports the Foundry resource in UAE North. A Foundry *project* region does not prove where
inference runs: Global Standard and Data Zone deployment types route outside the resource region.

## Decision (to be confirmed by the project owner and legal review)

1. Clinical pilots in India run every processor in an Indian region: Storage and PostgreSQL in
   Central India, Speech in Central India, and a Foundry deployment whose **deployment type** is
   regional (Standard) in an Indian region with a model that is actually available there.
   Evidence to attach: portal screenshots or `az cognitiveservices account deployment show`
   output listing `sku.name` and the resource location, saved under `src/AiService/docs/reports/`.
2. Until such a deployment exists, `LLM_PROVIDER=foundry` is only used with synthetic or
   consented test audio. No "India resident" claim is made for Global Standard or UAE North.
3. Every provider call records adapter, model/deployment, endpoint and region on the job
   (`processing_job.usage`), so the processing location of each encounter is auditable.
4. Subprocessor terms to review: retention and abuse monitoring of prompts, no training on
   customer data, deletion on request, breach notification windows (India DPDP Act 2023 and the
   2025 Rules).

## Consequences

Deployment is blocked on item 1. The code needs no change to switch region: only
`SPEECH_REGION`, `SPEECH_ENDPOINT`, `FOUNDRY_ENDPOINT` and the deployment names.
