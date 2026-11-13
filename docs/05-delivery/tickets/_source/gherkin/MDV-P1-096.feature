Feature: A screen the role may not open says so, with a button to the start page
  The server refuses correctly, but the screen says Page not found.

  # chunk: A screen the role may not open says so, with a button to the start page
  @todo
  Scenario: Refused is not missing
    Given a receptionist
    When she opens /today, /settings or a consultation address
    Then the page says her role does not include this screen and offers the start page

  @todo
  Scenario: Missing is still missing
    Given any role
    When an address that does not exist is opened
    Then the page says Page not found

  @todo
  Scenario: The server still refuses
    Given the same role
    When the API behind the screen is called directly
    Then it answers 403, and the authorisation tests for those endpoints are unchanged

  @todo
  Scenario: One message for the nurse
    Given a nurse opening a consultation
    When the page renders
    Then one denied message shows and no red error lines

  @todo
  Scenario: Nothing leaks
    Given the denied page
    When its text and address are read
    Then they hold no patient name, MRN or clinical word

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs
    Then there is no violation and the message has one h1
