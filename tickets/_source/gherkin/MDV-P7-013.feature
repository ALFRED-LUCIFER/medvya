Feature: R7 hospital pilot, clinical sign-off and release
  As a founder or hospital IT lead, I want a verified hospital pilot, so that R7 can be released with clinical sign-off.

  # chunk: Run the pilot and gather the three exit criteria
  @todo
  Scenario: All three exit criteria are evidenced
    Given a pilot on hospital wards with HIS authority
    When the gate is reviewed
    Then evidence exists for integration contract and security review, write-back at least 99 percent with conflicts visible, and hospital clinical sign-off

  @todo
  Scenario: Journey J6 passes
    Given journey J6: failed write-back, retry, conflict, audit
    When the journey test runs
    Then J6 passes

  @todo
  Scenario: Runbook and security evidence pack delivered
    Given the R7 release
    When the delivery package is checked
    Then the integration runbook and the security evidence pack are delivered
