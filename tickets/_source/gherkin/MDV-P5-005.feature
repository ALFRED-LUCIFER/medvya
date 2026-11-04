Feature: Day-one load, change-data-capture parallel run and background backfill (N-04)
  As Medvya operations, I want a day-one load with a parallel run, so that the clinic switches without closing.

  # chunk: Add day-one load with imported badges
  @todo
  Scenario: Counts match signed dry run
    Given the agreed window
    When the day-one load completes
    Then counts match the signed dry run
  @todo
  Scenario: Imported badge
    Given an imported value
    When it is displayed
    Then it carries an "Imported" badge with source system, original id and import date

  # chunk: Add change-data-capture parallel run
  @todo
  Scenario: CDC latency without duplicates
    Given a parallel run
    When legacy changes occur
    Then they appear in Medvya within the agreed latency without duplicates

  # chunk: Add cut-over fallback checklist
  @todo
  Scenario: Fallback until sign-off
    Given the cut-over checklist
    When staff need to fall back
    Then they can at any time until sign-off
