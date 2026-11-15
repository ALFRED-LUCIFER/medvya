# Provider notes (Azure Speech fast transcription, Azure AI Foundry)

Verified against Microsoft Learn on **2026-11-08** (WebFetch). Anything not confirmed there is marked
**UNVERIFIED**. Adapters stay behind the `Transcriber` / `LLMClient` Protocols, so a doc change is a
one-file edit.

## 1. Azure Speech fast transcription

Sources:
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-speech-to-text
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support

| Item | Documented fact | Plan said | Used |
|---|---|---|---|
| Operation | `POST {endpoint}/speechtotext/transcriptions:transcribe?api-version=2025-10-15` | same | same |
| api-version | `2025-10-15` is "the latest version that's generally available"; `2024-05-15-preview` will be retired; `v3.x` retired 2026-03-31 | `2025-10-15` | `2025-10-15` (`SPEECH_API_VERSION`) |
| Host | Examples use the custom-subdomain resource endpoint `https://{resource}.cognitiveservices.azure.com`; portal "Keys and Endpoint" gives the value | `{region}.api.cognitive.microsoft.com` | `SPEECH_ENDPOINT` when set; else `https://{region}.api.cognitive.microsoft.com` (verified live 2026-11-09: `https://centralindia.api.cognitive.microsoft.com` accepts fast transcription with a key and WebM/Opus; `uaenorth` returns 400 "Fast transcription is not supported in this region") |
| Auth | `Ocp-Apim-Subscription-Key: <key>` or `Authorization: Bearer <Entra token>` | key | key header |
| Request | `multipart/form-data`: `audio` (file) and `definition` (JSON string). Audio by `audioUrl` inside `definition` is also possible and recommended for long files | multipart | multipart with inline audio (one segment per call, bounded by `SEGMENT_MAX_MS`) |
| `definition` | `locales: [..]`, `diarization: {enabled, maxSpeakers}`, `channels: [..]`, `phraseList: {phrases:[..]}` (2025-10-15), `profanityFilterMode: None\|Masked(default)\|Removed\|Tags` | locales + diarization | locales + diarization + `profanityFilterMode: "None"` (clinical text must not be masked) |
| `maxSpeakers` | Docs (Java/Python samples) state range **2 to 35** | 3 | `SPEECH_MAX_SPEAKERS` (default 3, valid) |
| Diarization | mono audio only; stereo with diarization cannot set `channels: [0,1]` | n/a | browser MediaRecorder output is mono, no `channels` sent |
| Response | `{durationMilliseconds, combinedPhrases:[{channel,text}], phrases:[{channel, speaker, offsetMilliseconds, durationMilliseconds, text, words[], locale, confidence}]}` | same | mapped by `azure_fast.map_response` |
| `speaker` | **integer** (sample shows `1`), only present with diarization | string implied | stored as `Speaker {n}`; missing -> `Speaker 1` |
| `confidence` | float 0..1 per phrase | same | stored in `transcript_segment.confidence` |
| Formats | WAV, MP3, OPUS/OGG, FLAC, WMA, AAC, ALAW/MULAW in WAV, AMR, **WebM**, SPEEX | WebM unverified | **WebM is documented as supported -> no ffmpeg remux implemented** (see 1.2) |
| Limits | **< 5 hours and < 500 MB** per file | ~2 h / 300 MB | 60 min segments (~30 MB opus) are far inside the limit |
| Throttling | HTTP 429 possible; recommended retry: up to 5 times, 2/4/8/16/32 s, on 429 and 500/502/503/504 and network errors; do not retry 400/401/422 | n/a | implemented in `AzureFastTranscriber` (injectable sleep for tests) |
| Locales | `hi-IN`, `en-IN`, `mr-IN` all marked supported for fast transcription (language-support page) | hi-IN + en-IN | `SPEECH_LOCALES=hi-IN,en-IN` |

### 1.1 Plan deltas that matter

1. **Language identification picks one main locale per audio file.** Docs: "designed to identify one main
   language locale per audio file ... If you need to transcribe multi-lingual contents ... consider
   multi-lingual transcription". With `locales=[hi-IN,en-IN]` a 60-minute segment gets one dominant locale
   per request, not per phrase. Real Hinglish code-switching therefore relies on the normalise step
   (LLM turns romanised/mixed text into English). Omitting `locales` selects the multi-lingual model, but
   its published locale list has `en-IN` and **not** `hi-IN`, and it "always outputs en-US". Decision:
   keep explicit candidates (plan A3); measure on the 30-conversation pilot set. Follow-ups (not built):
   shorter segments, Azure LLM Speech enhanced mode (same endpoint, `locales` optional, multilingual),
   per-phrase re-identification.
2. **Limits are looser than the plan assumed** (5 h / 500 MB), so rotation at 60 min is a latency/blast-radius
   choice, not a hard requirement.
3. **Diarization speaker numbers are per request (per segment).** "Speaker 1" in segment 2 is not guaranteed
   to be the same person as "Speaker 1" in segment 1. The clinician fixes this with the speaker relabel
   endpoint (B6). Cross-segment speaker linking is a follow-up.

### 1.2 WebM and the ffmpeg fallback

WebM (Opus) is documented as supported, and a concatenation of MediaRecorder timeslice chunks is a valid
WebM stream (the first chunk carries the header), so the adapter sends the bytes as-is. **UNVERIFIED live**:
no real Speech resource was called (no credentials here). If a live call rejects the audio (HTTP 400), the
documented fallback is to remux in the worker image before the call:

    ffmpeg -hide_banner -loglevel error -i in.webm -vn -ac 1 -ar 16000 -c:a pcm_s16le out.wav

The `ffmpeg` binary would then have to be installed in `src/AiService/Dockerfile` (B9). Not implemented now to
avoid shipping an unexercised code path; the `live` pytest marker (off by default) is where this gets proven.

## 2. Azure AI Foundry (Azure OpenAI v1 API)

Sources:
- https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle
- https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs

| Item | Documented fact | Used |
|---|---|---|
| Client | Standard `openai.OpenAI(...)` / `AsyncOpenAI`, not `AzureOpenAI` | `AsyncOpenAI` |
| `base_url` | `https://{resource}.openai.azure.com/openai/v1/` (also `https://{resource}.services.ai.azure.com/openai/v1/`) | `FOUNDRY_ENDPOINT` normalised to end in `/openai/v1/` |
| `api-version` | Not required with v1 | not sent |
| Key auth | `api_key=<key>` (header `api-key` on REST) | `FOUNDRY_API_KEY` |
| `model` | the **deployment name** | `FOUNDRY_DEPLOYMENT_EXTRACT` / `_NORMALISE` |
| Structured output (Python) | `client.chat.completions.parse(model=..., messages=..., response_format=PydanticModel)` (docs show `client.beta.chat.completions.parse`; in `openai` 3.x `chat.completions.parse` is the non-beta form, present and verified at install) ; REST: `response_format = {type: json_schema, json_schema:{name, strict:true, schema}}` | `chat.completions.parse` |
| Schema limits | all fields `required`; `additionalProperties:false`; optional = union with `null`; <= 100 object properties, <= 5 nesting levels; **unsupported**: `minLength maxLength pattern format minimum maximum multipleOf minItems maxItems uniqueItems ...`; root cannot be `anyOf`; `$defs` and recursion supported | `extraction/schemas.py` uses `extra="forbid"`, no defaults, no `Field` constraints; a unit test enforces it |
| Models with structured outputs | gpt-4o (2024-08-06, 2024-11-20), gpt-4o-mini, gpt-4.1 / mini / nano, gpt-5 family, o-series ... | pick via deployment |
| Package | `openai` 3.46.0 installed (latest on 2026-11-08; pulls `httpx2` as its HTTP layer) | `uv add openai` |

No `temperature` is sent (2026-11-09): gpt-4.1 / gpt-4o are Legacy in the Foundry catalog, and the current
reasoning models (gpt-5.x, gpt-6) reject a non-default `temperature`. Hallucination control is the strict
JSON schema plus quote verification, not sampling temperature.

Region/residency: model and Speech availability in Central India is not verified here (plan "Open" item).

## 3. Pipeline behaviour worth knowing

- Retry: Speech adapter retries 5x (2..32 s) on 429/5xx/network; Foundry adapter the same (SDK retries
  disabled so the policy is ours and testable); the job queue retries whole jobs 5x with exponential
  `run_after` (30 s * 2^(n-1), capped at 1 h).
- Job `last_error` stores only an error code / exception class name, never provider response bodies.
- Evidence check uses the **segment index** (`segment_idx`, the transcript row `idx`) in prompts and schemas,
  not the UUID: models copy small integers reliably and mangle 36-char UUIDs.
