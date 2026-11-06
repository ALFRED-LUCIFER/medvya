Feature: Patient contact log and reminders: outcome required; "delivered" is not a contact (C-03)
  As a care coordinator, I want to log contact attempts with an outcome, so that a delivered message is never mistaken for a contact.

  # chunk: Add contact log with required outcome
  @todo
  Scenario: Save disabled until selected
    Given the contact log form
    When channel and outcome are not both selected
    Then Save outcome is disabled
  @todo
  Scenario: Delivered is not a contact
    Given a message delivered
    When the task is checked
    Then it does not move to Patient contacted
    And the microcopy shows "Delivered — this does not complete the task"
  @todo
  Scenario: Contact history shown
    Given logged contacts
    When the task or patient record is opened
    Then the contact history is shown
