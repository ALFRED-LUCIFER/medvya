Feature: Leave and schedule exceptions with re-offer of affected appointments
  As a receptionist, I want leave to require an action for each affected booking, so that no patient is left with a cancelled slot unknowingly.

  # chunk: Add leave with affected-appointment handling
  @todo
  Scenario: Leave overlapping bookings requires an action per appointment
    Given booked appointments overlap the leave range
    When the user saves the leave
    Then the count and list are shown
    And an action is required for each appointment before the leave is saved

  @todo
  Scenario: Affected patients receive a rebooking message
    When the leave is saved
    Then affected patients receive a rebooking message with no clinical details
    And the appointments move to "needs rebooking" on the board

  @todo
  Scenario: Extra sessions create slots for that day only
    When an extra session is added for a day
    Then bookable slots exist for that day only

  @todo
  Scenario: Leave and exceptions are audited
    When leave or an exception is saved
    Then it is audited
