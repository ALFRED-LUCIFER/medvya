Feature: Emergency-symptom warning as a prompt, not triage (A-08)
  As a doctor, I want a prompt when an emergency phrase is heard, so that I can decide, with the system never acting on its own.

  # chunk: Add the emergency prompt
  @todo
  Scenario: Prompt changes nothing automatically
    Given an emergency phrase is detected
    When the prompt appears
    Then no orders, notes or patient messages change automatically
  @todo
  Scenario: Response recorded
    Given the prompt
    When the doctor chooses Acknowledge or Not relevant
    Then actor, time and reason are recorded
  @todo
  Scenario: Phrase list versioned and tested
    Given the phrase list
    When reviewed
    Then it is versioned, clinically reviewed, and detection is tested in en, hi and ta
