# Medvya in India: unreliable internet and power

Date: 2026-11-04. Scope: research only, no repo edits. Labels: **[V]** verified from a cited source, **[R]** read in the repo, **[I]** my inference or general engineering knowledge (not verified in this research).

## 1. Answer in brief

Do not build "full offline" for P1. Build a web app that **degrades gracefully**: no lost input, safe retries, honest status, a printable fallback, and SMS for the patient. Add a small, encrypted, read-only "today's schedule" cache only if pilot clinics show real outages. Defer offline writes to clinical or consent-gated data. This fits Medvya's rules (AGENTS.md 1.2, 1.5, 1.4) and the front-desk-first scope.

## 2. What the repo already says

- **[R] Planned/built**
  - `web/clinic/src/features/connectivity` exists: `isReachable()` does a HEAD to our origin (not just `navigator.onLine`), `OfflineBanner` shows over the page "so typed input survives (F-05d)". It is a banner only. There is no queue, retry, or storage.
  - X4 states (`cross-cutting-metrics-process.md`) lists "offline" among required UI states.
  - `api-sketch.md`: idempotency keys on POSTs that create money, messages, orders, deliveries. Booking is not named explicitly.
  - Requirements: "calendar first render < 2.5 s on a clinic PC over 4G"; booking API p95 < 300 ms. MDV-P1-037 is the performance budget ticket.
  - Patient portal is planned as a PWA (`repository-structure.md`).
  - **MDV-P8-006** (COULD, 6 pts, weeks 58-62, DESIGN NEEDED, confidence 45): offline read of today's charts and draft notes with encrypted local storage and sync. AC: encrypted, time-limited, wiped on sign-out, conflicts surfaced. Its own note says "significant security implications".
  - P6-01: the wearable "clip" has an encrypted offline buffer (device, not web).
  - Existing P1 tickets that matter: P1-036 tablet-responsive reception, P1-037 performance budget, P1-032/033 SMS/email reminders (done), P1-039 production deploy, P1-041 baseline metrics, P1-045 DLT.
- **[R] Gaps**: nothing on power cuts, request timeouts, retry policy, draft autosave, printable day sheet, or measuring the clinic's real connection.

## 3. Verified facts about India

All sources accessed 2026-11-04 via search snippets or fetch; I could not open most pages in full.

**Connectivity**
- **[V]** Ookla median mobile download in India was 131.77 Mbps in Aug 2025 (rank 25th). Fixed broadband median was 63.79 Mbps in Aug 2024 (rank 84th). Those are national medians from speed tests, dominated by metros and 5G users. [telecomtalk-mobile], [telecomtalk-rank]
- **[V]** V-SPEED SpeedGeo (Jul 2025 to Jun 2026): Jio mobile avg 122.7 Mbps, ACT Fibernet 121.2 Mbps; different method, not comparable with Ookla. [telecomtalk-vspeed]
- **[V]** Practo's own history says Practo Ray, "being an online SaaS product", suffered irregular internet. [yourstory/aitopics]
- **[Not found]** TRAI figures, latency, and tier-2/3 outage patterns. Speed is therefore *not* the proven problem; **intermittency and latency spikes** are, and I have no data on them. Treat as a pilot question.

**Power**
- **[V]** Government figures cited by Upstox: rural supply 12.5 h/day (2015) to 21.9 h (2024); urban 23.4 h. Same piece says reality differs from official figures in some states. [upstox]
- **[V]** LocalCircles survey (via Media India): two in three households have outages; one in three have cuts over 2 h a day. Older LocalCircles 2022 data (Statista): 28% up to 2 h/day, 2% 12-24 h/day. [mediaindia], [statista]
- **[V]** Only 17 of 36 DISCOMs report SAIFI/SAIDI, so there is no consistent national reliability number. [leap-journal]
- **[V]** An Indian small-hospital IT article says 85% of private establishments have fewer than 25 beds and many lack UPS backup, so PCs crash on power loss. [siliconindia] (opinion piece, date not confirmed)
- **[Not found]** Inverter/UPS penetration, battery runtime in clinics, device mix (low-end Android, shared PCs). Treat all as unknown.

**Competitors (vendor claims, not independently tested)**
- **[V]** HealthPlix launched an offline EMR (Oct 2023): records and prescriptions without internet, auto-switch, sync "within five minutes" after reconnect. [healthcare-digital]
- **[V]** Practo says its Practo Tab app runs the full Practo Ray offline and syncs later; Practo Pro app listing claims offline access. [fitgap/play-store]
- **[V]** IndiaHMIS markets offline in tier-2/3 towns. [india-hmis]
- **[V]** Bahmni Connect (open source) supports offline patient search and registration in remote sub-centres (JSS). [bahmni]
- **[Not found]** DocPulse and Eka Care offline details. Eka's "EMR fails" page confirms the problem is unstable clinic internet but says nothing on power or its offline features. [eka]
- **Takeaway [I]**: offline is a marketing differentiator for the doctor-side EMR. It is less proven for front desk, where the hard problem is double booking.

**Law**
- **[V]** DPDP Rules 2025 (notified Nov 2025; most obligations from about May 2027): Rule 6 "reasonable security safeguards" including encryption, masking, access control, logs, backups; Rule 7 breach notice to the Board within 72 h with no risk threshold, so **any lost/stolen device holding unencrypted patient data is a reportable breach**. Penalty caps cited up to INR 250 cr. [dpdp-secondary sources; confirm with gazette]
- **[Not found]** ABDM rules on storing data on devices. Do not assume permissive. Ask counsel (MDV-P1-044).

## 4. Technical options and fit (mostly [I], well-established web platform behaviour)

| Option | Fit for Medvya |
|---|---|
| Retries with backoff, request timeouts, `Idempotency-Key` header, server dedupe | **Do now.** No stored PHI; it is the biggest resilience win. |
| Small payloads (paged lists, compressed JSON, no big bundles), code-split, cache static assets via service worker | **Do now.** Static assets contain no PHI, so a service worker for app shell is safe. |
| Autosave drafts of form state | **Non-PHI only** in browser storage. Registration forms are PHI, so keep in memory and warn before leave, or hold server-side drafts when online. |
| Optimistic UI | Only for harmless actions (toggle filter, mark notification read). Never for booking. |
| Service worker + IndexedDB read cache | Possible but PHI at rest on a shared PC. Needs WebCrypto encryption with a key not stored beside the data; browsers may evict storage (notably Safari/iOS). Pilot-optional. |
| Background Sync / outbox | Background Sync API is Chromium-only [I]. For writes, only safe for non-conflicting actions. |
| CRDTs / local-first | Overkill; bookings need a single arbiter. Not for Medvya. |
| Printable day sheet (PDF/print CSS) | Cheap, works through a power cut, but a printed sheet is PHI on paper. Same handling as any clinic paper. |
| SMS/WhatsApp to patient | Already built for reminders. Must stay PHI-free in previews (AGENTS 1.2). |
| Desktop wrapper (Electron/Tauri) with local DB | Heavy: second platform, local-DB security, sync. Defer. |
| Local clinic server / mini-PC | Defer; high ops burden for a one-developer team. |

Power-specific [I]: a cut kills the PC, router and often the ISP node. Browsers survive only if there is UPS. Most useful web-side mitigations: server-side persistence of every confirmed step, no multi-screen unsaved wizard, fast cold start after reboot (small bundle, session resume), and a mobile-phone path (tethering/phone browser) for the receptionist. A phone on 4G with battery is often the only working device during a cut.

## 5. Conflicts with Medvya's safety rules

- **AGENTS 1.2 (no PHI in browser storage)**: any offline cache of schedule/patient data violates it unless encrypted and justified. P8-006 acknowledges this. Require an ADR before any PHI in IndexedDB.
- **1.5 (signed records never edited; AI is a draft until signed)**: signing and amendment must be online only. An offline "signed" note would need server-side time and identity proof.
- **1.3/1.4 (server-side authorisation, tenant isolation)**: a cache is outside the server's control. A revoked staff member (P1-009 "ends sessions immediately") can still read an offline cache until it expires. Mitigate with short expiry (e.g. end of day), key held in memory/derived from session so sign-out wipes it.
- **Double-booking guarantee**: needs one authoritative slot allocator. Offline booking creates conflicts the clinic discovers at the desk with the patient present.
- **Consent (P1-019)**: consent state can change; offline checks may be stale.
- **Shared PCs** [I]: the cache persists across users unless wiped; per-user keys required.

**Do NOT build offline (any phase before pilot evidence):** signing, amendment, prescribing, consent capture or consent-dependent sharing, booking/reschedule/cancel, merge/duplicate review, billing/money, patient identity creation (duplicate risk), AI drafting, staff/role administration, ABDM calls.

## 6. Recommended phases

### (a) P1 now: resilient to flaky connections (no PHI at rest)

| Item | Ticket | Size |
|---|---|---|
| Extend booking, check-in, registration POSTs with `Idempotency-Key` plus server dedupe (confirm booking path is included; api-sketch lists only money/messages/orders) | new **MDV-P1-047** | 3 |
| Shared API client: timeouts (e.g. 15 s), bounded retry with jitter for GETs and idempotent POSTs, clear failed-state message | new **MDV-P1-048** (could merge with 047) | 3 |
| Offline banner shows "N changes not saved" and blocks, rather than hides, actions that cannot be queued; `beforeunload` warning on dirty forms | extends existing connectivity feature; add to P1-036 | 2 |
| Throttled-network budget in perf test (Slow 4G, 150 ms RTT, 4x CPU) and bundle-size limit | extend **MDV-P1-037** | 2 |
| Service worker for static assets only | new **MDV-P1-049** | 2 |

Gherkin, MDV-P1-047/048:
```
Scenario: Network drops after a booking request is sent
  Given the receptionist submits a booking and the response never arrives
  When the client retries with the same Idempotency-Key
  Then exactly one appointment exists
  And the receptionist sees the confirmed booking

Scenario: Slot taken meanwhile
  Given a retry arrives after another user took the slot
  Then the API returns a conflict and the UI offers other slots
  And no duplicate appointment is created

Scenario: Request times out
  Given the API does not answer within 15 seconds
  Then the UI shows a retry message without clearing the form
```
Gherkin, MDV-P1-049:
```
Scenario: Reload with no network
  Given the user opened the app earlier today
  When the network is off and the page reloads
  Then the app shell loads with the offline state and no patient data is shown
  And browser storage contains no patient identifiers (automated check)
```

**As built (MDV-P1-047).** `POST /v1/appointments`, `POST /v1/patients` and `POST /v1/appointments/{id}/check-in` accept an `Idempotency-Key` (8 to 64 letters, digits, `-`, `_`). The server stores no response and no separate key table: the booking or patient row carries two hashes (SHA-256 of the key; SHA-256 of the key plus the request, so patient fields cannot be guessed back), unique per tenant and user, cleared after 24 hours by the next keyed write of the tenant. A repeat with the same request rebuilds the first outcome from the record; the same key with another request is 422. Identical requests racing each other are serialised by the doctor-day lock (booking) or the unique index (registration). Check-in needs no stored key: it is idempotent by state. The client keeps one key per submission (reused on retry, new after an edit or an answer). `apiFetch` is the only retry layer: TanStack Query does not retry, so a failing GET is at most 3 requests, not 9.

### (b) By R1 pilot (decide after pilot interviews)

| Item | Ticket | Size |
|---|---|---|
| **Printable day sheet** (time, token, first name + age, doctor, reason code; no phone/ID), PDF/print CSS | new **MDV-P1-050** | 3 |
| **Patient SMS fallback**: if staff cannot reach cloud, patients still got 24 h/2 h reminders (already independent of clinic internet); add "reply to confirm/cancel" only if DLT permits | covered by P1-032/033/045 | 0 |
| **Encrypted read-only today's schedule cache**, 12 h expiry, wiped on sign-out, per-user key, no search | new **MDV-P1-051**, needs ADR first | 5 |
| Connectivity telemetry (outage count, duration, offline-banner shown), PHI-free | extend **MDV-P1-041** | 2 |
| Pilot runbook: "if internet is down" desk card (phone hotspot, paper day sheet, walk-in log to enter later) | extend **MDV-P1-040** | 1 |

Gherkin, MDV-P1-050:
```
Scenario: Print the day sheet
  Given a receptionist views today's schedule
  When they choose Print day sheet
  Then a one-page-per-doctor sheet is produced in the facility time zone
  And it excludes phone numbers and national IDs
  And an access-log entry records who printed it
```
Gherkin, MDV-P1-051:
```
Scenario: Schedule readable during outage
  Given the receptionist loaded today's schedule while online
  When the network drops
  Then the schedule is shown read-only with an "as of <time>" label
  And create, edit and cancel are disabled

Scenario: Cache is wiped
  When the user signs out, or 12 hours pass, or the account is deactivated
  Then the cached schedule is deleted and unreadable

Scenario: Stored data is encrypted (mutation check)
  Then inspecting IndexedDB shows only ciphertext
  And the test fails if encryption is removed
```
Only build P1-051 if pilot clinics report outages long enough to need it; the paper sheet may suffice.

### (c) Defer
- P8-006 as written (offline charts, draft notes, sync). Keep as COULD. Revisit only with design approval, ADR, legal view on DPDP/ABDM, and evidence that pilots lose real work.
- Desktop wrapper, local server, CRDTs, offline booking, queued writes of clinical data.
- If a competitor win depends on "offline EMR", position it later as doctor-side drafts only, signing online.

## 7. Questions for the two pilot clinics
1. What internet do you have (broadband brand, 4G dongle, phone hotspot)? Do you have a backup line?
2. In the last month, how many times did the internet go down during clinic hours? How long each time?
3. How many power cuts in clinic hours? Do you have UPS or inverter, and for how long does it run the PC and router?
4. What do you do today when the system is down: paper register, phone calls, wait?
5. Which device does the front desk use (PC model, age, shared by staff, tablet, phone)? Which browser?
6. Do doctors use their own phones in consult rooms? Is a mobile hotspot acceptable?
7. Would a printed day sheet be useful? When would you print it (morning, every hour)?
8. Is a read-only view of today's schedule enough during an outage, or must you register or book walk-ins?
9. Is there a lockable room and do staff share a login?
10. Are you comfortable with patient data on the clinic PC if it is encrypted and wiped daily?
11. What is the worst outage you can tolerate before you give up on the software?
12. Is the patient's phone the best fallback (SMS), and which language do patients prefer?

## 8. Caveats
- Competitor offline claims are marketing; I did not test any product.
- National speed medians say little about a tier-3 clinic. The pilots are the real data source.
- Nothing found on ABDM device-storage rules or DPDP wording beyond secondary summaries; verify against the gazette.
- Browser behaviour claims (Background Sync support, storage eviction) are from general knowledge, not re-verified now.

## Sources (accessed 2026-11-04)
- [telecomtalk-mobile] https://telecomtalk.info/india-sees-median-mobile-download-speed-jump/1000114/
- [telecomtalk-rank] https://telecomtalk.info/india-falls-4-ranks-in-ooklas-speedtest-global-index-for-mobile-speeds
- [telecomtalk-vspeed] https://telecomtalk.info/jio-tops-mobilespeeds-while-actfibernet-broadband-india/1009440/
- [upstox] https://upstox.com/news/upstox-originals/investing/watt-s-next-india-s-growing-rural-electrification/article-158206
- [mediaindia] https://mediaindia.eu/?p=127314
- [statista] https://statista.com/statistics/1245470/india-frequency-of-power-outages-in-households-by-region
- [leap-journal] https://blog.theleapjournal.org/2025/09/a-review-of-outage-reporting-by-indian.html
- [siliconindia] https://www.siliconindia.com/viewpoint/cxoinsights/challenges-in-adopting-it-for-small-hospitals-chains-of-small-hospitals-nwid-11634.html
- [healthcare-digital] https://healthcare-digital.com/digital-healthcare/healthplixs-emr-offline-capability-to-help-doctors-in-india (published 2023-10-17)
- [eka] https://eka.care/services/emr-fails-why-it-happens-how-to-save-your-clinic
- [fitgap] https://us.fitgap.com/products/ray-by-practo ; [play-store] https://play.google.com/store/apps/details?id=com.practo.droid ; [yourstory] https://cwv.yourstory.com/2014/08/practo-growth-story
- [india-hmis] https://india-hmis-site.pages.dev/
- [bahmni] https://medium.com/bahmni-blog/offline-patient-search-and-registration-in-remote-sub-centres-of-jss-using-bahmni-connect-88987b040df8
- [dpdp] https://www.mondaq.com/india/privacy-protection/1707558/digital-personal-data-protection-rules-2025-highlights ; https://dpdpa.com/dpdparules/rule7.html ; https://static.pib.gov.in/WriteReadData/specificdocs/documents/2025/nov/doc20251117695301.pdf
- Repo: AGENTS.md; docs/01-prd/requirements-catalogue.md; docs/01-prd/cross-cutting-metrics-process.md; docs/03-architecture/api-sketch.md; tickets/_source/P8.tickets (MDV-P8-006); web/clinic/src/features/connectivity.
