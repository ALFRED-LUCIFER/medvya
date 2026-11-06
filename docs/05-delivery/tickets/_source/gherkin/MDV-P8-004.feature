Feature: India ABDM: ABHA consent requests and linked records from other facilities
  As a doctor or patient, I want consented ABDM records from other facilities, so that care is informed without losing control.

  # chunk: Pass ABDM sandbox certification
  @todo
  Scenario: Sandbox certification is passed
    Given the ABDM sandbox and current implementation guides
    When certification is run
    Then it is passed

  # chunk: Request consent and link care contexts
  @todo
  Scenario: Consent request is approved on the patient's phone
    Given a patient who used Scan & Share at reception
    When a consent request is sent
    Then the patient approves it on their phone

  @todo
  Scenario: Medvya visits are linked as care contexts
    Given a completed Medvya visit
    When care-context linking runs
    Then the visit is linked as a care context

  # chunk: Show linked records with provenance
  @todo
  Scenario: Linked records show provenance and are not merged without acceptance
    Given FHIR bundles received for a consented period
    When the doctor views them
    Then they display with ABDM provenance and consent period
    And they are never merged into verified data without clinician acceptance
