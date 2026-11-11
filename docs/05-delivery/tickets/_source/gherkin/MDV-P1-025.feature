Feature: Walk-in registration and booking straight to the queue
  As a receptionist, I want to register and check in a walk-in quickly, so that waiting patients join the queue without delay.

  # chunk: Add walk-in to the board
  @done
  Scenario: Unscheduled walk-in when no slot is free
    Given no slot is free
    When a walk-in is added
    Then the walk-in is queued without breaking the no-double-booking rule
    And unscheduled walk-ins do not occupy slots

  @done
  Scenario: Walk-ins appear in Checked in
    When a walk-in is checked in
    Then it appears in the Checked in column with wait time and the Walk-in tag
