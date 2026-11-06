Feature: Create an appointment from the calendar with a short slot hold
  As a receptionist, I want to hold a slot while I book, so that nobody else books it at the same time.

  # chunk: Add slot hold and booking from the calendar
  @done
  Scenario: Opening the booking panel creates a hold
    When a receptionist opens the booking panel for a slot
    Then a hold is created
    And another user sees the slot as "being booked" and cannot confirm it

  @done
  Scenario: Holds expire automatically
    Given a hold exists
    When the configured time passes
    Then the slot becomes free

  @done
  Scenario: Unavailable column cannot be booked
    Given the doctor is not working
    Then booking is impossible from both the UI and the API

  @done
  Scenario: Booking writes event, audit and board card
    When a booking is confirmed
    Then AppointmentBooked.v1 with no PHI and an audit event are written
    And the appointment appears on the board in Booked

  # chunk: Meet the booking latency budget
  @todo
  Scenario: Booking API latency under the P1 load profile
    Given the P1 load profile (MDV-P1-037)
    Then booking API p95 is under 300 ms
