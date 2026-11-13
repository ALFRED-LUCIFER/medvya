Feature: Settings sub-navigation and small copy and state fixes: walk-in column name, Terms status, nurse-intake switch, organisation page
  Small things that make the product look unsure of itself: a success message that names the wrong column, a status that says booking is paused while booking works, a switch that changes everyone's board mid-day with no warning, a page that calls an endpoint answering 404, and two tab rows in two styles.

  # chunk: Walk-in column name, Terms status, nurse-intake confirm, organisation page
  @todo
  Scenario: The real column is named
    Given nurse intake off
    When a walk-in is checked in
    Then the success message names Ready for doctor

  @todo
  Scenario: Terms status is true
    Given Settings → Terms
    When booking is allowed
    Then the status does not say booking is paused; when it is paused, then booking is refused by the server

  @todo
  Scenario: The switch asks first
    Given patients with the nurse
    When the owner switches nurse intake off
    Then a dialog says how many are with the nurse and what happens to them, and nothing changes until confirmed

  @todo
  Scenario: No 404 on the organisation page
    Given an approved clinic
    When /organisation opens
    Then no request answers 404 and the page says Approved or is not in the menu

  # chunk: Settings left sub-navigation
  @todo
  Scenario: One settings navigation
    Given 1024 px and wider
    When Settings opens
    Then a left sub-navigation replaces the second tab row and each ?tab= address opens the same panel as before

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on each changed screen
    Then there is no violation
