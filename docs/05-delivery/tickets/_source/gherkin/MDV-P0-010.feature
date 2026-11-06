Feature: Tenancy model with EF Core global filters and Azure SQL row-level security (cross-tenant test in CI)
  As a staff member, I want to see only my clinic's data, so that patient data never leaks across tenants.

  # chunk: Block cross-tenant reads and writes with filters and row-level security
  @done
  Scenario: Cross-tenant reads return nothing even without the EF filter
    Given two tenants with data
    When user A of tenant 1 queries any tenant-owned table, even with IgnoreQueryFilters()
    Then no row of tenant 2 is returned

  @done
  Scenario: Cross-tenant writes are blocked
    When a row is inserted or updated with another tenant's tenant_id
    Then the BLOCK predicate blocks it

  # chunk: Scope sessions to the active facility and reset on pooled connections
  @done
  Scenario: User with two memberships switches facility
    Given a user with memberships in two clinics
    Then they see only the active facility's tenant data
    When they switch facility
    Then session context is re-established

  @done
  Scenario: Session context does not leak across pooled connections
    When a test opens 100 pooled connections alternating tenants
    Then there are zero leaks

  # chunk: Run the cross-tenant test in CI on every PR
  @done
  Scenario: Cross-tenant test gates every PR
    When CI runs on a pull request
    Then the cross-tenant read/write test runs
    And any leak fails the build
