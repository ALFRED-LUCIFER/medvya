Feature: Re-baseline load and desk tests on Postgres
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Re-baseline load and desk tests on Postgres
  @todo
  Scenario: Check 1
    Given 100 parallel bookings for one slot
    When they run
    Then exactly one succeeds
  @todo
  Scenario: Check 2
    Given the desk load profile
    When it runs
    Then the p95 budgets in load-testing.md still hold or are updated with evidence
