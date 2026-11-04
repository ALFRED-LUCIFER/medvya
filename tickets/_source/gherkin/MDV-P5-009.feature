Feature: Pre-authorisation, claims, resubmission and remittance matching — India TPA/PMJAY/CGHS (P-14)
  As a receptionist, I want to manage pre-auth and claims, so that insurer payments are tracked.

  # chunk: Add pre-authorisation with required documents
  @todo
  Scenario: Documents required
    Given a pre-auth request
    When required documents are missing
    Then it cannot be submitted
  @todo
  Scenario: Signed documents only
    Given a document to attach
    When it is clinical
    Then only signed clinical documents can be attached

  # chunk: Add claims list and resubmission
  @todo
  Scenario: Actions needed shown
    Given claims
    When the list is viewed
    Then it shows actions needed (rejected with reason, query) and resubmission history

  # chunk: Add remittance matching
  @todo
  Scenario: Remittance reconciles
    Given a remittance advice
    When matched
    Then amounts reconcile to invoices and short payments are flagged
