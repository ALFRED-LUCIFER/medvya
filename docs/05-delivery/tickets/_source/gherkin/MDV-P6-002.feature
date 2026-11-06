Feature: Clip pairing with code match and microphone self-test (W-01)
  As a doctor, I want to pair my clip by matching a code and testing its microphone, so that only my verified clip records my consultations.

  # chunk: Pair the clip only after the codes match
  @todo
  Scenario: Matching code pairs the clip
    Given a doctor holds the clip button for 3 seconds and its LED pulses
    And the companion shows a 4-digit code
    When the doctor confirms the code matches the clip's LED pattern
    Then the pairing continues to the microphone self-test

  @todo
  Scenario: Mismatched code aborts and is logged
    Given the companion shows a 4-digit code
    When the doctor reports that the code does not match the LED pattern
    Then the pairing is aborted
    And the mismatch is logged

  # chunk: Run the microphone self-test and finish pairing
  @todo
  Scenario: Failed self-test blocks pairing with guidance
    Given the doctor is asked to say "testing"
    When the microphone self-test fails
    Then the pairing is blocked
    And the companion shows guidance on what to do next

  @todo
  Scenario: Successful pairing lists the clip at consent
    Given the self-test has passed
    When the pairing completes
    Then the clip is paired to the doctor with a 90 day validity
    And it appears in the A-02 device list with battery and firmware

  # chunk: Expire pairings after 90 days
  @todo
  Scenario: Pairing expires after 90 days
    Given a clip was paired 90 days ago
    When the doctor tries to use the clip
    Then re-pairing is required
