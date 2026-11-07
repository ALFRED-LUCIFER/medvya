# Desk-flow load test (MDV-P1-037)

MDV-P1-037. Runs nightly in CI ([`load-nightly.yml`](../../.github/workflows/load-nightly.yml)) and on demand locally. A run on Azure dev
needs a staff session from the owner (see below).

## What it does

`tests/Medvya.IntegrationTests/DeskLoadTests.cs` drives 20 simulated receptionists over real HTTP against `Host.Api`. Each flow:
patient search, confirm patient, hold slot, book (with `Idempotency-Key`), check in, board read, day schedule read. Every desk also keeps the
live board open (a SignalR subscription to `/v1/hubs/board`, as the reception screen does) and times each change it hears ("Board push": from
the change to receipt, budget 2 s from MDV-P1-029). Each desk is a receptionist with a real server-side session, so every call and hub call checks
it as production does. The run prints p50/p95/p99 and the error rate per step, fails when any step is over budget or has an error, and writes a report:

| Report | Profile |
|---|---|
| [`load-test-desk.md`](load-test-desk.md) | the P1 profile: 20 desks, 30 s think time, about 2,000 bookings an hour (10 times the ticket's 200) |
| [`load-test-desk-stress.md`](load-test-desk-stress.md) | stress: 20 desks, 0.5 s think time, 60 s |

Budgets (PRD cross-cutting NFR, ticket MDV-P1-037): reads p95 < 300 ms, booking p95 < 300 ms, writes p95 < 600 ms, no errors.

## How to run

Local (needs Docker; starts PostgreSQL 16 with Testcontainers, seeds synthetic data, starts the API on a loopback port):

```
pnpm load:desk
MEDVYA_LOAD_THINK_MS=500 MEDVYA_LOAD_SECONDS=60 MEDVYA_LOAD_REPORT=/tmp/stress.md pnpm load:desk   # stress
MEDVYA_LOAD_HOT_DAY=1 MEDVYA_LOAD_THINK_MS=500 MEDVYA_LOAD_SECONDS=60 MEDVYA_LOAD_REPORT=/tmp/hot.md pnpm load:desk   # stress, every desk on one doctor-day
```

Other outputs: `MEDVYA_LOAD_RESULTS=<path>` writes the numbers as JSON; `MEDVYA_LOAD_BASELINE=<path>` (a results file) also fails the run when a
step's p95 is more than 20 % over the baseline's.

The test is skipped unless `MEDVYA_LOAD=1` (the script sets it), so `dotnet test` stays fast. The per-IP and per-user rate limits are raised
(`RateLimiting:PermitsPerMinute` and two more) only inside this harness; production configuration is untouched.

## Nightly regression check

[`load-nightly.yml`](../../.github/workflows/load-nightly.yml) runs every night (and on PRs that touch the load test or its baseline): a Release
build of `Host.Api` on Kestrel against PostgreSQL 16 in Docker, the P1 profile for 600 s, compared with
[`load-baseline.json`](../../tests/Medvya.IntegrationTests/load-baseline.json). That baseline comes from a CI run (the `load-results`
artifact), not a laptop; refresh it in a PR when a change makes the flow faster or deliberately slower, and say why. The run fails when a step is
over budget, has an error, or its p95 is more than 20 % over the baseline. A failing nightly or manual run opens an issue titled
"Nightly load test failed (MDV-P1-037)" labelled `performance`, or comments on it while it is open. The report is in the run summary.

## Azure dev (by hand, with the owner)

Staff sign-in on Azure dev is Entra with MFA, so the run cannot sign in by itself. The owner signs in, picks the load-test facility, and hands over
the session cookie; never bypass sign-in. Jobs run on Azure dev and booking confirmations really send email and SMS, so use a dedicated synthetic
practice whose patients have no email and no phone (or only `@example.test`), and cancel what the run booked afterwards.

Set `MEDVYA_LOAD_URL`, `MEDVYA_LOAD_DESKS` (JSON array of request-header objects, one per desk: the
credentials), `MEDVYA_LOAD_FACILITY`, `MEDVYA_LOAD_DOCTORS` (comma-separated calendar ids) and `MEDVYA_LOAD_BOOK_FROM` (a Monday with free
slots for those doctors, e.g. 27 weeks ahead). One cookie for every desk: `MEDVYA_LOAD_DESKS='[{"Cookie":"<name>=<value>"}]'`. The per-user
limits (30 searches a minute) then cap one session at about 15 desks at 30 s think time; use `MEDVYA_LOAD_THINK_MS=60000` with 20 desks (about
1,000 bookings an hour, 5 times the pilot) or one cookie per desk. The target needs at least 20,000 synthetic patients with MRNs 100000 to 119999
(the run searches by MRN) and booked visits today for the check-in step. Write the report outside `docs/` (`MEDVYA_LOAD_REPORT=/tmp/azure.md`).
"Board push" compares the server's change time with the load machine's clock, so clock skew shows up in it. Not run against Azure yet.

## What the numbers mean

- A local run is Docker and Rosetta on a laptop with other processes: PostgreSQL, Kestrel and the load generator share the CPU, and fsync on the
  Colima volume is slower than Azure's log disk. It shows regressions and lock contention, not Azure latency. Treat it as a relative baseline.
- The calendar first-render budget (2.5 s over throttled 4G) is the Playwright test `tests/e2e/schedule-performance.spec.ts`, in CI on every PR (#918).
- The local seed books every day, weekends included, so the check-in step has visits today whenever the run happens.

## Bottlenecks found

1. **Fixed (MDV-P1-037): the doctor-day transaction ran SERIALIZABLE.** Its range locks made unrelated doctors and days queue on each other's
   index ranges in `SlotHolds` and `ScheduleSlots`: in the stress run 93.8 % of holds answered `busy` (hold p95 13,010 ms, p50 3,209 ms, a full
   run completed 2 flows). The doctor-day app lock already makes each check-then-write exclusive and the unique index on `ScheduleSlots` stops a
   double booking if it is bypassed, so `DoctorDayTransaction` now runs READ COMMITTED. Stress after: 0 errors, hold p95 106 ms, book p95 140 ms,
   1,502 complete flows in 61 s. Regression test: `NoDoubleBookingTests.DesksHoldingAndBookingDifferentSlotsAtOnceAllSucceedWithoutBusy`.
   Re-measured on PostgreSQL 16 (MDV-P1-022, after ADR-012; stress profile, 60 s, a quiet laptop). SERIALIZABLE put back as a check:
   58.1 % of holds and 34.7 % of bookings fail (62.9 % and 36.0 % with every desk on one doctor-day). READ COMMITTED: 0 errors and no
   `busy`, hold p95 88 ms and book p95 101 ms spread over doctors; hold p95 50 ms and book p95 64 ms with every desk on one doctor-day
   (`MEDVYA_LOAD_HOT_DAY=1`). The advisory-lock waits are on that doctor-day lock; no deadlock was seen. Regression test for the hot case:
   `NoDoubleBookingTests.AHundredDesksOnOneDoctorDayBookEveryFreeSlotOnceAndNeverAnswerBusy` (100 desks, 84 distinct slots all booked).
   The lock name carries the tenant (`doctor:{tenant}:{calendar}:{date}`), so one practice's load never queues another's.
2. The earlier note that holds at READ COMMITTED left `busy` on book is superseded: the whole transaction (hold, book, walk-in, change) now runs
   at READ COMMITTED, and the test above fails on SERIALIZABLE and passes on READ COMMITTED.
3. The pilot's real rate (200 bookings an hour, about one every 18 s over two clinics) is far below where this appears; it matters for bursts
   (a clinic opening, a campaign) and for the "100 parallel bookings on staging" test of MDV-P1-022.
