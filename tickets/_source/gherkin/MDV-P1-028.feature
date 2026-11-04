Feature: Reception board with visit columns and one-tap check-in
  As a receptionist, I want a board of today's visits with quick check-in, so that I can run the waiting room.

  # chunk: Add reception board with check-in
  @done
  Scenario: Check-in takes at most 3 interactions and is instrumented
    Given a returning patient with an appointment
    When the receptionist checks them in
    Then it takes at most 3 interactions
    And median check-in time is instrumented from arrival click to checked-in

  @done
  Scenario: Invalid column moves are rejected
    When a card is moved against the visit state machine
    Then the server rejects it

  @done
  Scenario: Cards never show clinical information
    Then reasons are shown only as a category
    And a security test on the board API payload verifies it

  @done
  Scenario: Billing column is a placeholder
    Then the Billing column exists
    And payment actions are placeholders until P4 behind a feature flag

  @done
  Scenario: Board works at tablet width
    Given tablet width
    Then the board scrolls horizontally per the responsive rules
