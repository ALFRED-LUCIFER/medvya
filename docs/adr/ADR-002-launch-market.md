# ADR-002 · Launch market

- **Status:** Proposed. **Founder decision needed in P0 week 1** (ticket MDV-P0-003).
- **Date:** 2026-10-20

## Context

- The Gap Analysis advises launching in one country first and adding the second after the first is paying. The Build Plan agrees: one country and one segment.
- UAE launch blockers are regulator-controlled: exchange connection (Malaffi/NABIDH/Riayati), Emirates ID reader, eClaimLink/Shafafiya.
- The India path has no regulator gate for the first paying clinic.
- The design partners and synthetic cast are India-heavy (Chennai, Kochi). The UAE is fully designed, including Arabic RTL.

## Options

1. **India first** (PRD v2 default): pilot in India. Build the UAE pack in P5 while regulator onboarding runs in parallel.
2. UAE first: higher price point, but go-live depends on regulator dates.

## Proposed decision

India first, for the P1–P4 pilots and the first paying clinic. Start UAE regulator applications in P1 (PRD risk mitigation) so the UAE pack (P5-04) is ready when onboarding completes.

## Consequences

- P0 Bicep deploys **Central India** first. The UAE North parameter files exist but are not deployed until the UAE pack.
- India-specific work comes first: DLT SMS registration (P1), Razorpay (P4), GST invoice formats (P4).
- If UAE is chosen instead: EID card reader and the launch-emirate exchange move to the critical path for the first paying clinic (week 29). The PRD notes this can delay that milestone.
