Feature: Clip capture bound to encounter and consent with visible recording light (capture FSM)
  As a doctor, I want the clip to record only for the current consented encounter with a visible light, so that patients can trust when they are being recorded.

  # chunk: Authorise capture only for an active consented encounter
  @todo
  Scenario: Clip cannot record without encounter and consent
    Given a paired clip and an encounter without consent authorisation
    When the doctor tries to start capture
    Then the server refuses the session
    And the firmware refuses to record

  @todo
  Scenario: Session start is device-bound with a nonce
    Given an active encounter with consent
    When the doctor starts capture on the clip
    Then a device-bound session is started with a nonce
    And the session cannot be attached to a second encounter

  # chunk: Keep the recording light on during capture
  @todo
  Scenario: Recording light is on whenever the microphone captures
    Given the clip is capturing
    When the hardware test samples the microphone and the LED
    Then the LED is on whenever the microphone captures

  # chunk: Reject replayed or out-of-sequence chunks
  @todo
  Scenario: Replayed or out-of-sequence chunks are rejected
    Given chunks are uploaded with integrity hashes via phone relay or Wi-Fi
    When a chunk is replayed or arrives out of sequence
    Then it is rejected

  @todo
  Scenario: Device, consent and overlap suites pass
    Given the device, consent and overlap test suites
    When they run in CI
    Then they all pass (exit gate 1)
