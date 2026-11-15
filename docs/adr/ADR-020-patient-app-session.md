# ADR-020 · Patient app session: a verified mobile number, no clinic data until records are linked

- **Status:** Accepted 15 Nov 2026 (owner-approved default). (P5); implemented in MDV-P5-033

## Context

The patient app (`mobile/patient`, ADR-011 §2) signs in with a mobile number and an SMS code. The code request and check, with limits and a lock, are built (ADR-004, "Patient SMS code"). A right code started no session, because nothing said what a signed-in patient is. Patient records belong to clinics (tenants, row-level security). A number can belong to several people, and a person can be a patient at several clinics. Linking a number to records needs family profiles and an explicit, time-limited proxy, which is MDV-P5-036.

## Decision

1. **A verified mobile number is a patient identity.** It is known only by the hash the SMS sign-in already keeps (`SmsSignInRules.HashPhone`). It has no clinic, no tenant and no patient record, like the staff sessions table (`identity.PatientSessions`, no row-level security, every query names the session).
2. **The session is the staff app's native session, on its own scheme.** `POST /v1/patient-sign-in/verify` with a right code answers the token pair directly (one call fewer than a code exchange; there is no browser step to protect). The 15-minute access token is data-protected with its own purpose (`Medvya.Patient.Access`). The refresh token has the staff format (`RefreshToken`, user id empty) and the same rotation: only its SHA-256 is stored, each refresh swaps it with a conditional update, and the token the last rotation replaced coming back ends the session (RFC 9700). Lifetimes are the staff app's: 30 days at most, 7 days unused.
3. **A patient session reaches no clinic or patient data.** The principal holds the session id only (claim `patient_sid`), no user, tenant, facility or role. Staff endpoints never accept it: the staff schemes cannot read a patient token, and no staff policy names the patient scheme. Patient endpoints (`/v1/patient/*`, policy `patient`) accept the patient scheme only. Linking records is MDV-P5-036 and needs its own decision on proxy and consent.
4. **Endpoints.** `POST /v1/patient-sign-in/verify` (public, `sign-in` limit) · `POST /v1/patient-sign-in/refresh {refreshToken}` (public, `sign-in` limit; 400 `invalid_grant` means the session is over) · `GET /v1/patient/me` → `{ sessionId, device, signedInAt }`, only what the session holds · `POST /v1/patient/logout` → 204.
5. **Audit.** A patient session has no tenant, so `patient.signin`, `patient.signout` and `session.refresh.replay` go in the platform chain with no staff actor and the session id as the resource.
6. **Device registration.** The app sends `platform` (`ios` or `android`) and `appVersion` with verify (the staff app sends `platform` and `app_version` with its code exchange). The session is labelled "Medvya app 1.2.0 on iOS", which My devices shows. Anything else is dropped, so the label never carries free text.

## Not decided here (shortcuts in code)

- **Device key and proof of possession.** The app does not register a key and a refresh carries no signature, for staff or patients. Neither app has an asymmetric-key library (expo-crypto has digests and random bytes only), so this needs a new native dependency. Until then a copied refresh token works off the phone until the real app's next refresh ends the session as a replay. Add a hardware-backed key (Secure Enclave, Android Keystore) and signed refreshes before the store release (MDV-P5-039).
- **Push tokens and My devices for patients** come with the patient app's home and reminders (MDV-P5-036, MDV-P5-038). A patient signs out per device only.
- **The patient app keeps the session in memory** for now: a refresh token left from an earlier run is ended and wiped at launch, so closing the app signs out (MDV-P5-036 adds the app PIN that a stored session needs).
- **SMS delivery.** Real DLT delivery is deferred by the owner; the Development sink stays.

## Consequences

The patient app can prove it is signed in against the real API, with the staff app's refresh, replay and revoke behaviour, while nothing a clinic holds is reachable. MDV-P5-036 adds a link from this identity to records; until then every patient screen without an API says "not connected".
