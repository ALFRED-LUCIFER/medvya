Feature: Sign and finalise: immutable signed note v1 with re-authentication (E-06)
  As a doctor, I want to sign a note with re-authentication into an immutable record, so that signed notes can be trusted.

  # chunk: Add the sign modal with step-up and eligibility
  @todo
  Scenario: Only eligible doctors sign
    Given a doctor without a verified licence or care relationship
    When they try to sign
    Then signing is refused
    And step-up authentication within the last 5 minutes is required

  # chunk: Add immutable ledger storage and hash
  @todo
  Scenario: Signed notes cannot be modified
    Given a signed note
    When an automated test tries direct API and repository updates
    Then both fail (exit gate 3)
  @todo
  Scenario: Hash verifies
    Given a signed note v1
    When the stored hash is compared with the rendered content
    Then it verifies and ledger verification passes

  # chunk: Add events and audit after signing
  @todo
  Scenario: Signing events
    Given a note is signed
    When signing completes
    Then NoteSigned.v1 (no PHI) and an audit event are written
    And Today removes it from pending notes
