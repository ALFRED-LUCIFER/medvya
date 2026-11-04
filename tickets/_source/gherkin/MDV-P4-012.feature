Feature: Cash reconciliation and day-end close with denominations, second-person confirmation and shift report (P-07, P-13)
  As a receptionist, I want to count the drawer and close the day, so that cash variance is explained.

  # chunk: Add cash session close with variance confirmation
  @todo
  Scenario: Expected cash formula
    Given a cash session
    When expected cash is calculated
    Then it equals cash receipts minus cash refunds plus float
  @todo
  Scenario: Variance needs second person
    Given a counted total that differs from expected
    When the day is closed
    Then a note and a second, different staff user's confirmation are required
  @todo
  Scenario: UAE rounding is not variance
    Given UAE cash rounding lines (always down to 25 fils)
    When the summary is built
    Then they are listed and already reduce expected cash
    And never show as variance

  # chunk: Add immutable closed sessions and pilot variance target
  @todo
  Scenario: Closed sessions immutable
    Given a closed session
    When a correction is needed
    Then an adjustment entry is created and the session is unchanged
  @todo
  Scenario: Ten days zero variance
    Given pilot clinics
    When they close for 10 consecutive days
    Then each close has zero unexplained variance (exit gate 1, tracked in MDV-P4-033)
