# ADR-017 · Diagnosis code set and version per country

- **Status:** Accepted for the pilot starter list, 10 Nov 2026. **The licensed full code sets are not chosen yet** (needs a person: WHO licence terms for India, and the ICD-10-CM edition the UAE payers require). Ticket: MDV-P2-010.
- **Context:** Doctors code diagnoses on the problem list (E-07). A code must come from a known set, and the set and its version must be stored with every coded problem, so a later claim (P5) or export can tell exactly what was meant. India records WHO ICD-10. UAE claims (DHA eClaimLink, DOH Shafafiya) are coded in ICD-10-CM. Neither full classification can be copied into the repository without checking its licence, and the WHO text is copyrighted.

## Decision

1. **Where the choice lives.** `DiagnosisCodeRules` (one subclass per country, `src/Modules/Encounters/Medvya.Encounters.Domain/DiagnosisCodes.cs`) returns the code set a country codes from. Nothing else branches on the country.
2. **What is shipped now.** One curated starter list of about 90 common primary-care codes, system `ICD-10`, version `2019-medvya-starter-1`. The codes are identifiers from WHO ICD-10 (2019); every description is Medvya's own short wording, not WHO text. India and the UAE both use it for the pilot.
3. **Every coded problem stores the system and version.** A database check makes the system and version travel together, and only with a code. A doctor who cannot find the right code leaves the code blank and writes the problem in words: a code is never guessed (AGENTS.md 1.5).
4. **Search runs in the browser** over the country's list (`GET /v1/diagnosis-codes`), so a search term never reaches a URL, a log or the server. It is a linear scan, tested under 300 ms over 15,000 codes.
5. **Provisional is never exported as confirmed.** Only a `Verified` problem counts as confirmed (`ProblemRow.Confirmed`). The referral letter marks a provisional problem "Provisional, not a confirmed diagnosis" and leaves resolved ones out. P5 claims must take only confirmed diagnoses.

## Consequences

- The starter list is a safe start, not the classification. A clinic that needs a code that is not listed records the problem uncoded.
- **Before P5 claims in the UAE**, the UAE subclass must return a licensed ICD-10-CM set under its own system name and version (`ICD-10-CM`, the payer's edition). Problems already coded keep the system and version they were coded from; nothing is recoded in place.
- **Before the full WHO list is used in India**, someone checks the WHO licence for commercial use and records it here in a superseding ADR. Loading it is a new version string, never an edit of `2019-medvya-starter-1`.
- A person must decide (a) the WHO ICD-10 licence for India and (b) the ICD-10-CM edition and source for the UAE. Both are listed in `docs/05-delivery/session-handoff.md`.
