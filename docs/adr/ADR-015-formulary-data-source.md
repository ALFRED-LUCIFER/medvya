# ADR-015 · Formulary data source and licence per country

- **Status:** Accepted for the pilot starter list, 9 Nov 2026. **The licensed national registers are not chosen yet** (needs a person: licence terms and a clinical or regulatory advisor). Ticket: MDV-P2-014 (dependency 1, Q-04).
- **Context:** The prescription composer (MDV-P2-015) needs a country formulary: generic name, strength, form, brands, national code, regulatory class (India Schedule H / H1 / X, UAE controlled), ATC code and a default sig. Medvya must not invent clinical data, and a commercial drug database cannot be copied into a migration without a licence.

## Decision

1. **Seed only data that carries no licence risk.** Generic names (INN) are non-proprietary. Strengths and forms are common dose facts. ATC codes are taken from the WHO ATC index (identifiers only, no text from it). Brands and national codes are **left blank** in the seed: they are trademarked or licensed registers, and a blank stays blank (AGENTS.md 1.5).
2. **The starter list is small and marked.** 22 common medicines per country (`FormularySeed`), loaded by the migration as rows with no tenant and source `Starter`. Every clinic of that country reads them; none can change them (row-level security refuses any write to a row without the connection's own tenant). The classes in it are **placeholders**: a clinical or regulatory advisor must confirm each before a clinic relies on it for printing rules. The Settings screen says so.
3. **Clinics add what they need.** A practice admin adds a medicine (validated by the country's rules, audited, marked "clinic-added"). It belongs to that clinic only. Brand names come the same way (MDV-P1-088): the admin pastes brand rows in bulk, each naming a medicine already listed with an ATC code and the allergy class its reviewer expects. The server adds all rows or none, refuses a row whose class differs from the safety rules, and never maps a brand by guess. A brand is a name on the generic's row, so it is checked exactly as the generic is. Each clinic's mapping still needs a named clinical reviewer.
4. **Licensed registers come later, by a new migration.** Candidates, to be checked for licence terms before any import: for India the CDSCO approved-drug lists and the National List of Essential Medicines (government publications); for the UAE the MOHAP and DHA drug registers (drug codes). Nothing is imported until the licence and the redistribution terms are recorded here in a superseding ADR.
5. **Where the rules live.** Which classes a country has, what a printed prescription must show for each, and the national-code shape are in `FormularyRules` (one subclass per country). The seed and the API never branch on the country code.

## Consequences

- The pilot formulary is a safe starting point, not a clinical reference. A doctor can always prescribe a medicine that is not listed as free text, flagged for review (MDV-P2-015).
- Print requirements are stable codes (`warning_schedule_h`, `pharmacist_keeps_copy`, ...). Their exact wording on paper needs regulatory sign-off before the Rx print (MDV-P2-017).
- A person must: (a) have a clinical advisor confirm the starter classes, and (b) decide and sign the licensed source per country. Both are listed in `docs/05-delivery/session-handoff.md`.
