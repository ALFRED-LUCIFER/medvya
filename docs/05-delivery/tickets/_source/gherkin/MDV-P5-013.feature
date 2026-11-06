Feature: Regulator e-prescription and e-sick leave; letters and certificates from templates (N-09)
  As a doctor, I want e-prescriptions, sick leave and letters from templates, so that regulated documents are issued digitally.

  # chunk: Add eRx submission
  @todo
  Scenario: Regulator reference stored
    Given an authorised prescription
    When submitted to eRx
    Then a regulator reference is stored on the prescription
    And failures are visible and retryable

  # chunk: Add sick leave and letters
  @todo
  Scenario: Sick leave signed in regulator format
    Given a sick-leave certificate
    When issued
    Then it follows the regulator format and is signed by an authenticated doctor action
  @todo
  Scenario: Letters released to portal
    Given a letter template
    When used
    Then it produces a signed PDF released to the portal
