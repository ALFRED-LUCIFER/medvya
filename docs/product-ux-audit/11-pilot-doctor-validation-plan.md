# 11 · Pilot doctor and staff validation plan

No clinician, nurse, receptionist or clinic owner took part in this audit. Everything below is a decision the auditors could not make. Nothing safety-sensitive should be built from this audit alone.

Use synthetic patients only in these sessions. The pilot doctor is in Pune; sessions can run on the Azure dev app after it is redeployed from `main` (backlog UX-12), with the concept open beside it.

## 1 · Decisions that need a named person

| # | Decision | Why it matters | Who | Blocks |
|---|---|---|---|---|
| 1 | Do you prescribe by brand or by generic? Which 100 medicines cover most days? | Brands typed as free text skip the allergy check (F-01) | Pilot doctor | UX-01, UX-02 |
| 2 | Wording of the free-text alert: "Not checked against: Penicillin". Is a typed reason acceptable on every such line? | An alert that annoys gets dismissed by habit | Pilot doctor | UX-01 |
| 3 | What must the pinned patient strip show? Name, age and sex, MRN, date of birth, allergy, consent: anything missing, anything too much? How should five allergies show? | It is on screen all day (F-02) | Pilot doctor, nurse | UX-03 |
| 4 | One look for the allergy everywhere: a solid red block with the word "Allergy". And for "not recorded" and "no known allergies"? | Today it differs between chart and consultation (F-19) | Pilot doctor | UX-03, UX-21 |
| 5 | Is a tap to confirm identity at the start of each consultation acceptable, or should the checklist only say the patient was shown? | The checklist claims a confirmation nobody made (F-09) | Pilot doctor | UX-06 |
| 6 | When you end a visit with a prescription not yet authorised: authorise, discard, or end anyway? May it be finished after the patient left? | F-04 | Pilot doctor | UX-05 |
| 7 | Section order of the note. Prescription right after assessment? Diagnosis inline? Is "one page" or "guided steps" better on the tablet in your room? | F-11 | Pilot doctor | UX-14 |
| 8 | Should the nurse's complaint prefill your note? | F-16 | Pilot doctor, nurse | UX-11 |
| 9 | What must the visit record and the printed prescription contain: vitals, diagnosis, weight, qualification line, signature image? | F-27 | Pilot doctor | UX-26 |
| 10 | Are patients seen in token order, arrival order or appointment order? Do booked patients jump walk-ins? | The display, the board and My day disagree today (F-07, F-15) | Pilot doctor, receptionist | UX-10 |
| 11 | Who marks a patient "in the room" and "done": the desk or the doctor? | The desk can close a visit under the doctor (F-05) | Pilot doctor, receptionist | UX-07 |
| 12 | How often do walk-ins leave before being seen, and how is it recorded today? | No such state exists (F-05) | Receptionist | UX-07 |
| 13 | Does the patient need a paper token, or is a spoken number enough? | No print exists (F-15) | Receptionist, owner | UX-23 |
| 14 | Board or list at the desk? | Arbitration left this to the receptionist | Receptionist | UX-19 |
| 15 | Is the date of birth in search results acceptable at your desk, where others may see the screen? | F-08 against privacy | Owner, receptionist | UX-08 |
| 16 | Are the vitals ranges right? Is one confirmation for all flagged values safe? May the nurse correct a reading before the doctor starts? | F-28; ranges await clinician sign-off (issue #956) | Clinician, nurse | UX-25 |
| 17 | Which five numbers do you want every evening? | F-29 | Owner | UX-22 |
| 18 | May a practice admin remove a doctor, or the owner? | F-30 | Owner | UX-27 |
| 19 | Would you ever prescribe or sign from a phone? | The audit proposes blocking it | Pilot doctor | Phone rules in [08](08-design-system-blueprint.md) |
| 20 | Which devices: a desktop at the desk, a tablet or laptop in the room, a tablet for the nurse? Mouse or touch? | Density and layout tiers | Owner, on a site visit | UX-13 to UX-15 |
| 21 | New names: "My day", "Front desk", "Patient record". And when may screens change shape: before training, or after the first weeks? | F-32 | Founder with the clinic | UX-18 |

## 2 · Scenario scripts

Each is a task to do while thinking aloud. The observer notes where the person hesitates, scrolls, asks, or works around the screen. Do not help unless they are stuck for a minute.

### Doctor, on the device they will use in the room (40 minutes)

1. "Your next patient is ready. Start with her." (Today → consultation.) Watch: do they confirm who it is, and how?
2. "She says she has been thirsty for two weeks. Write that, examine, and record what you think it is." Watch: scrolling; where they look for vitals and past visits; whether they miss the chart panel on a tablet.
3. "Add the diagnosis." Watch: the trip to another page and back.
4. "Prescribe what you would normally give." Watch: **brand or generic; whether they use free text; whether they read the "not checked" line.**
5. "She is allergic to penicillin. Prescribe your usual antibiotic for a throat infection." Watch: the block, the override, the free-text path.
6. "The phone rings; you step out and come back." (Scroll the page first.) Ask: "Whose record is this? What is she allergic to?" without letting them scroll up.
7. "Finish the visit." Watch: do they authorise, sign and end, in which order, and what they expect each to do.
8. "You forgot something: add a line to the note you signed."
9. Repeat steps 1 to 7 in the concept. Ask which they would rather use tomorrow, and what the concept got wrong.

### Receptionist, at the real desk if possible (30 minutes)

1. "A new patient walks in. Get her a token." Count the steps.
2. "A man gives you his phone number; he has been here before." Use the two same-name synthetic records. Watch: which one they pick and why.
3. "You pressed Call in on the wrong patient." Watch what they try.
4. "A walk-in got tired of waiting and left."
5. "The doctor said this patient should come back in two weeks. Book it."
6. "Dr. Menon is running late. Move his next patient to Dr. Rao."
7. "How many are waiting for each doctor right now?"
8. Show the board and the concept's list side by side with the same nine patients. "Which would you rather have at 10:30 on a Monday?"

### Nurse, on a tablet (20 minutes)

1. "Take this patient's vitals." Enter a high blood pressure and a fever. Watch: the per-value confirmation.
2. "You typed the weight wrong." Before and after hand-off.
3. "This patient has chest pain. Tell the doctor."
4. Halfway down the form: "Who is this patient?"

### Owner (20 minutes)

1. "It is 11:00. How is the morning going?" Watch where they look.
2. "Did the new system make check-in faster?" (Pilot metrics.)
3. "Add a receptionist. Now change her to a nurse."
4. "Who opened Meera Iyer's record this week?" (Audit log.)
5. Show "Clinic today" in the concept. "Which of these would you act on? What is missing?"

## 3 · Questions to ask after each session

- What did you expect to happen that did not?
- What would you have done on paper at that moment?
- Which screen would you want changed first?
- Was there a moment you were not sure whose record you were in?
- Was anything on screen that the patient should not see?

## 4 · Safe acceptance criteria

A change that touches clinical behaviour is accepted only when:

1. The pilot doctor (and the nurse or receptionist where named above) has seen it working on synthetic data and agreed, and that agreement is written on the ticket with a date.
2. In the doctor's session, the question "whose record is this, and what is she allergic to?" is answered correctly without scrolling, three times out of three.
3. In the doctor's session, no medicine in the allergy's class reaches an authorised prescription without a stop the doctor noticed.
4. The receptionist recovers from a wrong "Call in" without help.
5. The walk-in still takes no more than 6 clicks for a new patient and 3 for a returning one.
6. No tick, "Saved" or number appeared that the person did not cause.
7. The server-side rule behind the change has a test that fails when the rule is removed.

These are the audit's proposed criteria, not a clinical safety standard. The clinical advisor's journey sign-off (MDV-P1-073) stands apart from this plan and is still required.

## 5 · What must not change without validation

The allergy rules and wording; "not recorded" never shown as "none"; the Add medicine dialog; the sign dialog and read-only signed notes; the duplicate block at registration; the identity confirmation at check-in; vitals ranges; what a receptionist may see.
