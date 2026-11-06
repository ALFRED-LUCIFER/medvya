# Desk-flow load test (MDV-P1-037)

The local part of MDV-P1-037. The run on Azure staging is a later, human-approved step.

## What it does

`tests/Medvya.IntegrationTests/DeskLoadTests.cs` drives 20 simulated receptionists over real HTTP against `Host.Api`. Each flow:
patient search, confirm patient, hold slot, book (with `Idempotency-Key`), check in, board read, day schedule read. It prints p50/p95/p99 and the
error rate per step, fails the run when any step is over budget or has an error, and writes a report:

| Report | Profile |
|---|---|
| [`load-test-desk.md`](load-test-desk.md) | the P1 profile: 20 desks, 30 s think time, about 2,000 bookings an hour (10 times the ticket's 200) |
| [`load-test-desk-stress.md`](load-test-desk-stress.md) | stress: 20 desks, 0.5 s think time, 60 s |

Budgets (PRD cross-cutting NFR, ticket MDV-P1-037): reads p95 < 300 ms, booking p95 < 300 ms, writes p95 < 600 ms, no errors.

## How to run

Local (needs Docker; starts SQL Server 2022 with Testcontainers, seeds synthetic data, starts the API on a loopback port):

```
pnpm load:desk
MEDVYA_LOAD_THINK_MS=500 MEDVYA_LOAD_SECONDS=60 MEDVYA_LOAD_REPORT=/tmp/stress.md pnpm load:desk   # stress
```

The test is skipped unless `MEDVYA_LOAD=1` (the script sets it), so `dotnet test` stays fast. The per-IP and per-user rate limits are raised
(`RateLimiting:PermitsPerMinute` and two more) only inside this harness; production configuration is untouched.

Another environment (staging): set `MEDVYA_LOAD_URL`, `MEDVYA_LOAD_DESKS` (JSON array of request-header objects, one per desk: the
credentials), `MEDVYA_LOAD_FACILITY`, `MEDVYA_LOAD_DOCTORS` (comma-separated calendar ids) and `MEDVYA_LOAD_BOOK_FROM` (a Monday with free
slots for those doctors, e.g. 27 weeks ahead). The target needs at least 20,000 synthetic patients with MRNs 100000 to 119999 (the run searches by MRN) and
booked visits today for the check-in step. Not run against Azure yet; the external path only differs from the local one in where the URL and headers come from.

## What the numbers mean

- A local run is Docker and Rosetta on a laptop with other processes: SQL Server, Kestrel and the load generator share the CPU, and fsync on the
  Colima volume is slower than Azure's log disk. It shows regressions and lock contention, not Azure latency. Treat it as a relative baseline.
- It runs with `READ_COMMITTED_SNAPSHOT` on, as Azure SQL Database does by default (Docker SQL Server has it off; `MEDVYA_LOAD_RCSI=0` turns it off).
- Not covered yet: SignalR board subscriptions (board refetch is measured, not the socket), the nightly job with the 20 % regression alert, and the
  calendar first-render budget (that is the Playwright test `tests/e2e/schedule-performance.spec.ts`).
- The check-in step only runs when the board has booked visits today, so it has fewer calls than the others.

## Bottlenecks found

1. **Fixed (MDV-P1-037): the doctor-day transaction ran SERIALIZABLE.** Its range locks made unrelated doctors and days queue on each other's
   index ranges in `SlotHolds` and `ScheduleSlots`: in the stress run 93.8 % of holds answered `busy` (hold p95 13,010 ms, p50 3,209 ms, a full
   run completed 2 flows). The doctor-day app lock already makes each check-then-write exclusive and the unique index on `ScheduleSlots` stops a
   double booking if it is bypassed, so `DoctorDayTransaction` now runs READ COMMITTED. Stress after: 0 errors, hold p95 106 ms, book p95 140 ms,
   1,502 complete flows in 61 s. Regression test: `NoDoubleBookingTests.DesksHoldingAndBookingDifferentSlotsAtOnceAllSucceedWithoutBusy`.
2. The earlier note that holds at READ COMMITTED left `busy` on book is superseded: the whole transaction (hold, book, walk-in, change) now runs
   at READ COMMITTED, and the test above fails on SERIALIZABLE and passes on READ COMMITTED.
3. The pilot's real rate (200 bookings an hour, about one every 18 s over two clinics) is far below where this appears; it matters for bursts
   (a clinic opening, a campaign) and for the "100 parallel bookings on staging" test of MDV-P1-022.
