# Reviewer C — rebuttal

13 November 2026. Audit only. My original report is unchanged. For this round I read `reviewer-a-clinical-workflow.md` (findings A-01 to A-09 in full, the rest by heading), `reviewer-b-visual-interaction.md` (sections 2 and 11), and two concept screenshots (`concepts/screenshots/doctor-1440.png`, `desk-1440.png`). I did not re-run A's walkthroughs; I take A's observations as reported.

## 1. Behaviour first, or system first? — **Agree with A, with conditions**

A is right about order, and I ranked it wrong. My report put six system PRs before the pilot. A-02 (a free-text brand skips the allergy check), A-03 (stale note text), A-04 (end with an unauthorised draft) and A-06 (no undo) hurt a pilot clinic more than ten copies of `Message` do. None of them needs a design-system PR first; they use `Dialog`, `ClinicalAlert` and `Button`, which exist.

Only three of my system PRs are true prerequisites or near-free:

| System PR | Why before pilot | Effort |
|---|---|---|
| One sticky `PatientBanner` (my step 5, narrowed) | It *is* A-01, B-03 and C-02. Three reviewers found it independently. Two banners exist today (`features/encounters/components/IdentityBanner.tsx:17`, `packages/ui/src/primitives.tsx:139`); making one sticky without merging them hard-codes the divergent allergy look | M |
| Control defaults to 44px + codemod (step 1) | Mechanical, no behaviour change, removes about 350 overrides so the consult layout pass has a smaller diff | S |
| `label` required on `Table` | Keyboard scrolling on 4 tables; a type change | S |

Merged order:

1. A-02 free-text allergy acknowledgement (backend + frontend, clinician sign-off).
2. Sticky single banner on consult, its sub-pages and nurse intake, plus B-01 (sign bar covering fields) in the same PR.
3. A-03 and A-15 (note cache and flush).
4. A-04 end-consultation guard; A-10 pre-ticked identity.
5. A-06 undo and "left without being seen"; C-14 / A-23 offboard guard.
6. A-07 follow-up reaches the desk; A-08 escalation ordering; A-09 nurse complaint.
7. A-05 date of birth in picker rows.
8. Step 1 codemod, then the bounded consult layout pass (A-13, B 11.3).

After the pilot: my steps 2, 3, 4, 6–12 (`Card`, `Field` + error summary, tone tokens, navigation renames, type tokens, tables, settings sub-nav, dark mode). Exception: when a behaviour fix already touches a file, replace its local `Message` or `Field` in the same PR.

## 2. Feasibility of the arbiter's prototype — **Agree with conditions**

| Part | Maps to | New backend? | e2e exposure | Effort |
|---|---|---|---|---|
| One-row sticky patient strip | The banner PR above | No | Low; add one scroll assertion | M |
| Single account menu, 48px top bar | `AppShell.tsx:86-98`; needs shadcn `dropdown-menu` | No | 3 spec files, 10 locators for Sign out / Switch facility / My devices | S–M |
| 56px icon-only rail at 1440 | `AppShell.tsx:41` | No | 5 files use `getByRole('navigation')` | S to build; **I disagree with it** (section 3) |
| Three-region consult, section anchors, layout footer | `ConsultStudio.tsx` (185 lines) is composition; controllers stay | No | 13 spec files, 79 references to consult; J2 alone has 60 locators | M–L |
| Always-open chart panel | The Assist / Chart / Checklist tabs exist (`ConsultStudio.tsx:98`) | No | Low | S |
| "My queue" rail inside the consult | Today's data (`features/today`, `v1/front-desk/board`) | No for the list. **Yes** for a correct order: A-08 says the order is slot time, not arrival or escalation | New tests | M + backend |
| Prescription inline with a free-text add box | Would replace `AddMedicinePanel` and sit beside `SafetyChecks` | Depends | High | L. **Disagree before pilot** |
| Status-grouped desk list, counts as filters | `groupByColumn` (`front-desk/domain/board.ts:56`) already groups; counts derive client-side; `QueueTable.tsx` (52 lines) is already a sortable queue table used by the nurse queue and clinic overview | No | 19 spec files, 55 references to the board | M |
| One find-or-check-in box | Walk-in and booking pickers exist; merging them is new flow | Possibly (A-05 wants last visit) | Medium | M |
| Owner "Clinic today" | `ClinicOverviewPage.tsx` (179 lines), `v1/front-desk/overview`, `v1/schedule/metrics` cover live load | **Yes** for business figures (A-19: planned P4) | New | S for a live page, L with reports |

On the inline prescription: the concept's box reads "Add medicine (try amoxicillin)". A-02 shows free text is exactly where the safety net has a hole. An inline free-text entry makes the unsafe path the fastest one. Keep the structured picker until A-02 is closed and a clinician has seen the replacement.

**40px pointer rows / 48px coarse.** Compatible with my density model in principle (pointer decides), and simpler than mine: one media query instead of a `data-density` prop. I withdraw my 32px compact tier for now and accept 40 / 48 for **list rows**. Conditions: (a) AGENTS.md §4 says 44px targets, so the PR that introduces 40px must amend that rule in the same PR, as AGENTS.md itself requires; (b) buttons that commit a clinical or irreversible action stay 44px at any pointer type; (c) in-row buttons in the concept look about 30px tall, which meets WCAG 2.5.8 (24px) but needs 8px spacing and a tablet check, since A-17 already reports small targets on a tablet.

## 3. Where "no shell change" ends — **Agree with B; disagree with one arbiter choice**

My guardrail meant: do not rebuild the shell or the brand. I proposed the account menu myself (C-08), and a top bar of 48 or 56px is a number.

Inside the line: account menu, bar height, facility name in the bar, labelled 72px rail between 768 and 1279 (B 11.1), a user-controlled sidebar collapse. All are edits to one file that keep `navigation.ts`, the landmarks, the skip link and the breakpoint mechanism.

Outside the line before the pilot: an icon-only 56px rail as the **default at 1440**. C-09 and B's heuristic 6 both flag unlabeled icons as a recognition problem at tablet widths; the concept extends that to every width. Four line icons are not a navigation for a receptionist on day one. Collapsed by choice, yes. Collapsed by default, no.

## 4. Kanban to grouped list — **Agree with conditions: add it, do not replace**

Cost is lower than it looks: same `Board` object, same live feed (`boardLive.ts` is view-agnostic), grouping and a table component already exist. Risk is in tests (19 files) and in changing the desk's main surface days before real users arrive.

Proposal: before the pilot, keep the board and fix its chrome (B 11.2: first card near 200px instead of 452px; S–M). Land A-06 (undo, left without being seen) and A-11 (doctor on the card) first, so those actions are not built twice. After that, add the grouped list as a third option in the existing view switch (`FrontDeskViews.tsx`), and let the pilot receptionist's use decide the default. The list's density win is real: about 9 patients in the concept's first screen against about 3 cards per column today.

## 5. Is a reset warranted anywhere? — **Disagree, including for the consultation page**

A reset pays off when the foundation is wrong. All three reviews found it sound: 0 overflow pages, axe nearly clean, contrast tested, RTL working, layering lint-enforced. On the consult specifically, the hard parts (autosave with conflict detection, sign gating, safety checks, separate authorisation) live in hooks and domain code that A lists under "must not be broken". The page file is 185 lines of composition. Rearranging it into three regions is a layout pass, which A, B and the arbiter all describe in nearly the same terms. Call it that and keep the controllers and their tests.

## 6. Guardrails against re-drift

1. **Lint, same style as the existing table rule:** forbid `min-h-11` / `h-11` on `Input`, `SelectTrigger`, `Button`; forbid arbitrary colours everywhere; forbid raw `<h1>` and the string `border bg-card` in `features/**`; forbid `sm:grid-cols-*`.
2. **A ratchet script in CI** for what lint cannot express: count local `function Message|Field|Card|Empty` in `features/*/components` and fail when the count rises. Start at today's numbers (10, 5, 2, 2) and only let them fall. This is not weakening a guard; it adds one.
3. **One safety e2e assertion, mutation-checked:** after scrolling the consult to Prescription, the patient name and allergy text are in the viewport. Remove `sticky` and it must fail.
4. **Contrast test reads tone tokens**, not look tables, once tones exist.
5. **`/__ui` gallery** (`web/clinic/src/dev/UiGallery.tsx`) shows every library component in en and ar; a new shared pattern is not done until it is there.

## 7. Mobile and platform console before the pilot — **Explicitly nothing**

- **Expo apps:** not in the pilot flow (CLAUDE.md lists sign-up through offboard on the web app). Do nothing, except fix the Metro bundling defect (C-20) if anyone needs to open the apps. Do not port web consolidation to `ui-native`; the borrowing goes the other way later (`Tone`).
- **Platform console:** Medvya staff only, desktop, axe-clean at 1440 in my mocked captures. It inherits the control-default fix for free. The 375px sign-in overflow and 30px filter tabs (C-22) can wait.

## Vote and non-negotiables

**Vote: still B as the destination, but sequenced A-first.** Before the pilot: A's behaviour fixes, three system PRs (banner, control defaults, table label), B's chrome fixes on the board and one bounded consult layout pass. The rest of B after the pilot. I have changed my mind on the order, not on the direction: A's own list contains two system items (the pinned strip, the consult pass), and without consolidation afterwards the next ten features recreate the drift.

Non-negotiables:

1. A-02 is closed, with a clinician's sign-off, before any pilot doctor prescribes. No new free-text prescribing shortcut until then.
2. One patient banner, sticky on every screen that writes to a record, with the allergy wording and look approved by a clinician and guarded by a test that fails when the pin is removed.
3. No palette, font, icon-only default navigation or second component library. Brand and shell structure stay.
4. Pattern fixes land in `packages/ui` with a lint rule or ratchet in the same PR. No new local `Message`, `Field` or `Card`.
5. Controls that commit clinical or irreversible actions stay 44px (48px coarse). Any 40px rule amends AGENTS.md in the same PR, and J1–J6 stay green on every PR.
