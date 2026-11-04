Feature: UAE payments (Network International or Stripe) and card-on-terminal recording (P-10)
  As a receptionist, I want UAE card and terminal payments recorded, so that collections reconcile at day close.

  # chunk: Add UAE provider adapter and card-on-terminal method
  @todo
  Scenario: Provider ADR and contract tests
    Given the UAE provider decision
    When the adapter is tested
    Then the choice is recorded in an ADR
    And the adapter passes the same contract tests as Razorpay
  @todo
  Scenario: Card on terminal
    Given a card payment on a terminal
    When it is recorded
    Then only reference and amount are stored
    And it reconciles in day close
  @todo
  Scenario: Co-pay linked to insurer invoice
    Given an insurer-pending invoice
    When co-pay is collected
    Then the collection is linked to that invoice
