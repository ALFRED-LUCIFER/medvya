Feature: AI safety card, waiting-room consent notice (EN/HI/AR) and AI incident procedure
  As a founder or clinical advisor, I want safety materials and an incident procedure, so that clinics use AI safely.

  # chunk: Deliver safety materials and incident procedure
  @todo
  Scenario: Materials reviewed and delivered
    Given the safety card and notices
    When reviewed
    Then the clinical advisor and counsel have reviewed them and they are delivered to pilot clinics
  @todo
  Scenario: Kill switch tested
    Given the AI incident procedure
    When tested in a tabletop
    Then it includes a kill-switch flag per tenant and is tested once
  @todo
  Scenario: Evaluation report attached
    Given the R3 release notes
    When published
    Then the evaluation report is attached
