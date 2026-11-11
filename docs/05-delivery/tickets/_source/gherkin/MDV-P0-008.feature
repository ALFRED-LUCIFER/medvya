Feature: Database security baseline part 1: Entra-only auth, managed identity, least-privilege users per module and for migrations
  As a developer, I want least-privilege database access, so that a compromised component cannot reach beyond its own data.

  # chunk: Enforce least-privilege roles per module and for migrations
  @done
  Scenario: App identity cannot change schema but migration identity can
    Given roles equivalent to production on a Testcontainers PostgreSQL
    When the app identity tries to ALTER or CREATE objects
    Then it is refused
    And the migration identity can

  @done
  Scenario: A module role cannot read another module's schema
    When a module's database role selects from another module's schema
    Then it is refused

  # chunk: Enforce Entra-only auth and passwordless strict connection strings
  @todo
  Scenario: SQL username and password login fails
    When I attempt SQL authentication with a username and password
    Then it fails because Entra-only authentication is on

  @todo
  Scenario: Connection strings carry no password and enforce strict encryption
    Then connection strings in App Service settings contain no password
    And Encrypt=Strict is enforced

  # chunk: Document just-in-time human access to production data
  @todo
  Scenario: Human production data access is just-in-time only
    Then human access to production data is only via just-in-time elevation
    And a documented runbook links MDV-P0-025
