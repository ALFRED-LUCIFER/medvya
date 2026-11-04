Feature: Nurse intake queue and vitals with out-of-range re-measure and escalation (N-01)
  As a nurse, I want to record vitals with clear out-of-range prompts and escalation, so that unsafe values are re-checked or raised to the doctor.

  # chunk: Add vitals entry with plausibility checks and flags
  @todo
  Scenario: Reject impossible values
    Given the vitals form
    When a physiologically impossible value is entered
    Then it is rejected
  @todo
  Scenario: Flag out-of-range values in words
    Given a value outside the configured range
    When it is entered
    Then it is flagged with text, not colour only
  @todo
  Scenario: Flagged value needs a decision
    Given a flagged value
    When the nurse tries to hand off
    Then they must re-measure or choose an explicit "keep value"

  # chunk: Add escalate to doctor and saving as observations
  @todo
  Scenario: Escalate with reason
    Given a nurse escalating
    When they submit without a reason
    Then it is refused
    And with a reason the urgent item appears on the treating doctor's Today within 2 seconds
  @todo
  Scenario: Saved vitals become observations
    Given saved vitals
    When the doctor opens Patient 360
    Then they appear as observations with source "Nurse intake", verified by the nurse

  # chunk: Make the intake tablet-first
  @todo
  Scenario: Touch targets
    Given a tablet
    When the intake screen is used
    Then all targets are at least 44 px
