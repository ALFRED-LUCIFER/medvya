Feature: Medvya subscription: plans, trial, subscription invoices with GST/VAT, seat counts, dunning (N-11)
  As a practice owner, I want plans, a trial and subscription invoices, so that I can pay for Medvya correctly.

  # chunk: Record the pricing decision
  @todo
  Scenario: Pricing ADR
    Given the pricing decision
    When implementation starts
    Then it is recorded in an ADR first

  # chunk: Add trial conversion and prorated seats
  @todo
  Scenario: Trial converts with tax invoice
    Given a trial
    When it converts to paid
    Then a correct tax invoice is issued
  @todo
  Scenario: Seat changes prorate
    Given a seat change
    When billed
    Then it is prorated

  # chunk: Add dunning that never blocks care
  @todo
  Scenario: Dunning keeps records readable
    Given an overdue subscription
    When dunning applies
    Then clinicians can still read existing patient records
    And new features and admin are limited
  @todo
  Scenario: No PHI in control plane
    Given subscription data
    When stored
    Then it lives in the platform control plane with no PHI
