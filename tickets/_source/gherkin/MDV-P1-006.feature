Feature: Practice settings: notification preferences and data tab
  As a practice admin, I want to control notifications and see data residency, so that patients get the right messages and the clinic's data location is clear.

  # chunk: Add notification preferences and data tab
  @todo
  Scenario: Turning off a reminder type stops scheduling
    Given reminders are queued for future appointments
    When the admin turns off a reminder type
    Then no future reminders of that type are scheduled
    And already-queued ones are cancelled

  @todo
  Scenario: Templates cannot include clinical fields
    When an admin previews a template
    Then only placeholders are shown
    And validation rejects clinical merge fields

  @todo
  Scenario: Quiet hours defer SMS
    Given quiet hours are configured
    When an SMS is due inside the window
    Then the job reschedules it to the end of the window

  @todo
  Scenario: Residency and country are read-only
    When an admin opens the Data tab
    Then the tenant country and residency are displayed read-only with the trust-centre wording
