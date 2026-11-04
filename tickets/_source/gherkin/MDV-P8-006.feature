Feature: Front-desk extras: kiosk/QR self check-in, clinic letter inbox, offline read and draft (N-16)
  As a patient, receptionist or doctor, I want self check-in, a letter inbox and offline chart access, so that front-desk work and care continue smoothly.

  # chunk: Resolve and approve the N-16 design
  @todo
  Scenario: Designs are approved before build
    Given N-16 needs design and offline mode has security implications
    When the designs are reviewed
    Then they are approved before build starts

  # chunk: Self check-in by tablet or QR
  @todo
  Scenario: Patient self checks in
    Given a patient at a tablet or QR code
    When they confirm details and consent
    Then they are checked in

  # chunk: Match inbound clinic letters
  @todo
  Scenario: Inbound letters use the same identity-match rules as results
    Given a letter arrives in the clinic email inbox
    When it is auto-matched
    Then the same identity-match rules as results apply
    And unmatched letters go to a "needs filing" queue

  # chunk: Read and draft offline securely
  @todo
  Scenario: Offline data is encrypted, time-limited and wiped on sign-out
    Given a doctor reads today's charts and drafts notes offline
    When time passes or the doctor signs out
    Then the local data is encrypted, time-limited and wiped on sign-out

  @todo
  Scenario: Sync conflicts are surfaced
    Given a note edited offline and changed elsewhere
    When the device syncs
    Then the conflict is surfaced to the user
