Feature: Per-encounter recording consent with device selection; decline goes straight to manual note (A-02, E-04)
  As a doctor, I want per-encounter recording consent with device choice, so that no recording happens without the patient's agreement.

  # chunk: Enforce consent before recording
  @todo
  Scenario: Recording needs given consent
    Given an encounter without a "given" consent receipt
    When recording is started
    Then the server refuses it (FSM CONSENT_VERIFIED)
  @todo
  Scenario: Every session references a receipt
    Given recording sessions
    When stored
    Then 100 % reference a consent receipt (database constraint and test; exit gate 3)
  @todo
  Scenario: Per-encounter, not implied
    Given treatment consent at registration
    When a new encounter begins
    Then recording consent is still required for that encounter

  # chunk: Add the decline path and microphone guidance
  @todo
  Scenario: Decline leads to manual note
    Given the patient declines
    When the doctor records the decision
    Then the manual note opens with the same sign flow (Playwright test)
    And no guilt copy is shown
  @todo
  Scenario: Microphone denied
    Given microphone permission is denied
    When the browser reports it
    Then guidance is shown and the manual note is offered
