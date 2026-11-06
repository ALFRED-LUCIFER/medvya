Feature: Clinical results inbox: severity in words, review/acknowledge, assign follow-up, release to patient (C-01)
  As a doctor, I want a results inbox with review before follow-up or release, so that no abnormal result is missed.

  # chunk: Add review gate on assign and release
  @todo
  Scenario: Assign and release need review
    Given an unreviewed result
    When assign or release is attempted
    Then the server refuses it
  @todo
  Scenario: Patient sees only released results
    Given an unreleased result
    When the patient opens the portal
    Then the result is not visible

  # chunk: Add critical escalation and pilot reporting
  @todo
  Scenario: Critical escalation
    Given a critical result unreviewed after the configured time
    When the time passes
    Then the doctor and covering clinician are notified
  @todo
  Scenario: Pilot report on unacknowledged abnormal results
    Given the pilot report
    When it is run
    Then it shows 0 abnormal results unacknowledged > 72 h or lists breaches with actions (exit gate 3)
