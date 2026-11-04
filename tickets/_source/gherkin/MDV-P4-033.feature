Feature: R4 pilot, exit gate review and release notes
  As the founder, I want the R4 exit gate reviewed with evidence, so that we release safely.

  # chunk: Review R4 exit gate and publish release notes
  @todo
  Scenario: Evidence for four exit criteria
    Given the pilot
    When the gate is reviewed
    Then evidence for all four exit criteria is attached and confirmed
  @todo
  Scenario: Clinical safety sign-off
    Given the results loop and portal release rules
    When reviewed
    Then the clinical safety reviewer signs the release
  @todo
  Scenario: Release notes
    Given the R4 release
    When notes are published
    Then they list P4-01…P4-10 status and exclusions (insurance claims, teleconsultation)
  @todo
  Scenario: Journeys pass
    Given the build
    When Playwright runs
    Then the J1 billing step and J5 delayed-result journey pass
