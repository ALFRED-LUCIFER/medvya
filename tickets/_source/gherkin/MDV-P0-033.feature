Feature: One baseline migration per module on Postgres, regenerated seeder and ERD, forward-only after that
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: One baseline migration per module on Postgres, regenerated seeder and 
  @todo
  Scenario: Check 1
    Given an empty Postgres database
    When migrations run
    Then every module schema exists from its single baseline
  @todo
  Scenario: Check 2
    Given the ERD
    When the baseline merges
    Then `docs/03-architecture/erd.md` is regenerated and CI is green
  @todo
  Scenario: Check 3
    Given a schema-changing PR
    When is open during the freeze
    Then it is rebased onto the baseline before merge
