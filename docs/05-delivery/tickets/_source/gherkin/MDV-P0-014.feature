Feature: Server-side authorisation from the Foundations role-permission matrix
  As a practice admin, I want every action checked on the server against the role matrix, so that hiding things in the UI is never the only control.

  # chunk: Enforce policies and matrix on every endpoint
  @done
  Scenario: Every endpoint declares a policy
    When a test enumerates all API endpoints
    Then each has an explicit authorization policy or is on the public allow-list
    And an accidentally anonymous endpoint fails the test

  @done
  Scenario: Capabilities match the Foundations matrix cell by cell
    When the table-driven test generated from docs/02-design/data/foundations.json runs
    Then capabilities per role match the matrix cell by cell

  # chunk: Deny and audit receptionist access to clinical notes
  @todo
  Scenario: Receptionist cannot call a clinical-note endpoint
    When a receptionist calls a clinical-note endpoint
    Then the response is 403 regardless of UI
    And the attempt is audited as "Denied"

  # chunk: Restrict custom roles to existing capabilities
  @todo
  Scenario: Custom roles use only existing, unlocked capabilities
    When a custom role is composed
    Then it can only use existing capabilities
    And locked capabilities from a built-in role cannot be granted

  # chunk: Publish the role matrix doc
  @todo
  Scenario: Role matrix is published and linked
    Then docs/02-design/sitemap-and-permissions.md is the published role matrix
    And it is linked from the R0 release notes
