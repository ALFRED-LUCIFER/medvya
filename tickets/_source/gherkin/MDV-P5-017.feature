Feature: Platform operations console: tenant catalogue, onboarding automation, per-tenant flags, time-boxed audited support access visible to the clinic (N-12)
  As Medvya operations, I want a console with clinic-approved support access, so that support is safe and visible.

  # chunk: Add time-boxed support access
  @todo
  Scenario: Grant required
    Given support staff
    When no approved time-limited grant exists
    Then they cannot see tenant data
    And access ends automatically
  @todo
  Scenario: Actions in clinic access log
    Given a support action
    When performed
    Then it is audited and listed in the clinic's access log (F-12)

  # chunk: Add per-tenant flags and PHI-free control plane
  @todo
  Scenario: Flags audited
    Given the console
    When a flag is toggled for a tenant
    Then it is audited
  @todo
  Scenario: No PHI in control plane
    Given the control plane
    When inspected
    Then it stores no PHI and the data map is updated
