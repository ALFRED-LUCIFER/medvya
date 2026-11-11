Feature: Append-only audit log on hash-chained PostgreSQL tables with audit middleware
  As a practice admin, I want every patient-data access recorded tamper-evidently, so that access is provable.

  # chunk: Audit every patient-data request, including refusals
  @done
  Scenario: One audit event per patient-data request
    When a test enumerates endpoints tagged as patient-data read or write and calls each
    Then each produces exactly one audit event per request

  @done
  Scenario: Denied attempts are audited
    When an authorization attempt is denied
    Then an audit event with outcome "denied" is written

  # chunk: Make the audit log append-only and tamper-evident
  @done
  Scenario: App identity cannot change audit rows
    When the app identity tries to UPDATE or DELETE audit rows
    Then it is refused

  @done
  Scenario: Ledger verification detects tampering
    Given a tampered row in a test database
    When ledger verification runs
    Then the tamper is detected

  # chunk: Scan audit payloads for PHI
  @todo
  Scenario: Audit events hold opaque patient ids only
    When a test scans audit payloads for names, phone numbers and identifiers
    Then none are found and the test fails if any appear

  # chunk: Export and verify daily ledger digests
  @todo
  Scenario: Daily digest export to immutable storage
    When the daily digest export runs
    Then it succeeds into an immutable (time-based retention) container

  @todo
  Scenario: Scheduled verification alerts on mismatch
    When the scheduled verification finds a mismatch
    Then an alert is raised
