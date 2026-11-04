Feature: Portal consent and privacy controls with policy version and change log (R-03)
  As a patient, I want to control my consents, so that my privacy choices are respected and recorded.

  # chunk: Add consent switches with receipts
  @todo
  Scenario: Receipt per toggle
    Given a consent switch
    When the patient toggles it
    Then a new receipt is created
    And the history is visible to the patient
  @todo
  Scenario: WhatsApp off stops messages
    Given WhatsApp reminders are turned off
    When the next reminder is due
    Then no WhatsApp message is sent
  @todo
  Scenario: Recording preference at next visit
    Given recording preference was withdrawn
    When the next visit's consent step is reached
    Then the change is reflected
    And consent is still asked per visit
