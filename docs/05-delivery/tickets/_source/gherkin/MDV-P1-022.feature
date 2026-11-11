Feature: No double booking under concurrency (unique index, serializable transaction, doctor-day app lock)
  As the system, I want the database to guarantee slot integrity, so that no slot is ever double booked.

  # chunk: Add concurrency guards for single and multi-slot bookings
  @done
  Scenario: 100 parallel attempts at one slot
    When 100 parallel booking attempts target one slot on Testcontainers PostgreSQL
    Then exactly one succeeds
    And 99 receive a clear "slot no longer available" error

  @done
  Scenario: Overlapping multi-slot bookings are rejected
    Given a multi-slot booking exists
    When an overlapping multi-slot booking is attempted
    Then it is rejected the same way

  @done
  Scenario: Deadlocks and lock timeouts retry safely
    When a deadlock or lock timeout occurs
    Then the booking is retried safely without creating duplicates

  # chunk: Run the concurrency test on staging and in CI
  @todo
  Scenario: The 100-parallel test also runs on staging
    When the 100-parallel booking test runs on staging
    Then exactly one succeeds

  @todo
  Scenario: Test runs in CI for Scheduling changes
    When a PR touches Scheduling
    Then the concurrency test runs in CI
