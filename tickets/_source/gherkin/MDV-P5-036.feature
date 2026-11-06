Feature: Patient app: SMS sign-in, family profiles, home, booking, check-in and reschedule
  As patient, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Patient app: SMS sign-in, family profiles, home, booking, check-in and
  @todo
  Scenario: Check 1
    Given a patient books from the app
    When a reception booking takes the same slot
    Then only one succeeds and the other sees the next free slots
  @todo
  Scenario: Check 2
    Given a family member is viewed
    When the proxy has expired
    Then access ends and the patient sees why
  @todo
  Scenario: Check 3
    Given the phone is offline
    When the patient books
    Then the booking is queued and shown as not yet confirmed
  @todo
  Scenario: Check 4
    Given the SMS code arrives
    When the patient opens the app
    Then the code is filled in automatically on iOS and Android
