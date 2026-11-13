# 12 · Prototype guide

**File:** [concepts/medvya-workspace-concept.html](concepts/medvya-workspace-concept.html)

One self-contained HTML file: styles, script and the Manrope font are inside it. It makes no network request and talks to no backend. All names and values are synthetic.

It is a design proposal to react to. It is not a working record system, it has not been used by a clinician, and nothing in the application was changed.

## How to view it

Open the file in a browser (double-click it, or `open docs/product-ux-audit/concepts/medvya-workspace-concept.html`).

The dark bar at the top is not part of the product. It holds:

- **Doctor / Receptionist / Owner**: switches the view. The rail icons do the same.
- **Show audit notes**: numbered violet markers. Hover or focus one to read what changed and which finding it answers.
- **Touch sizing**: forces the 48 px rows and 44 px controls that a touch device gets automatically.

Resize the window to see the tablet layouts: under 1180 px the doctor's queue becomes a "Next" button; under 860 px the chart panel becomes a drawer; under 560 px the rail becomes bottom tabs.

To rebuild it after an edit, change `medvya-workspace-concept.src.html` and run `python3 -I docs/product-ux-audit/tools/concept-build.py` (the only difference between the two files is the embedded font). `node docs/product-ux-audit/tools/concept-capture.mjs` renders the previews again.

## Why these three screens

The evidence put the most cost and the most risk in the consultation (F-01, F-02, F-10, F-11), then the front desk (F-05, F-08, F-12), and showed the owner has nothing to look at (F-29). So the concept is a consultation workspace first, with the desk and the owner page as smaller views.

## What to try

**Doctor**

1. Scroll the note: the patient strip and the allergy stay.
2. Type in Examination, Assessment and Plan: the section marks and the footer count update; the save status changes.
3. In "Search the formulary", type `Amoxicillin 500 mg` and press Add: blocked, with the reason in words.
4. Type `Augmentin 625 Duo` and press Add: the free-text acknowledgement names the allergy.
5. Add `Amlodipine 5 mg`: added.
6. Press "N to finish before signing": the checklist opens. Press Resolve.
7. Choose a follow-up, then "Review and sign": the dialog restates the patient. Sign: the note becomes read-only.

**Receptionist**

1. Type `90000` in the search box: two patients with date of birth and last visit.
2. Press Check in: it asks to confirm the date of birth. Press again: the row moves, with a token.
3. Press "Undo last move".
4. Use the status counts to filter.

**Owner**

Read the page. Nothing is interactive beyond the two filters.

## What each change answers

| Note | Proposed change | Findings |
|---|---|---|
| 1 | A 48 px top bar with clinic, search, alerts and one account menu; a rail with a label under every icon | F-18, F-22 |
| 2 | One patient strip that never scrolls away, the same on every clinical screen; the allergy as a solid labelled block | F-02, F-19 |
| 3 | The doctor's queue by token with escalations first and unfinished work listed. Tokens and ages only | F-07, F-04 |
| 4 | One scrolling note with section anchors and completion marks; diagnoses inline | F-11, F-03 |
| 5 | The prescription in the note flow; a formulary conflict blocks in words; free text needs an acknowledgement that names the allergy | F-01, F-11 |
| 6 | Chart context beside the note; a drawer on tablets | F-11 |
| 7 | A footer that is part of the layout: save status, what is left, preview, sign | F-10, F-04, F-09 |
| 8 | One box finds a returning patient by phone; rows show date of birth and last visit; check-in confirms the date of birth | F-08 |
| 9 | Status counts as filters; a grouped list with the doctor on every row | F-12, F-14 |
| 10 | Undo; doctor load and desk jobs beside the queue | F-05, F-12 |
| 11 | Counts from data that exists; "Not tracked yet" where it does not | F-29 |
| 12 | A needs-attention list where each line has one action | F-29, F-04 |

## What the concept deliberately does not settle

These came out of the reviewers' critique and the arbitration ([07](07-debate-and-arbitration.md)):

- **The medicine box is a sketch.** In the product, the existing Add medicine dialog stays until F-01 is closed and the pilot doctor has seen a replacement.
- **The list does not replace the board before the pilot.** It is offered later as a third view; the receptionist decides.
- **40 px rows are shown for pointer use.** The ruling is 48 px rows wherever a row carries an action; treat the "Touch sizing" view as the truer picture of the desk list.
- **The owner page shows more than can be trusted today.** "Seen" and "median consult" wait for F-05; the ruling keeps five counts and the list.
- **Not designed:** several allergies in the strip; the strip at 375 px (it clips the date of birth); Arabic; the nurse intake; dark mode; real data volumes.

## Preview screenshots

In [concepts/screenshots/](concepts/screenshots/). Rendered with Playwright from the file itself; viewports are emulated.

| View | Files |
|---|---|
| Doctor | `doctor-1440.png`, `doctor-1024.png`, `doctor-768.png`, `doctor-375.png`, `doctor-1440-annotated.png`, `doctor-768-chart-open.png` |
| Doctor states | `doctor-1440-allergy-block.png`, `doctor-1440-free-text-ack.png`, `doctor-1440-sign-dialog.png`, `doctor-1440-signed.png` |
| Receptionist | `desk-1440.png`, `desk-1024.png`, `desk-768.png`, `desk-375.png`, `desk-1440-phone-lookup.png`, `desk-1440-confirm-dob.png`, `desk-1440-checked-in.png`, `desk-1440-annotated.png` |
| Owner | `owner-1440.png`, `owner-1024.png`, `owner-768.png`, `owner-375.png`, `owner-1440-annotated.png` |

Compare with the product today: `screenshots/local-stack/doctor/consult/1440-default-full.png`, `screenshots/local-stack/doctor/front-desk/1024-default.png`, `screenshots/azure-dev/owner-doctor/settings-metrics/1440-default-full.png`.
