Feature: Postgres hardening from the security review: narrower grants, DataKeys protection, stable ledger hash, destructive-migration gate
  As developer, I want this delivered, so that the PostgreSQL move is safe to deploy.

  # chunk: Postgres hardening from the security review: narrower grants, DataKeys
  @todo
  Scenario: Check 1
    Given a module role
    When it tries to DELETE a DataKeys row
    Then the database refuses it
  @todo
  Scenario: Check 2
    Given a tenant with identifier rows and no key row
    When the key is requested
    Then the call throws and no new key is created
  @todo
  Scenario: Check 3
    Given the reporting role
    When it reads Invitations, DoctorLicences or Profiles
    Then it is refused or sees only masked columns
  @todo
  Scenario: Check 4
    Given a column is added to a ledger table
    When verify_chain runs
    Then every old chain still verifies
  @todo
  Scenario: Check 5
    Given an app role
    When it deletes from ledger.Chains or TenantIndex
    Then the database refuses it
  @todo
  Scenario: Check 6
    Given a database that already holds identifier rows
    When the national-ID migration runs
    Then it stops with a clear error instead of dropping data
