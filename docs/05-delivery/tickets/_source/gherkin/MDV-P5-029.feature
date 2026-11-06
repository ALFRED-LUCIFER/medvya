Feature: First paying clinic cut-over without closing (week 29) with go-live runbook
  As a clinic owner, I want to switch without closing, so that patients are not disrupted.

  # chunk: Run cut-over and hypercare
  @todo
  Scenario: No closure with fallback
    Given the cut-over
    When it happens
    Then the clinic does not close
    And fallback was available until sign-off (exit gate 1)
  @todo
  Scenario: Documents delivered
    Given the handover
    When complete
    Then the migration report and go-live runbook are delivered (delivery package 2)
  @todo
  Scenario: Hypercare tracked
    Given hypercare issues
    When tracked
    Then they are tracked to closure with the clinic
