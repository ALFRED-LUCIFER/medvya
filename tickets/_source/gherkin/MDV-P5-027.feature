Feature: Availability monitoring: 30 days at ≥ 99.5 % before GA
  As a developer, I want availability monitored, so that we prove 99.5 % before GA.

  # chunk: Add availability probes and gate
  @todo
  Scenario: Probes every minute
    Given in-country locations
    When probes run
    Then they run every minute and availability is computed per month
  @todo
  Scenario: 30 days at target
    Given the GA gate
    When checked before week 32
    Then 30 consecutive days are ≥ 99.5 % (exit gate 4) or the GA date moves
