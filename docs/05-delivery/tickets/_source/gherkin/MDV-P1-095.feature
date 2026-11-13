Feature: Front-desk board: one header row, a one-line workload strip, pinned column headers and the doctor on the card
  The first card starts at about 452 px at every width from 768 to 1440: 41 % of a 1024 by 768 screen is left for patients.

  # chunk: Front-desk board: one header row, a one-line workload strip, pinned column headers and the doctor on the card
  @todo
  Scenario: Queue on the first screen
    Given nine visits at 1024 by 768
    When the board loads
    Then the first card starts at or above 220 px

  @todo
  Scenario: Nothing spills
    Given widths from 768 to 1440 px and a long patient name
    When the board renders
    Then no text is outside its card and the action label is on one line

  @todo
  Scenario: Whose patient
    Given two doctors working today
    When a card renders
    Then it shows the doctor's short name in words; given one doctor, then it does not

  @todo
  Scenario: Headers stay
    Given a long column
    When it is scrolled
    Then its header with name and count stays visible

  @todo
  Scenario: Phone is a list
    Given 375 px
    When the board opens
    Then it is one list with status tabs and no sideways scrolling

  @todo
  Scenario: Walk-in is not longer
    Given the walk-in path after this change
    When it is measured
    Then a new patient takes at most 6 clicks and a returning patient at most 3

  @todo
  Scenario: One primary action
    Given the board for a doctor
    When the first viewport is counted
    Then at most one filled primary button is visible

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs at 1440, 1024 and 375
    Then there is no violation
