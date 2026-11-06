Feature: Front-desk fixes from the live walk-through, round 2
  As a receptionist or practice admin, I want the front desk to behave consistently, so that daily work has no surprises.

  # chunk: Link queue displays from Settings and fix the TV copy
  @todo
  Scenario: A display can be created, paired, shown and revoked
    Given an admin opens Settings
    Then Queue displays is reachable from it
    And a display can be created, paired, shown with tokens only and revoked
    And the TV page tells staff where to add a screen

  # chunk: Keep a cancelled card from covering the freed slot
  @todo
  Scenario: The freed slot is bookable
    When an appointment is cancelled
    Then the slot is free and clickable by mouse and keyboard
    And the cancelled card leaves the tab order at once

  # chunk: Tighten walk-in rules
  @todo
  Scenario: Duplicate check-in warns
    When the same patient is checked in again
    Then the desk is warned before a second visit is made

  @todo
  Scenario: Order and success
    Then Next patients follows the token order
    And the success screen shows the token

  @todo
  Scenario: Quick registration validates as a whole
    Then all errors show together and a malformed mobile is caught before the server

  # chunk: Polish the schedule screen
  @todo
  Scenario: Ruler, address and panel
    Then the time ruler matches the slot length
    And the selected date is in the address
    And a stale appointment panel closes when the day or view changes

  # chunk: Make the access log readable
  @todo
  Scenario: Noise and names
    Then schedule and notification reads do not flood the log
    And action names are translated

  # chunk: Show reminders and allow consent after quick registration
  @todo
  Scenario: Reminder visibility and later consent
    Then the visit panel says when reminders are scheduled
    And a reminder consent can be added to a quick-registered patient

  # chunk: Show a revoked invitation clearly
  @todo
  Scenario: Revoked state
    Then a revoked invitation shows as revoked in the status column

  # chunk: Remove loading delays and copy slips
  @todo
  Scenario: Polish
    Then page loads do not sit on Loading for seconds
    And developer-facing text and plural slips are removed
