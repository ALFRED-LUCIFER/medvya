Feature: Hardware partner, device certification and regulated-device classification advice
  As a founder, I want a chosen hardware partner, certification plan and classification advice, so that the clip can be built and sold legally.

  # chunk: Resolve the hardware partner and classification questions
  @todo
  Scenario: Partner contract and certification plan are recorded
    Given the hardware partner has been selected
    When the founder opens the hardware decision record
    Then the partner contract and bill of materials are recorded
    And a certification plan with dates exists for each country

  @todo
  Scenario: Classification advice covers the final intended use
    Given the intended use is a recording accessory with human-approved documentation
    When the founder reviews the written classification advice for India (CDSCO) and the UAE
    Then the advice covers the final intended use and the marketing claims

  @todo
  Scenario: Go or no-go is recorded against phone-capture metrics
    Given the phone-capture metrics from P3 are available
    When the founder records the hardware decision
    Then a go or no-go decision is recorded against those metrics
