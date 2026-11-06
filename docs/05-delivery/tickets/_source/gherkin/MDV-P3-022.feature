Feature: AI vendor DPA, no-training guarantee and regional processing confirmed per country
  As a founder, I want vendor terms and region confirmed, so that AI processing is lawful and in-country.

  # chunk: Record vendor terms and verify region
  @todo
  Scenario: Terms recorded per country
    Given each country
    When reviewed
    Then DPA/terms references and abuse-monitoring status are recorded in docs/compliance/ai-vendors.md
  @todo
  Scenario: Sub-processor list updated
    Given the trust centre
    When updated
    Then the sub-processor list shows region and purpose (30-day notice rule acknowledged)
  @todo
  Scenario: Region configuration check
    Given the AI resources
    When the configuration check runs
    Then they live only in the approved region
