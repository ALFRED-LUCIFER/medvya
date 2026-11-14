# 13 · Mobile apps on a simulator (MDV-P5-053)

Run on 14 November 2026, after F-34 and C-20 said nobody could start the Expo apps. This is the same method as the web audit ([01](01-scope-environments-and-coverage.md), [04](04-evidence-backed-findings.md)): open every screen that exists, as each persona, look at it, read what a screen reader gets, try the error and empty states, and write each finding with where, status, evidence, consequence and remedy. Severity and status words are the ones in [04](04-evidence-backed-findings.md). No clinician has reviewed this.

**Data.** The apps read the fixed development cast only (ADR-011, until MDV-P5-033): Dr. Arjun Rao at Adyar for the clinician app, Meera Iyer (`+91 ••••••0101`) for the patient app. Every name, number and result on screen is synthetic ([personas](../00-overview/personas.md)).

**Evidence** is in [`evidence/mobile-simulator/`](evidence/mobile-simulator/), in git (unlike the web set): small JPEGs of synthetic screens only.

## 1 · Environment and the bundling error

| | |
|---|---|
| Checkout | `origin/main` at `a4b37186`, fresh worktree, `pnpm install --frozen-lockfile` |
| iOS | iPhone 18 Pro simulator, iOS 27.0, Expo Go 58.0.7, device language Arabic (as found) |
| Android | Pixel 9a emulator (AVD), Expo Go 58.0.5 (the CLI replaced an outdated 57.0.2 when asked to open the app) |
| Commands | `CI=1 npx expo start --port 8091` in `mobile/clinician` and `--port 8092` in `mobile/patient` (what `pnpm dev:clinician` / `dev:patient` run), then `xcrun simctl openurl booted exp://127.0.0.1:<port>`; on Android `--android` |
| How screens were driven | Android: `adb shell input` and `uiautomator dump` (the dump is what a screen reader gets: `content-desc` and text). iOS: first screen only, because this machine's Xcode has no Simulator window to click in |

**The reported error does not reproduce.** Both apps bundled on the first try in Expo Go on iOS and on Android:

```
iOS Bundled 2998ms node_modules/.pnpm/expo-router@58.0.30_…/expo-router/entry.js (1504 modules)   # clinician
iOS Bundled 3247ms node_modules/.pnpm/expo-router@58.0.30_…/expo-router/entry.js (1503 modules)   # patient
Android Bundled 12403ms … (1501 modules)                                                            # clinician
Android Bundled 3423ms … (1500 modules)                                                             # patient
```

`@babel/helper-define-polyfill-provider@0.6.8` is in `pnpm-lock.yaml` and resolves inside `.pnpm` next to `babel-plugin-polyfill-corejs3`, which is the only package that needs it. C-20's check (`ls node_modules/@babel/helper-define-polyfill-provider`) looked at the top level, where pnpm never puts a transitive package, so a missing folder there is expected. The error in the original screenshot most likely came from an older or partial install. No dependency was changed. To catch a real break, CI now bundles both apps (`expo export`) in the `web-checks` job.

First screens: [clinician sign-in](evidence/mobile-simulator/ios-clinician-01-first-screen-ar.jpg), [patient welcome](evidence/mobile-simulator/ios-patient-01-first-screen-ar.jpg) (iOS, Arabic).

## 2 · Coverage

| App | Screens opened | Not opened |
|---|---|---|
| Clinician | Sign-in (email, code, facility, biometric offer), Today, patient record (Meera Iyer), Patients with search and its empty state, Consult (consent step), Inbox, Account, Lock | Front-desk role tabs (A. Joseph), unlock with biometrics (no enrolled biometrics on the emulator) |
| Patient | Welcome (3 pages), mobile number with its error, code, "Who are you?", biometric offer, Home, Visits, Records (Visits, Results, Medicines), Messages, Profile, Home at 200 % font | Family member switch (Kavya Iyer), Lock, UAE number |

## 3 · Fixed in this ticket (AC3, B-25)

`packages/ui-native/src/controls.tsx`: the secondary button and the icon button draw their border with the `input` token, and text fields use `ink600` for the placeholder. `mobile/clinician/src/test/contrast.test.tsx` renders the three controls and computes the contrast against `card` and `surface`.

| | Before | After |
|---|---|---|
| Secondary and icon button border on card / surface | `line` 1.25 / 1.19 | `input` 3.60 / 3.44 |
| Placeholder on card / surface | `ink400` 3.05 / 2.91 | `ink600` 6.04 / 5.76 |

## 4 · Findings

### M-01 · Rows tell a screen reader the name but not the allergy or critical flag
| | |
|---|---|
| Where | Clinician Patients list and Today queue (`ListRow` in `packages/ui-native/src/display.tsx`) |
| Severity, status | P1 · verified defect (Android, `uiautomator dump`) |
| Evidence | Meera Iyer's row reads `"Meera Iyer, 54 F · ••4471"`; the "Allergy" chip beside it is not in the label. Same for "Critical" (Rahul Menon), "Overdue", "New", and the queue's "Next", "Waiting", "Via app". [patients](evidence/mobile-simulator/android-clinician-06-patients.jpg), [today](evidence/mobile-simulator/android-clinician-04-today.jpg) |
| Consequence | A doctor using TalkBack or VoiceOver hears no allergy or critical flag before opening the patient |
| Remedy | `ListRow` builds its label from title, subtitle and the trailing chip's words; callers that pass `label` include the flag |

### M-02 · Icon glyphs are read out as part of tab and banner labels
| | |
|---|---|
| Where | Both apps: bottom tabs, the allergy banner on the record, every row or button that groups an `Icon` |
| Severity, status | P2 · verified defect (Android) |
| Evidence | Tabs read `", Today"`, `", 3, Inbox"`; the banner reads `", Allergy: Penicillin (rash), , Verified"`. `Icon` sets `accessible={false}`, which does not keep the glyph out of a parent's merged label on Android |
| Consequence | TalkBack reads private-use characters (silence or "unknown") before every tab and in the allergy banner |
| Remedy | `Icon` also sets `importantForAccessibility="no"` and `accessibilityElementsHidden`; tab bar icons the same; a test on the dump-equivalent (`getByRole(...).props.accessibilityLabel`) |

### M-03 · Arabic shows left to right
| | |
|---|---|
| Where | Both apps in Expo Go on iOS with the device in Arabic |
| Severity, status | P1 (language) · verified defect |
| Evidence | Arabic text is translated but the layout stays left to right: monogram at the left, headings left-aligned. [clinician](evidence/mobile-simulator/ios-clinician-01-first-screen-ar.jpg), [patient](evidence/mobile-simulator/ios-patient-01-first-screen-ar.jpg). Neither `app.json` sets `supportsRTL` for `expo-localization`, and no code calls `I18nManager.forceRTL` |
| Consequence | UAE users get a mirrored-wrong layout; the RTL chevron and back-arrow code in `ui-native` never takes effect |
| Remedy | `supportsRTL: true` in both apps' `expo-localization` plugin config, and `I18nManager.allowRTL`/`forceRTL` from the locale with one reload; check in Expo Go and a dev build |

### M-04 · Allergy and medicines share one icon, and the allergy chip does not say "allergy"
| | |
|---|---|
| Where | Clinician Today ("Up next" chip), Patients flag; patient Welcome and Home medicine items |
| Severity, status | P2 (clinical safety) · verified |
| Evidence | Allergy uses Material `medication` (`TodayScreen.tsx` line 9); the patient app uses the same glyph for Medicines. Today's chip reads "Penicillin" only; the record reads "Allergy: Penicillin (rash)". Native queue states (With doctor, Next, Waiting, Booked, Attended) have no icon, while web status chips always have one (`packages/ui/src/primitives.tsx`) and web uses Lucide, native Material (C-21). [today](evidence/mobile-simulator/android-clinician-04-today.jpg), [record](evidence/mobile-simulator/android-clinician-05-record.jpg), [patient home](evidence/mobile-simulator/android-patient-03-home.jpg) |
| Consequence | The allergy cue is colour plus a medicine icon; the same state looks different on phone and desktop |
| Remedy | One status → icon and words map in `packages/tokens` used by `packages/ui` and `packages/ui-native`; allergy gets its own glyph and the chip says "Allergy: Penicillin". **Not fixed here** (AC3 asks to record it) |

### M-05 · Disabled actions do not say why on screen
| | |
|---|---|
| Where | Clinician Consult ("Patient agrees", "Patient says no"), Inbox ("Mark reviewed"); patient Home ("Reschedule", "Pay now", "Join waiting room"), Visits ("Book a visit") |
| Severity, status | P2 · verified |
| Evidence | The reason ("Available in a later release") is only in `accessibilityHint`. The 0.45 opacity puts white-on-brand labels well under 4.5:1, although `Button`'s comment says disabled buttons "stay readable". [consult](evidence/mobile-simulator/android-clinician-07-consult.jpg), [inbox](evidence/mobile-simulator/android-clinician-08-inbox.jpg), [visits](evidence/mobile-simulator/android-patient-04-visits.jpg) |
| Consequence | Sighted users see greyed main actions (the Book tab's only action) with no reason |
| Remedy | Show the hint as a caption under a disabled `Button`, or hide actions that are not built; disabled colours from tokens instead of opacity |

### M-06 · "Face ID" on Android
| | |
|---|---|
| Where | Both apps: biometric offer after sign-in, Account, Profile, Welcome page 2 |
| Severity, status | P2 · verified |
| Evidence | Pixel emulator shows "Use Face ID?" ([evidence](evidence/mobile-simulator/android-clinician-03-face-id-on-android.jpg)); keys `mobile.signIn.biometricTitle`, `mobile.account.biometricOff`, `mobile.patientSignIn.biometricNone` |
| Consequence | Wrong platform words on most phones in India; the offer has only "Continue", no "Not now" |
| Remedy | Platform-aware keys (Face ID / Touch ID on iOS, "fingerprint or face unlock" on Android) in en, hi, ar |

### M-07 · The keyboard covers the sign-in button
| | |
|---|---|
| Where | Clinician sign-in (email, code); patient mobile number |
| Severity, status | P3 · verified |
| Evidence | With the keyboard up, "Continue" is hidden ([evidence](evidence/mobile-simulator/android-clinician-02-keyboard-hides-continue.jpg)); Enter does submit |
| Remedy | `Screen` footer inside a `KeyboardAvoidingView` |

### M-08 · Patient choices say "change any time" but cannot be changed
| | |
|---|---|
| Where | Patient Profile, "Your choices" |
| Severity, status | P2 (consent) · verified |
| Evidence | Reminders, ABDM sharing and visit recording show as "On" chips with no control under "Change any time" ([evidence](evidence/mobile-simulator/android-patient-06-profile.jpg)) |
| Remedy | Until consent changes are built: say how to change them ("Ask your clinic") instead of "Change any time" |

### M-09 · A message can be typed but not sent
| | |
|---|---|
| Where | Patient Messages |
| Severity, status | P3 · verified |
| Evidence | The field takes text; send is disabled with "Available in a later release" ([evidence](evidence/mobile-simulator/android-patient-05-messages.jpg)) |
| Consequence | What the patient types is lost |
| Remedy | Disable the field with the send button, or hide the composer |

### M-10 · Two buttons with the same name in the Inbox
| | |
|---|---|
| Where | Clinician Inbox |
| Severity, status | P3 · verified |
| Evidence | Both results have a button labelled only "Mark reviewed" ([evidence](evidence/mobile-simulator/android-clinician-08-inbox.jpg)) |
| Remedy | Label "Mark reviewed: Rahul Menon, Potassium" |

### M-11 · The development cast has fixed dates, so past visits look upcoming
| | |
|---|---|
| Where | Patient Home |
| Severity, status | P3 · verified (development data only) |
| Evidence | "Video visit · 13 Nov 2026 · 17:00 · you can join 10 minutes early" shown on 14 Nov ([evidence](evidence/mobile-simulator/android-patient-03-home.jpg)) |
| Remedy | Cast dates relative to today in `mobile/*/src/dev/cast.ts` |

### M-12 · Names that disagree, and icons that do not grow with text
| | |
|---|---|
| Where | Patient tabs; both apps at large font |
| Severity, status | P3 · verified |
| Evidence | The "Book" tab opens a screen titled "Visits", and Records has its own "Visits" tab ([visits](evidence/mobile-simulator/android-patient-04-visits.jpg)). At 200 % system font the text scales but chip and button icons stay at their fixed size ([evidence](evidence/mobile-simulator/android-patient-08-font-200.jpg)); changing the font size also reloads the app and signs the user out (in-memory session, the recorded MDV-P5-033 shortcut) |
| Remedy | One name for the tab and its screen; `Icon` size scales with `PixelRatio.getFontScale()` (capped) |

Seen and fine: the error states on the number and code fields (icon plus words), the empty search state, the lock screen, masked phone and MRN, "AI found · not reviewed" and "Verified" provenance, the emergency line on patient Home, 44 px and larger targets (tabs 49 dp, buttons 52 dp).

## 5 · Proposed tickets

For the coordinator to create; none were created by this ticket.

| Title | Finding | Acceptance criteria |
|---|---|---|
| Native rows and tabs give screen readers the flag, not the icon glyph | M-01, M-02, M-10 | Given a patient row with an allergy or critical flag, its accessible name includes the flag's words; no accessible name contains an icon glyph; each "Mark reviewed" names its result |
| Arabic lays out right to left in both Expo apps | M-03 | Given the device in Arabic, both apps lay out right to left after one reload, in Expo Go and a dev build; screenshots in the PR |
| One status icon map shared by web and native; allergy has its own icon and words | M-04, C-21 | Given a status, web and native show the same glyph and words from one map in `packages/tokens`; the allergy chip reads "Allergy: <substance>" with a glyph not used for medicines |
| Disabled actions say why on screen | M-05, M-09 | Given a disabled action, its reason shows as text under it; disabled colours come from tokens; the message field is disabled when sending is |
| Biometric wording per platform | M-06 | Given Android, no screen says "Face ID"; the offer has a "Not now" |
| Footer actions stay above the keyboard | M-07 | Given the keyboard is up on sign-in, the primary button is visible |
| Patient choices: honest wording until they can be changed | M-08 | Given Profile, the choices say how to change them; "Change any time" only appears when a control exists |
| Development cast dates relative to today | M-11 | Given any day, the cast's upcoming items are in the future and past items in the past |
| Patient tab names match screens; icons scale with font size | M-12 | Given the Book tab, its screen title matches; at 200 % font, icons grow with text |
