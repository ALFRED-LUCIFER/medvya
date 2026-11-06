Feature: Charge capture and invoice lifecycle (draft → issued → paid/failed/refunded), separate from clinical sign-off (P-06)
  As a receptionist, I want to capture charges and issue invoices separately from clinical sign-off, so that billing never alters clinical records.

  # chunk: Add invoice states and server-enforced actions
  @todo
  Scenario: State machine enforced
    Given an invoice in any state
    When an action is attempted
    Then only actions allowed by the invoice state machine succeed on the server
  @todo
  Scenario: Issuing assigns legal number
    Given a draft invoice
    When it is issued
    Then the legal number is taken from DOC_SEQUENCE
  @todo
  Scenario: Issued invoices immutable
    Given an issued invoice
    When a correction is needed
    Then it is made by a credit note and the invoice is unchanged

  # chunk: Add discounts with reason and clinical-data separation
  @todo
  Scenario: Discounts need a reason
    Given an invoice line
    When a discount is applied without a reason
    Then it is refused
    And a discount with a reason is audited
  @todo
  Scenario: No clinical data on billing screens
    Given billing screens and their payloads
    When the security test inspects them
    Then no clinical notes or diagnoses appear

  # chunk: Add India and UAE rounding rules
  @todo
  Scenario: India round-off line
    Given an India invoice with a payable total
    When it is issued
    Then lines, tax and totals are exact to the paisa
    And a separate Round off line rounds the payable total to the nearest rupee, 50 paise or more up and less than 50 paise down
    And the e-invoice RndOffAmt carries the same amount
  @todo
  Scenario Outline: India round-off examples
    Given an India invoice total
    When the total is <total>
    Then the round-off is <roundoff>
    Examples:
      | total | roundoff |
      | ₹1,234.50 | +₹0.50 |
      | ₹1,234.49 | -₹0.49 |
  @todo
  Scenario: UAE exact to the fils
    Given a UAE invoice
    When it is issued
    Then lines, VAT and totals are exact to the fils
    And no invoice-level rounding is applied
