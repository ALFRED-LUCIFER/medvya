Feature: Nurse intake: one confirmation for all flagged readings, and a correction with a reason until the doctor starts
  BP 168/96, pulse 104 and temperature 39.2 each needed Keep this value: four taps before hand-off, most for the sickest patients; habitual tapping weakens the check.

  # chunk: Nurse intake: one confirmation for all flagged readings, and a correction with a reason until the doctor starts
  @todo
  Scenario: One confirmation for all flags
    Given three flagged readings
    When the nurse confirms once
    Then all three are kept and the record shows each was confirmed

  @todo
  Scenario: Each can be re-measured
    Given the flagged list
    When the nurse re-measures one
    Then only that reading changes

  @todo
  Scenario: Correct before the doctor starts
    Given a handed-off intake and a consultation not yet started
    When the nurse corrects the weight with a reason
    Then the new value shows marked corrected and the old one is kept with who and when

  @todo
  Scenario: Fixed once the doctor starts
    Given a started consultation
    When a correction is sent
    Then the server refuses it, and the test fails if that rule is removed

  @todo
  Scenario: Flags in words
    Given a flagged reading
    When it renders
    Then High or Low and the range are text, not colour alone

  @todo
  Scenario: Tablet, languages, axe
    Given 1024 touch in English and Arabic
    When axe runs
    Then there is no violation and every control is at least 44 px
