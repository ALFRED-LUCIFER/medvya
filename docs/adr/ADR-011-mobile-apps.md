# ADR-011 · Mobile: two Expo store apps, native token sign-in, what a phone may keep

- **Status:** Accepted 7 Nov 2026 (founder). Two items are deferred to before store release (see "Deferred"). Ticket: MDV-P5-031.
- **Builds on:** ADR-004 (sessions), ADR-009 (front-end layers), ADR-013 (repository layout).

## Context

Doctors want the queue, the patient summary and recording on their phone. Patients want booking, results and payment in an app. The web app's rules hold: no tokens in the browser, server-side authorisation, no PHI outside the database and Blob. A phone breaks three web assumptions: it has no cookie-based backend-for-frontend, it is lost or shared more often than a clinic PC, and it must work offline.

## Decision

1. **Two store apps from one Expo workspace.** *Medvya Clinic* (`mobile/clinician`, teal, for doctors, nurses and front desk) and *Medvya for patients* (`mobile/patient`, indigo, so screenshots are never mistaken for the clinic app). They share `packages/tokens`, `packages/i18n` and `packages/api-client`, plus the layers of ADR-009. Native UI is `packages/ui-native`, our own components and not React Native Paper. The apps run on Development-only synthetic data until they are wired to the API.

2. **Sign-in.**
   - **Staff:** OIDC code + PKCE in the system browser through Host.Api, then bearer tokens backed by the same server-side session row as the web cookie (ADR-004, "native staff app"). Revoke, "My devices" and offboarding apply unchanged. Refresh tokens rotate, and a replay ends the session.
   - **Patients:** mobile number + SMS code, then Face ID, fingerprint or PIN. This waits for patient accounts and DLT-registered SMS (MDV-P5-033, later slice).
   - **Lock and refresh:** the app locks after 15 idle minutes (biometric, PIN fallback) and refreshes one request at a time.
   - **Session length:** a native session lasts at most 30 days and ends after 7 days unused. Browser sessions keep 8 h and the tenant's idle timeout.

3. **What a phone may keep.** This is the one exception to AGENTS.md 1.2, and nothing beyond it is allowed:
   - **Secure-store** (Keychain / Android Keystore): the refresh token, the device key and the PIN check value. The access token stays in memory only.
   - **Encrypted SQLite**, with its key in secure-store:
     - *Staff app:* today's list and the patient summaries opened today (read-only last copy), and recording chunks not yet uploaded.
     - *Patient app:* only the signed-in person's own last copy (and their proxied family members'), plus bookings waiting to sync.
   - **When it is deleted:** cached copies go after 24 h. Everything goes at sign-out, on a revoke (the first 401 from a refresh), on switching user, and on a failed PIN limit.
   - **Never on the phone:** PHI in AsyncStorage, files outside the encrypted database, logs, crash reports or analytics.

4. **Lost phone.** A revoke ends the session server-side at once (done for staff). The app then wipes everything local, **including unsent audio**. The server already knows the recording started, so that visit is flagged "recording incomplete: write the note by hand" (MDV-P5-035). Nothing on a lost phone stays readable, at the cost of that one recording.

5. **Shared phone.** One signed-in person per app install. "Switch user" means a full sign-out and wipe, then a new sign-in. No accounts are cached side by side.

6. **Screenshots and the app switcher.**
   - **Staff app:** screenshots and screen recording are blocked on Android (`FLAG_SECURE`). On iOS the app-switcher preview is blurred, and screenshots are detected and audited.
   - **Patient app:** switcher blur only, so patients can keep their own prescription or booking.

7. **Rooted or jailbroken phones.** The staff app refuses to sign in on a detected device, because it holds other patients' data offline. The patient app warns and works. Detection is best effort; it is recorded, not relied on.

8. **Push.** Payloads carry an opaque id and the fixed text "You have an update from your clinic". The app fetches the detail after unlock (MDV-P5-038). Per-user opt-in and quiet hours apply. A revoked device's push token is removed with its session.

9. **Over-the-air updates.** EAS Update ships UI fixes only. Clinical logic (consent, signing, allergy display, dosing, the offline rules above) ships only with store builds and release notes, and the pipeline refuses an update that touches it (MDV-P5-039).

## Deferred (founder, 7 Nov 2026: not now, before store release)

- **Store and Expo account owner:** Apple and Google developer accounts under the company. Apple needs a D-U-N-S number, and the Expo organisation has to be created. This blocks EAS preview builds (MDV-P5-032) and TestFlight.
- **Verified https links:** Universal Links and App Links replace the custom schemes before store release. They need the production domain to host `apple-app-site-association` and `assetlinks.json` (MDV-P5-039).

## Consequences

- AGENTS.md 1.2 names the on-device exception and points here. Any wider use of device storage needs a new ADR.
- The threat model gains boundary B7 (phone apps), and the data map gains device storage and push payloads.
- Unsent recordings on a revoked phone are lost by design; the flagged visit is the safety net.
- Rooted-device detection and screenshot detection on iOS can be bypassed by a determined user. Server-side authorisation and the short offline window remain the real controls.
