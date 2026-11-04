Feature: Consent revoked mid-visit stops capture and discards later audio (A-09)
  As a doctor, I want withdrawal of consent to stop capture and discard later audio, so that the patient's choice is honoured at once.

  # chunk: Stop capture and discard on revocation
  @todo
  Scenario: Server rejects chunks after revocation
    Given consent is revoked for a session
    When further chunks arrive
    Then the server rejects them
  @todo
  Scenario: Later audio deleted and recorded
    Given audio after the revocation timestamp
    When revocation is processed
    Then it is deleted and the deletion is recorded
  @todo
  Scenario: Pending jobs respect revocation
    Given pending jobs
    When they run
    Then they re-check consent and do not process revoked ranges (worker test)
  @todo
  Scenario: Revocation message and continue
    Given recording stopped
    When the doctor looks at the screen
    Then it shows "Recording stopped at {t}. Audio after withdrawal was discarded." with a "Continue manually" action
