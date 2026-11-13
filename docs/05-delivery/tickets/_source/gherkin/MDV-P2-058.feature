Feature: Move the forms to Field and ErrorSummary, one feature at a time: registration first
  After an empty submit on registration, focus stays on the button and at 375 px the errors are off screen.

  # chunk: Registration and walk-in
  @todo
  Scenario: Focus goes to the errors
    Given Register patient submitted empty at 375 px and 1440 px
    When the submit fails
    Then focus is on the summary, it is in the viewport, and each link moves focus to its field

  @todo
  Scenario: One convention per form
    Given each migrated form
    When it renders
    Then required or optional is marked one way throughout, in words

  @todo
  Scenario: No silent disabled buttons
    Given walk-in before a patient is chosen
    When Check in now is pressed or read
    Then the reason is stated in words

  # chunk: Settings, onboarding, sign-up, pregnancy card and the platform console
  @todo
  Scenario: Safety steps unchanged
    Given registration and check-in
    When a duplicate or an identity confirmation applies
    Then they behave exactly as before, and the walk-in path is at most 6 clicks new and 3 returning

  @todo
  Scenario: No local Field left
    Given features
    When searched
    Then no local Field component remains and the lint rule is an error

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on each migrated form with errors showing
    Then there is no violation
