# ADR-019 · Medvya subscription pricing: three plans, per-doctor Clinic price, 14-day trial, plans as versioned data

- **Status:** Decided. Prices approved by the owner on 6 Nov 2026 (Medvya Web design, decision record "Approved 06 Nov 2026"); limits and defaults decided on 15 Nov 2026 (plan on epic #774). Settles the open decision "Pricing model" of MDV-P5-016.
- **Tickets:** MDV-P5-016 (this record), MDV-P5-042 (versioned plans in the console), MDV-P5-043 (subscriptions, invoices, dunning), MDV-P5-050 (public pricing page), MDV-P5-065 (collecting payments).
- **Context:** MDV-P5-016 asks for the pricing decision to be recorded before any subscription code. Until now the only plan is the hard-coded starter plan of ADR-016 (1 doctor and 2 other staff, `StarterPlan.cs`). The public site shows the approved prices from `src/Host.Api/PublicSite/pricing.json`. ADR-016 decided that the subscription belongs to the organisation and that seats are counted per organisation across its locations.

## Decision

1. **Three plans.** Prices are per month or per year, excluding GST (India, 18 %) and VAT (UAE, 5 %). Yearly is ten times the monthly price (two months free).

   | Plan | India | UAE | Doctors | Staff | Locations | Medvya Assist (AI notes) | Trial |
   |---|---|---|---|---|---|---|---|
   | Solo | ₹1,999 a month | AED 349 a month | 1 | 2 | 1 | Add-on, ₹999 (AED 149) per doctor a month | 14 days |
   | Clinic | ₹3,499 per doctor a month | AED 599 per doctor a month | Per doctor | 15 included | Several | Included, 300 visits per doctor a month | 14 days |
   | Hospital | Custom, from ₹2,50,000 a year | Custom, from AED 40,000 a year | Unlimited | Unlimited | Several | In the contract | None (talk to us) |

   Solo is a one-doctor organisation, not an individual account (ADR-016). Hospital adds single sign-on and HIS integration.
2. **Trial:** 14 days with every feature of the plan, no card needed.
3. **What counts toward a limit:** doctors, other staff and locations, **per organisation in total across its locations** (ADR-016). **Patients are not counted** (this narrows ADR-016 point 5, which listed patients as seats).
4. **Defaults, all editable later as plan data, not code:**
   - Staff beyond the included count: the owner sees "Talk to us"; there is no price for an extra seat yet.
   - Medvya Assist above the monthly quota warns; it never blocks a doctor.
   - A feature not yet assigned to any plan is included in every plan.
   - Medvya Assist is an add-on on Solo and included, with its quota, on Clinic and Hospital. The Solo add-on uses the same 300-visit quota until a different one is decided.
5. **Plans are versioned data in the platform control plane** (`platform.PlanVersions`, MDV-P5-042), never code: code, version, prices per country as integer minor units with the ISO currency (AGENTS.md 3.4), the limits above (empty means unlimited), trial days and the feature keys included. An edit in the console creates a new version; an existing version is never changed (the database refuses it). Version 1 is seeded from `pricing.json`, the approved price list.
6. **Who may change a plan:** Owner and Finance in the console, with a reason, logged in the platform audit log. Operations, Support and Reviewer read only (server-side policy).
7. **Price changes:** new subscriptions take the new version at once; existing subscribers move at renewal with 30 days' notice (MDV-P5-043, when subscriptions exist).
8. **Payment:** no payment provider now. Medvya's own invoices and a "mark paid" by Finance come first (MDV-P5-043); collection through the Razorpay (MDV-P4-007) and UAE (MDV-P4-008) adapters follows in MDV-P5-065, after the merchant accounts (MDV-P4-032).
9. **Non-payment never locks patient records** (grace ladder of the 6 Nov decision: day 3, 7, 14, 30 limited, 60 read-only, 90 suspended; clinicians can always read and treat), MDV-P5-043.

## Consequences

- Until MDV-P5-050, `/pricing` still reads `pricing.json`. A price changed in the console must also be changed there until then (`shortcut:` in `pricing.json`).
- Until the per-country write path of MDV-P5-041 exists, plan entitlements are not pushed to the clinic databases and `StarterPlan.cs` keeps enforcing 1 doctor and 2 staff for every organisation (`shortcut:` there and in MDV-P5-042).
- The feature keys on a plan are plain strings. They are checked against the code feature list once MDV-P5-041 lands it.
- Money in INR and AED is never summed (MDV-P5-043 reports them separately).

## Alternatives considered

- **Per-practice flat price for every size:** simple, but a ten-doctor clinic would pay what a one-doctor clinic pays. Rejected in the 6 Nov decision.
- **Patients counted as seats** (ADR-016 point 5): penalises a busy clinic for the thing Medvya is meant to help with, and is hard to explain. Not chosen on 15 Nov.
- **Plans in code or configuration** (like `StarterPlan.cs` and `pricing.json`): every change would need a deploy and leave no versioned record of what a subscriber agreed to. Rejected.
