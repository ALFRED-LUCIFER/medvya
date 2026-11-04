Feature: Spike: reporting store that keeps PHI out of analytics (read replica vs warehouse)
  As a developer, I want a decided reporting store, so that analytics never expose PHI.

  # chunk: Decide the reporting store
  @todo
  Scenario: ADR records the choice
    Given the spike
    When complete
    Then ADR-008 records the choice, cost and PHI controls
  @todo
  Scenario: No unmasked PHI for non-clinical roles
    Given a non-clinical role
    When report queries run
    Then they never run against tables with unmasked PHI
