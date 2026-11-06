Feature: Multi-speaker transcript review with speaker confirmation (A-04)
  As a doctor, I want to confirm speakers in the transcript, so that statements are attributed to the right person.

  # chunk: Add speaker confirmation
  @todo
  Scenario: Labels stay provisional
    Given a diarised transcript
    When the doctor has not confirmed
    Then speaker labels remain provisional and never assert patient identity automatically
  @todo
  Scenario: Companions keep their own statements
    Given a family member speaks
    When attribution is shown
    Then they do not inherit the patient identity and their statements are attributed to the companion
  @todo
  Scenario: Confirmations recorded and propagated
    Given a confirmation
    When saved
    Then actor and time are recorded and it propagates to draft attribution

  # chunk: Track transcript edits
  @todo
  Scenario: Edits versioned
    Given a transcript text edit
    When saved
    Then it is versioned and visible in the evidence panel
