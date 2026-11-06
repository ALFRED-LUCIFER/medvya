Feature: Allergy verification and patient-reported medicines with handoff to the doctor (N-01)
  As a nurse, I want to confirm every allergy before handing off, so that the doctor never starts with an unverified allergy list.

  # chunk: Add allergy status gate and medicine statements
  @todo
  Scenario: Handoff blocked without allergy status
    Given an allergy without a status
    When the nurse tries to hand off through the UI or API
    Then handoff is impossible
  @todo
  Scenario: No known allergies must be explicit
    Given a patient with no recorded allergies
    When the nurse reaches handoff
    Then "No known allergies" is not the default and must be explicitly confirmed
  @todo
  Scenario: Source and actor stored
    Given allergy statuses and patient-reported medicines
    When they are saved
    Then the source (nurse intake, patient) and actor are stored
  @todo
  Scenario: Handoff updates board and Today
    Given every allergy has a status
    When the nurse hands off
    Then the board and Today update in real time
