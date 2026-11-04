Feature: Azure Service Bus and .NET AI worker skeleton without database credentials (scoped job + callback)
  As a developer, I want an unprivileged AI worker fed by Service Bus, so that AI processing cannot reach the database.

  # chunk: Add the worker identity and job flow
  @todo
  Scenario: No database permissions
    Given the worker identity
    When the infra test runs
    Then it has no database permissions and cannot call public endpoints beyond the internal API
  @todo
  Scenario: Exactly one result per job
    Given the same job delivered twice
    When processed
    Then exactly one logical result is produced (idempotency test)

  # chunk: Add dead-letter and run records
  @todo
  Scenario: Poison messages dead-lettered
    Given a poison message
    When it fails
    Then it goes to dead-letter with an alert and message bodies contain no PHI
  @todo
  Scenario: AI run recorded
    Given an AI run
    When it completes
    Then it records model ref, prompt hash, schema version and region
