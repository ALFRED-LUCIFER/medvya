# Live run: three-speaker Hinglish test recording (synthetic script)

> Imported from the original HealthOps repository on 2026-11-16 (MDV-P3-028). Paths such as `apps/api` refer to that repository; the code now lives in `src/AiService`.

Recording: `Medvya AI_Doctor_Patient_Companion_Test_Script.pdf`, 8 min 18 s, one microphone, read by
the project owner's team on 2026-11-09. Providers: Azure Speech fast transcription (Central India,
`hi-IN,en-IN`, diarization max 4), Foundry `gpt-5.5` (normalise + extract). No patient data.

## Finding 1: diarization merges patient and companion (provider limit)

Re-transcribing the same audio with `maxSpeakers` 3 and 4 and with `en-IN` only always returned
two clusters; every daughter line sat in the patient's cluster. A `phraseList` of drug names did not
change "amalodiapin" / "metamorphin". Consequence: speaker roles must come from content and be
confirmed by the clinician (migration 0007, `normalise` role suggestions, apply-suggested-roles).

## Finding 2: Azure content filter refuses parts of a clinical transcript

Output-filter refusals (`ContentFilterFinishReasonError`) and input-filter 400s
(`ResponsibleAIPolicyViolation`) occurred non-deterministically; a bisect found no phrase refused on
its own. The adapter maps both to `LLM_CONTENT_FILTER` (never retried) and `normalise` splits the
batch, so three phrases were kept as heard instead of failing the session. Action for the owner:
configure a custom content filter on the deployment (prompt-shield annotate-only, relaxed
categories for medical text).

## Result after the changes (replay of the stored transcript, live model)

| Measure | First run (old pipeline) | New pipeline |
|---|---|---|
| Speaker roles | Speaker 1 / Speaker 2 only | Speaker 2 split: 35 patient, 38 companion, 3 doctor, 3 unknown; 117 phrases applied, 18 below 70% left for the clinician |
| Scripted companion lines attributed to companion | 0 / 11 | 11 / 11 |
| Drug names in English view | amalodiapin, metamorphin, amoxylicin | amlodipine, metformin, amoxicillin (original kept) |
| Facts / rejected by gate / uncertain | 31 / 0 / 7 | 34 / 0 / 5 |
| Metformin (wife's) in patient facts | no | no, and flagged in "to confirm" |
| Corrections honoured | 100.4 °F | 100.4 °F, nausea twice, vomiting once, 2 kg over 3 months, dysuria last month |
| Pertinent negatives | diarrhea | chest pain, diarrhea, current dysuria, antibiotics taken and prescribed, travel, sputum |
| Summary | 5 sentences | 10 sentences incl. caveats, history, medication, allergy, decisions, safety advice |
| LLM usage | n/a (old worker) | 11 calls, 18 116 prompt + 15 829 completion tokens |

Known remaining errors come from the speech layer: "Yes, little breathing problem" (spoken: "Breathing
problem, no"), "No gala thora Kharab" (spoken: "Gala thoda kharab hai"), one "42.9" noise phrase. The
extraction marks breathlessness uncertain and lists the garbled phrases for confirmation rather than
asserting them. A clinician review of this run is still required before any score is claimed.
