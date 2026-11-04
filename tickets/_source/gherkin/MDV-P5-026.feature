Feature: Support model: first response under 4 business hours, on-call rota and runbooks
  As the founder, I want a support model with SLAs, so that clinics get timely help.

  # chunk: Establish support model
  @todo
  Scenario: SLA matrix published
    Given the support model
    When published
    Then the SLA and severity matrix is available to customers
  @todo
  Scenario: Response time measured
    Given the pilot month
    When measured
    Then ≥ 95 % of first responses are within 4 business hours
  @todo
  Scenario: Restore runbook by second person
    Given the restore runbook
    When a second person executes it
    Then it is verified once
