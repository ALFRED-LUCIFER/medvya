Feature: Recurring appointments (series) with per-occurrence exceptions
  As a receptionist, I want to book a series of appointments, so that repeat visits are booked in one go.

  # chunk: Add series conflict review and independent occurrences
  @todo
  Scenario: Conflicting occurrences are shown before confirming
    When a series is created
    Then conflicting occurrences are shown
    And the user can skip or move them before confirming

  @todo
  Scenario: Each occurrence is protected by double-booking rules
    Then each occurrence is an independent appointment protected by the double-booking rules

  # chunk: Add cancel this and following
  @todo
  Scenario: Cancel "this and following"
    When a receptionist cancels "this and following"
    Then the remaining series is cancelled with one reason and one notification
