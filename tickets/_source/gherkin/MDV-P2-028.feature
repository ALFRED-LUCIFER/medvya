Feature: R2 pilot, exit gate review (≥ 20 signed visits per doctor) and release notes
  As a founder or clinical safety reviewer, I want an exit gate review of the R2 pilot, so that R2 ships only with evidence.

  # chunk: Run the R2 exit gate review
  @todo
  Scenario: Evidence for all exit criteria
    Given the R2 pilot
    When the gate review is held
    Then evidence for all four exit criteria is attached and confirmed
  @todo
  Scenario: Safety reviewer sign-off
    Given R2
    When released
    Then the clinical safety reviewer sign-off is recorded
  @todo
  Scenario: Release notes list status and exclusions
    Given the R2 release notes
    When published
    Then they list P2-01 to P2-08 status and exclusions (AI drafting, billing, results inbox, regulator e-prescription)
  @todo
  Scenario: Non-AI journeys pass
    Given the non-AI path (consent declined, manual note)
    When Playwright journeys J2 and J3 run
    Then they pass
