Feature: Legal review of recording consent in each market
  As a founder, I want counsel to review recording consent, so that the flow is lawful in each market.

  # chunk: Obtain counsel review and sign-off
  @todo
  Scenario: Approved texts published
    Given counsel approval
    When complete
    Then approved consent texts per language are published as templates (MDV-P3-002)
  @todo
  Scenario: Flow changes ticketed
    Given any required flow change
    When identified
    Then it is ticketed before P3 scope freeze
  @todo
  Scenario: Sign-off before first recording
    Given the first real recording
    When about to start
    Then sign-off is already recorded
