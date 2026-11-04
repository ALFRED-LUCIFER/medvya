Feature: SMS confirmations and reminders with India DLT templates and delivery receipts
  As a patient, I want SMS confirmations and reminders from my clinic, so that I am reminded even without email.

  # chunk: Add DLT-compliant SMS sending
  @todo
  Scenario: Only registered templates are sent
    When a message does not match a registered template
    Then it is not sent (validation test)

  @todo
  Scenario: Messages have no clinical details
    Then SMS messages contain no clinical details
    And the sender name is the clinic's

  # chunk: Add delivery receipts and failure visibility
  @todo
  Scenario: Delivery receipts update status
    When a delivery receipt webhook arrives
    Then it is verified and authenticated
    And the status is updated
    And failures are visible to reception

  # chunk: Add reminder delivery rate dashboard
  @todo
  Scenario: Delivery rate dashboard
    Then the dashboard shows at least 98 % delivered during the pilot
    Or lists every failure with its reason (exit gate 4)
