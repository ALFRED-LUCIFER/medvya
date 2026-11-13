# 00 · Executive verdict

Medvya product UX audit, 13 November 2026. Audit only: no application code was changed.

## The verdict

**Medvya is a careful product with a rough working surface. It is not yet safe to prescribe from in a pilot, because of one gap, and it is not yet pleasant to use all day, because of layout.**

Would a real clinic team prefer to use it all day?

- **Receptionist: close.** A new walk-in gets a token in 6 clicks and 3 typed fields, with no scrolling. What is missing is the way back: nothing can be undone.
- **Doctor: not yet.** A simple visit takes about 24 clicks, 5 page changes and several screens of scrolling; the patient's name and allergy scroll out of view while prescribing; and a brand-name medicine typed as free text was authorised for an allergic patient with "No alerts".
- **Nurse: workable on a tablet** when nurse intake is switched on, with the same identity problem.
- **Owner: has nothing to look at** beyond a live queue and four pilot numbers, one of which is always perfect.
- **Administrator: adequate.** Settings is complete for a pilot and reasonably grouped.

No clinician, nurse, receptionist or owner took part. Every judgement here is the auditors'.

## Recommended strategy

**Option B, cohesive modernisation, delivered safety-first.** Keep the brand, the routes and the logic. Close the safety and trust gaps as small separate changes; then do three system changes and one bounded redesign of the consultation; leave the rest of the design-system work until after the first pilot weeks.

**A full reset is not warranted**, for the whole product or for any one screen. The base is sound: on current `main`, 615 captured pages have no sideways overflow and 610 pass axe with no violation.

Three reviewers worked independently. Two voted for B and one for surgical fixes only; after reading each other, all three chose B in the clinical reviewer's order ([06](06-three-independent-reviews.md), [07](07-debate-and-arbitration.md)).

## Top systemic weaknesses

1. **Safety checks cover the planned path, not the likely one.** The allergy check works for formulary picks and is skipped for free text; the formulary has 23 generics and no brands (F-01).
2. **The patient is not always in view.** Identity and allergy scroll away on the consultation and nurse intake (F-02).
3. **Work only moves forward.** No undo at the desk, no reopening an ended consultation, no warning before ending with an unauthorised prescription (F-04, F-05).
4. **What one role decides does not reach the next.** The follow-up, the nurse's complaint and an escalation do not arrive where they are needed (F-06, F-07, F-16).
5. **The screen sometimes says more than happened.** "Identity confirmed", a stale note, a "0 s" metric (F-03, F-09, F-29).
6. **Pages spend the first screen on chrome.** 3 % of a tablet screen is working content on the consultation, 41 % on the reception board (F-11, F-12).
7. **Patterns are rebuilt in every feature.** Three patient headers, 20 view switches, about 350 size patches, 12 chip styles (F-19, F-20).
8. **What is deployed is behind what is built.** The Azure dev header breaks at tablet and phone widths; `main` does not (F-13).

## What is already good

Measured, not assumed:

- The walk-in path, phone-digit search, the same-day second-visit warning and the duplicate block at registration.
- Allergy handling: "not recorded" is never shown as "none"; a formulary medicine in the allergy's class is blocked with a logged override.
- Signing: identity restated, read-only afterwards, addenda as new signed versions.
- Conflict detection when two people edit one note.
- The waiting-room display shows tokens only.
- Every role tested is refused correctly by the server.
- Accessibility floor: axe nearly clean, text contrast passes, visible focus, a working skip link, status never by colour alone, right-to-left in place.
- Honest empty states: "No data yet", not zeros.
- The engineering base: tokens, a layered front end enforced by lint, three languages, tests against a real database.
- The visual identity (navy, teal, Manrope, quiet white surfaces) is credible for a clinic. It needs composition, not replacement.

## What must not be changed without validation

- Allergy rules, wording and the unknown / none / not recorded distinction.
- The Add medicine dialog and the safety-check wording ("not checked" is honest and should stay).
- The sign dialog; the separation of sign, authorise and place orders.
- The duplicate block and the identity confirmation at check-in.
- The walk-in path: it must not get longer.
- Consultation section order and required fields; vitals ranges and flags.
- What a receptionist can see of clinical data.
- The palette and typeface.

## Scores

Provisional throughout: synthetic data, emulated devices, no real users.

| Area | Score | Evidence | Coverage |
|---|---|---|---|
| Workflow efficiency | **15 / 25** | Desk paths are short (6, 3 and 2 clicks). The consultation takes about 24 clicks, 5 route changes and 4 to 6 screens of height. No undo | Good on desktop and tablet in English. Phone and Arabic not walked |
| Clinical safety and user trust | **10 / 20** | Strong allergy, signing and conflict rules. Against: F-01 reproduced end to end, identity not pinned, draft prescription at End, pre-ticked identity, stale note | No clinician reviewed it. Interactions, paediatric dosing, real step-up untested |
| Information architecture | **8 / 15** | Roles see sensible menus and Settings is grouped. Against: one word for several screens, a refused screen says "not found", diagnoses and medicines on other pages | Seven roles on `main`; two on Azure |
| Visual and design-system quality | **8 / 15** | Good tokens, colour semantics and restraint. Against: 12 type sizes for a spec of 7, patterns rebuilt per feature, raw colours in the shared library | Code counts and about 55 screens by eye |
| Responsiveness and tablet usability | **5 / 10** | No overflow on `main` at five widths. Against: the first-screen shares above, an unlabelled rail, a phone board that does not work, Azure broken at 768 and 375 | Emulated viewports only |
| Accessibility | **7 / 10** | axe clean on 610 of 615 pages, contrast, focus, reflow. Against: focus hidden under the sign bar, errors that do not take focus, small switches | Automated and partial manual. Not a conformance audit; no screen reader |
| Error handling and reliability | **3 / 5** | Autosave, conflict notice, honest empty states. Against: stale text, a lost last edit, no undo, "Loading…" jumps | Offline and double submission untested |
| **Total** | **56 / 100** | | Provisional |

The safety score is one below the clinical reviewer's 11, because the free-text gap was confirmed end to end after that score was given.

## The smallest safe first batch

Tickets from [10](10-prioritized-implementation-backlog.md). Not started; do not implement until instructed.

1. **UX-01** A free-text medicine needs a reason when the patient has, or may have, an allergy. With **UX-02** started alongside: the pilot doctor's brand list.
2. **UX-03** One pinned patient strip; the sign bar becomes a footer.
3. **UX-04** The note always shows what the server holds.
4. **UX-06** The checklist reports only what happened.
5. **UX-12** Redeploy Azure dev from `main`.

Before any of these: the questions for the pilot doctor in [11](11-pilot-doctor-validation-plan.md), rows 1 to 5.

## Motion, since it was asked

**No animation is needed now.** Later, about six small functional uses (a truthful "Saving… / Saved" status, an "Updated" mark on live board changes, skeletons, a tablet drawer). Never: page transitions, sliding cards, anything that moves while a doctor types. Details in [08, section 12](08-design-system-blueprint.md#12--motion).

## Mobile apps and the platform console

Both were reviewed from code; the console also through a mocked API. The mobile apps could not be started (a bundling error, F-34) and hold development-only data by design. Neither needs design work before the pilot, which runs on the web app.

## Where everything is

| File | Contents |
|---|---|
| [01](01-scope-environments-and-coverage.md) | Environments, tools really used, coverage, what was blocked, side effects |
| [02](02-route-role-state-inventory.md) | Routes, roles and states |
| [03](03-persona-workflow-walkthroughs.md) | Task paths with counts |
| [04](04-evidence-backed-findings.md) | 47 findings with evidence |
| [05](05-accessibility-responsive-and-reliability.md) | Responsive, accessibility and reliability results |
| [06](06-three-independent-reviews.md) | The three reviews and how they were run |
| [07](07-debate-and-arbitration.md) | Disagreements, rulings, red-team critique |
| [08](08-design-system-blueprint.md) | The design-system blueprint, including motion |
| [09](09-redesign-options-and-recommendation.md) | Options A, B, C and the recommendation |
| [10](10-prioritized-implementation-backlog.md) | 35 proposed tickets |
| [11](11-pilot-doctor-validation-plan.md) | What the pilot doctor and staff must confirm |
| [12](12-prototype-guide.md) | The concept and how to view it |
| [concepts/medvya-workspace-concept.html](concepts/medvya-workspace-concept.html) | The standalone concept |
| `screenshots/`, `evidence/`, `tools/`, `reviews/` | Evidence, metrics, the capture and walkthrough scripts, the reviewers' full reports |
