Feature: Patient app: results, prescriptions, messages, consents, payments and video visit
  As patient, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Patient app: results, prescriptions, messages, consents, payments and 
  @todo
  Scenario: Check 1
    Given a result is not reviewed
    When the patient opens Records
    Then it shows as pending, not as a value
  @todo
  Scenario: Check 2
    Given a payment
    When the bank has not confirmed
    Then the invoice is not marked paid
  @todo
  Scenario: Check 3
    Given a draft AI note exists
    When the patient opens Records
    Then nothing of it is visible
  @todo
  Scenario: Check 4
    Given video visits are not enabled for the clinic
    When the patient opens the app
    Then no video entry is shown
