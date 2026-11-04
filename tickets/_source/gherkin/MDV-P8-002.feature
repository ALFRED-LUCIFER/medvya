Feature: Teleconsultation: video visit with identity check and ambient notes (A-11)
  As a doctor or patient, I want a video visit with identity check and ambient notes, so that remote consultations are safe and documented.

  # chunk: Resolve telemedicine rules and the video vendor
  @todo
  Scenario: Country telemedicine requirements are documented and met
    Given telemedicine rules differ per country and the video vendor is open
    When the rules and vendor are decided
    Then requirements per country are documented and met

  # chunk: Verify identity before the visit
  @todo
  Scenario: Identity check is recorded before the consultation starts
    Given a patient joining through the portal
    When the identity check is completed
    Then it is recorded
    And the consultation can start only afterwards

  # chunk: Record with consent and take ambient notes
  @todo
  Scenario: Recording needs its own consent
    Given a video visit with recording toggles
    When the patient declines recording consent
    Then no recording is made
    And the doctor writes a manual note

  @todo
  Scenario: Ambient notes are a draft for the clinician
    Given recording consent was given
    When the P3 pipeline drafts notes from the visit
    Then the notes are a draft until a licensed clinician accepts each field and signs
