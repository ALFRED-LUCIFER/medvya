Feature: Health-exchange pull and push for the launch emirate (Malaffi / NABIDH / Riayati) (N-07)
  As a doctor, I want exchange history pulled and signed visits pushed, so that care is joined up.

  # chunk: Add exchange conformance and pull
  @todo
  Scenario: Conformance passed
    Given the launch emirate programme
    When conformance tests run
    Then they pass and evidence is stored
  @todo
  Scenario: Exchange provenance
    Given pulled data
    When shown
    Then it has "Exchange" provenance
    And is never merged into verified chart data without clinician acceptance

  # chunk: Add push with receipts and status screen
  @todo
  Scenario: Push with receipts and retry
    Given a signed visit
    When pushed
    Then a receipt is kept
    And failures retry and are visible in N-07

  # chunk: Keep India launch unblocked
  @todo
  Scenario: India not blocked
    Given exchange work slips
    When India launch is assessed
    Then India launch is not blocked
