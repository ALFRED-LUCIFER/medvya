Feature: Front desk: a list view as a third view beside By status and By doctor; the receptionist chooses the default
  The board shows about one and a half cards per column on a first screen; a grouped list shows about nine patients in the same space.

  # chunk: Front desk: a list view as a third view beside By status and By doctor; the receptionist chooses the default
  @todo
  Scenario: A third view, not a replacement
    Given Front desk
    When the view switch is read
    Then By status, By doctor and List are offered and the first two behave as before

  @todo
  Scenario: Who needs action comes first
    Given 20 booked and 6 waiting patients
    When the list opens
    Then the waiting group is first and done is collapsed

  @todo
  Scenario: Counts filter
    Given the status counts
    When one is pressed
    Then only that group shows and the pressed state is exposed to assistive technology

  @todo
  Scenario: Tested with a real day
    Given 40 rows with long names at 1024 by 768 and 1440 by 900
    When the list renders
    Then no text is clipped and no sideways scrolling occurs

  @todo
  Scenario: Rows are comfortable
    Given any pointer
    When rows and actions are measured
    Then rows are at least 48 px and actions at least 44 px with at least 8 px between actions in adjacent rows

  @todo
  Scenario: Check-in keeps the identity confirm
    Given a search result in the list
    When Check in is pressed
    Then name and date of birth are confirmed first, as on the board

  @todo
  Scenario: Same order as the display
    Given the clinic's queue rule
    When the list and the queue display are compared
    Then the order within a status is the same

  @todo
  Scenario: Languages and axe
    Given English and Arabic at 1440, 1024 and 375
    When axe runs
    Then there is no violation
