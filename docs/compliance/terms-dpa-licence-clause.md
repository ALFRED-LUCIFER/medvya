# Terms and DPA: the organisation answers for the licences it attests (MDV-P1-067)

**DRAFT. Not approved for real clinics.** [LEGAL: counsel to confirm] Written sign-off from counsel (MDV-P1-044, MDV-P2-022) must exist before a real pilot clinic accepts this version. The Hindi and Arabic text also needs native and legal review.

Version: `2026-11-draft-2` (`TermsPolicy.CurrentVersion`). Bumping it makes every owner accept again through the terms gate (booking and messaging only; set-up stays open). The strings are `settings.terms.licenceClause` and `doctors.licence.reminder` in `packages/i18n/messages/{en,hi,ar}.json`.

## Clause (plain language, English source)

1. Medvya approves the organisation. It does not check individual doctors, their licences or their qualifications.
2. The organisation is responsible for every doctor and staff member it adds, and for attesting each doctor's licence (number, authority, expiry) through the owner's attestation.
3. If an attestation is false or the licence has lapsed, the organisation answers for it, not Medvya.
4. Medvya may suspend an organisation.
5. A Hospital signs the DPA before approval (ADR-016).

## Reminder shown at each licence attestation

> Your organisation answers for this licence. Medvya does not check it. A false attestation is the organisation's responsibility.

## For counsel

Review the Clinic and Hospital document lists too (ADR-016). Remove the draft marker and the "draft" notice (`settings.terms.draft`) only after sign-off.
