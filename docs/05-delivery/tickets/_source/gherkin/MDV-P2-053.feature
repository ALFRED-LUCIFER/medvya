Feature: Clinic today for the owner: five counts the system can stand behind and a needs-attention list
  The owner, who is the buyer, has four 14-day numbers inside Settings and nothing on how the day is going.

  # chunk: Clinic today for the owner: five counts the system can stand behind and a needs-attention list
  @todo
  Scenario: Only earned numbers
    Given the page
    When it is read
    Then it shows the five counts above and no seen, median consult, running late or revenue figure

  @todo
  Scenario: Not tracked is said
    Given revenue
    When the page renders
    Then it reads Not tracked yet; given a count with no data, then it reads No data yet and not 0

  @todo
  Scenario: Each attention line acts
    Given a needs-attention line
    When its action is pressed
    Then the matching screen opens filtered to that item

  @todo
  Scenario: Counts match the source
    Given the board and the doctors' days
    When the counts are compared at the same moment
    Then they are equal

  @todo
  Scenario: Authorised and scoped
    Given a receptionist or another clinic's owner
    When the page's API is called
    Then the server answers 403 or returns only their own clinic, and the test fails if the policy is removed

  @todo
  Scenario: No patient data
    Given the page and its API answers
    When they are read
    Then they hold counts and staff names only

  @todo
  Scenario: Phone to desktop, languages, axe
    Given 375, 768 and 1440 in English and Arabic
    When axe runs
    Then there is no violation
