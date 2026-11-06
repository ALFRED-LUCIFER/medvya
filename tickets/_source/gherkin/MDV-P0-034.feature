Feature: Tenant isolation on Postgres: FORCE row-level security, per-module roles, EF filter kept as layer one
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Tenant isolation on Postgres: FORCE row-level security, per-module rol
  @todo
  Scenario: Check 1
    Given a cross-tenant read or write
    When the EF filter is bypassed
    Then row-level security still blocks it (test fails when the policy is removed)
  @todo
  Scenario: Check 2
    Given the app role
    When it connects
    Then it has no DDL, no BYPASSRLS and only its module schema
  @todo
  Scenario: Check 3
    Given a pooled connection
    When it is reused for another tenant
    Then the tenant setting is reset first
  @todo
  Scenario: Check 4
    Given the facility picker
    When it runs with only a user id
    Then it sees that user's memberships and nothing else
