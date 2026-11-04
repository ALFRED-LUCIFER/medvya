Feature: Dues, ageing and patient statement with no-PHI balance reminders (P-12)
  As a receptionist, I want a dues list with ageing and statements, so that pending balances are collected.

  # chunk: Add dues ageing, statements and reminders
  @todo
  Scenario: Ageing buckets
    Given dues of different ages
    When the list is viewed and filtered
    Then buckets 0–30, 31–60, 61–90 and 90+ are correct and filterable
  @todo
  Scenario: Statements reconcile
    Given a patient statement
    When it is compared with invoices and payments
    Then it reconciles exactly
  @todo
  Scenario: Reminders consent and no clinical content
    Given a balance reminder
    When it is sent
    Then it respects channel consent
    And contains no clinical information
