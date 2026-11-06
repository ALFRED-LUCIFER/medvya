Feature: Hospital procurement, HIS vendor engagement and integration contract
  As a founder or hospital IT lead, I want a signed contract and agreed integration specification, so that the first hospital integration can proceed.

  # chunk: Resolve the first hospital and HIS vendor agreement
  @todo
  Scenario: Contract and integration specification are signed
    Given the first hospital and its HIS vendor are engaged
    When the contract and specification are reviewed
    Then a signed hospital contract exists
    And the integration specification covers FHIR R4 resources or HL7 messages, auth, environments, error semantics and receipts

  @todo
  Scenario: Hospital security review is passed
    Given the hospital has reviewed Medvya security
    When the review concludes
    Then the security review is recorded as passed (exit gate 1)
