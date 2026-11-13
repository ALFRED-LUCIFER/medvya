Feature: Later: date and time pickers and a combobox in the app language; dark and high contrast only after the tone tokens
  Native date inputs follow the browser's language, not the app's: an Arabic screen can show an English-format date widget.

  # chunk: Later: date and time pickers and a combobox in the app language; dark and high contrast only after the tone tokens
  @todo
  Scenario: Follows the app language
    Given the app in Arabic on a browser set to English
    When a date field opens
    Then the picker is in Arabic, right-to-left, and the stored value is the same ISO date

  @todo
  Scenario: Typing still works
    Given a date of birth field
    When a date is typed by keyboard
    Then it is accepted without opening the picker and an impossible date is refused with a message

  @todo
  Scenario: No native date inputs left
    Given features
    When searched
    Then no input of type date or time remains

  @todo
  Scenario: Combobox by keyboard
    Given the patient picker
    When it is used by keyboard
    Then arrow keys, Enter and Escape work and each option is announced with name and date of birth

  @todo
  Scenario: Walk-in not longer
    Given the walk-in and registration paths
    When measured
    Then the click counts did not rise

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs with the picker open
    Then there is no violation
