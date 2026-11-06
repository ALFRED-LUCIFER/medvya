Feature: Azure Database for PostgreSQL Flexible Server in Bicep: private endpoint, Entra-only auth, customer-managed key, in-country HA and point-in-time restore
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Azure Database for PostgreSQL Flexible Server in Bicep: private endpoi
  @todo
  Scenario: Check 1
    Given the Bicep
    When it deploys
    Then the server has no public endpoint and password login is off
  @todo
  Scenario: Check 2
    Given a restore drill
    When it runs
    Then point-in-time restore completes within the agreed time and is timed in the runbook
  @todo
  Scenario: Check 3
    Given a resource
    When is created outside the allowed regions
    Then policy denies it
