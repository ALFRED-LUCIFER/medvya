Feature: India payments with Razorpay (UPI collect/QR, card, refunds) and idempotent webhooks (P-10)
  As a receptionist, I want to collect India payments by UPI and card, so that invoices are paid safely without storing card data.

  # chunk: Add Razorpay payment confirmation by verified webhook
  @todo
  Scenario: Paid only after verified confirmation
    Given an invoice awaiting provider confirmation
    When a forged webhook arrives
    Then it is rejected and the invoice is not Paid
    And a verified webhook or provider API confirmation makes it Paid
  @todo
  Scenario: Webhook replay idempotent
    Given a verified webhook
    When it is replayed 10 times
    Then the state changes once

  # chunk: Add card data safety and failure handling
  @todo
  Scenario: No card secrets stored
    Given any payment
    When data and logs are inspected
    Then no card number, CVV or UPI PIN is stored or logged
  @todo
  Scenario: Failure and timeout handling
    Given a payment that fails or times out
    When the result is shown
    Then "Payment failed" appears with retry and record-cash options
