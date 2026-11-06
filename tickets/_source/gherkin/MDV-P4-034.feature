Feature: UPI AutoPay for packages, secure pay link by WhatsApp, Apple Pay and Google Pay in the UAE
  As receptionist, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: UPI AutoPay for packages, secure pay link by WhatsApp, Apple Pay and G
  @todo
  Scenario: Check 1
    Given a pay link is sent
    When the WhatsApp message is read
    Then it shows only the clinic name and amount
  @todo
  Scenario: Check 2
    Given an AutoPay charge is due
    When 24 hours before
    Then the patient is reminded
  @todo
  Scenario: Check 3
    Given the gateway has not confirmed
    When the desk looks at the invoice
    Then it is not marked paid
