Feature: Schedule day view opens at the current time; the past collapses into one row
  In the afternoon every visible row is Passed.

  # chunk: Schedule day view opens at the current time; the past collapses into one row
  @todo
  Scenario: Opens at now
    Given today's day view at 15:00 clinic time
    When it opens
    Then the first visible slot is within one hour of now

  @todo
  Scenario: The past is one row
    Given passed slots with appointments
    When the day opens
    Then they are one collapsed row with a count, and opening it shows each appointment

  @todo
  Scenario: More room for appointments
    Given 1024 by 768
    When the day view loads
    Then the first slot starts at or above 240 px

  @todo
  Scenario: Booking still works from an empty slot
    Given an empty future slot
    When it is chosen
    Then the booking panel opens for that time as today

  @todo
  Scenario: Keyboard
    Given the collapsed row
    When it is focused
    Then Enter opens it and it states collapsed or expanded to assistive technology

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs
    Then there is no violation
