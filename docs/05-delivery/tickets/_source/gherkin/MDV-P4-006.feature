Feature: Refunds and credit notes
  As a receptionist, I want to refund an invoice in full or in part with a credit note, so that corrections are traceable.

  # chunk: Add refunds with credit notes and approval
  @todo
  Scenario: Refund creates credit note
    Given a paid invoice
    When a refund is made
    Then a credit note references the original invoice number
    And dues and reports are updated
  @todo
  Scenario: Provider refund confirmed by webhook
    Given a provider refund
    When it is requested
    Then "Refunded" shows only after the webhook confirms
    And failures are visible
  @todo
  Scenario: Threshold approval and audit
    Given a refund above the configured threshold
    When it is requested
    Then it requires admin approval
    And all refunds are audited
