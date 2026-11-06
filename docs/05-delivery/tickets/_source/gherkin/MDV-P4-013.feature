Feature: Doctor revenue and payout report (P-09)
  As a practice admin, I want a doctor revenue and payout report, so that payouts reconcile with billing.

  # chunk: Add doctor revenue and payout report
  @todo
  Scenario: Figures reconcile
    Given a doctor and period
    When the report is run
    Then net (gross − discounts − refunds) and payout reconcile with invoices and payments
  @todo
  Scenario: Admin only
    Given a non-admin user
    When they open the report
    Then they get a 403
  @todo
  Scenario: Versioned payout share
    Given a payout share change
    When it is saved
    Then it is versioned by effective date
