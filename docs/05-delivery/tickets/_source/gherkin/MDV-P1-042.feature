Feature: R1 pilot (2 consecutive weeks of real bookings), exit gate review and release notes
  As a founder, I want a formal exit gate review, so that R1 is released only with evidence.

  # chunk: Run the pilot and review exit evidence
  @todo
  Scenario: Evidence for all five exit criteria
    Then evidence for each of the five exit criteria is attached
    And confirmed by founder, clinical advisor and one pilot user

  @todo
  Scenario: Holiday slip is formally recorded
    Given the holiday period prevents two consecutive weeks
    Then the gate is formally moved to the first two full January weeks
    And the move is recorded

  # chunk: Publish release notes and pass journey J3
  @todo
  Scenario: R1 release notes
    Then they list P1-01 to P1-12 delivered status, known issues and what is not included (clinical notes, Rx, billing, AI, portal)

  @todo
  Scenario: Journey J3 passes
    Then the J3 front-desk steps (P-01, P-02, P-03) pass in Playwright
