Feature: Field and ErrorSummary in the shared library: one label, hint, error and required convention; a summary that takes focus
  Five Field wrappers, three error styles and three required or optional conventions exist.

  # chunk: Field and ErrorSummary in the shared library: one label, hint, error and required convention; a summary that takes focus
  @todo
  Scenario: Summary takes focus
    Given a sample form in the gallery with three invalid fields
    When it is submitted
    Then focus moves to the summary, which lists three links, and activating a link focuses that field

  @todo
  Scenario: Errors are visible at 375
    Given the same form at 375 px
    When it is submitted empty
    Then the summary is in the viewport

  @todo
  Scenario: Errors say what to do
    Given a Field with an error
    When it renders
    Then the message is text beside the field, linked to the control, and the control is marked invalid without relying on colour

  @todo
  Scenario: One convention per form
    Given a form set to mark required fields
    When it renders
    Then every required field carries the word in the label and no asterisk stands alone

  @todo
  Scenario: Announced once
    Given a screen reader
    When the summary appears
    Then it is announced once and not repeated on every keystroke

  @todo
  Scenario: Accessible and mirrored
    Given the gallery in English and Arabic
    When axe runs
    Then there is no violation
