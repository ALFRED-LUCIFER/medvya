Feature: Tamper-evident audit and signed records without SQL ledger: hash chain, append-only triggers, daily digest to immutable Blob
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Tamper-evident audit and signed records without SQL ledger: hash chain
  @todo
  Scenario: Check 1
    Given an audit row
    When an UPDATE or DELETE is attempted
    Then the database refuses it (test fails when the trigger is removed)
  @todo
  Scenario: Check 2
    Given a row is altered by a superuser
    When verification runs
    Then the chain mismatch is found and an alert is raised
  @todo
  Scenario: Check 3
    Given the daily digest
    When it is written
    Then it lands in the immutable container and can be verified later
