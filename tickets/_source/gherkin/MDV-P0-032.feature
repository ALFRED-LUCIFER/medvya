Feature: Swap the platform to PostgreSQL: Npgsql provider, Testcontainers.PostgreSql, Hangfire.PostgreSql, local services and CI
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Swap the platform to PostgreSQL: Npgsql provider, Testcontainers.Postg
  @todo
  Scenario: Check 1
    Given the solution
    When it builds
    Then no SqlServer package or type remains and warnings stay at zero
  @todo
  Scenario: Check 2
    Given CI
    When it runs
    Then the integration tests run against PostgreSQL in Testcontainers
  @todo
  Scenario: Check 3
    Given local services
    When they start
    Then PostgreSQL, Azurite and the email sink come up with one command
