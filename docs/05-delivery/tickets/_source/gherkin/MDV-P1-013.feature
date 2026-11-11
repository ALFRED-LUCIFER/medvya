Feature: Patient registration - India full flow (identity, ABHA optional, Aadhaar last 4, Indian address, payer)
  As a receptionist, I want to register an Indian patient through a guided flow, so that demographics, IDs and payer are captured safely.

  # chunk: Add India registration rules and validation
  @done
  Scenario: Minimum to register in India
    When a receptionist registers a patient with name, DOB or age, sex and mobile only
    Then registration is accepted
    And other fields stay optional
    And validation messages match the design, for example "PIN code must be 6 digits"

  @done
  Scenario: Aadhaar stored as last 4 only and ABHA encrypted
    When 12 Aadhaar digits are entered
    Then they are rejected with an explanation
    And ABHA is stored with envelope encryption (ADR-012) and is never required for care

  # chunk: Add registration save with success screen
  @done
  Scenario: Saving writes records in one transaction
    When a registration is saved
    Then PATIENT, PATIENT_IDENTIFIER (tokenised) and PAYER_COVERAGE are written in one transaction
    And an audit event PatientRegistered.v1 with no PHI in the payload is written
    And a facility-scoped MRN is assigned

  @done
  Scenario: Success screen offers next actions
    When registration completes
    Then the screen shows name, MRN, timestamp and actor
    And offers Book appointment, Check in now (walk-in) and Register another

  # chunk: Protect clinical data and make form accessible
  @done
  Scenario: Receptionist sees no clinical data
    Given a receptionist
    When they use registration or directory screens
    Then no clinical data is visible (security test)

  @done
  Scenario: Form is accessible and tablet friendly
    Then the form is keyboard-completable
    And errors are announced via aria-live
    And it works at tablet width

  # chunk: Add consent receipt to the registration transaction
  @todo
  Scenario: Consent receipt is written with registration
    When a registration is saved
    Then CONSENT_RECEIPT records are written in the same transaction
