Feature: Doctor Today (D-01): schedule, patient flags, pending notes, inbox counts
  As a doctor, I want a Today screen with my schedule, flags and pending notes, so that I start the day from one place.

  # chunk: Show my schedule and the clinic view on Today
  @todo
  Scenario: Mine by default, clinic when toggled
    Given a signed-in doctor with appointments and colleagues with their own
    When the doctor opens Today
    Then only their own appointments are shown
    And toggling to the clinic view shows the clinic schedule where permissions allow
  @todo
  Scenario: Status chips update live
    Given Today is open
    When a patient's board status changes
    Then the chip updates live and shows an icon plus a label

  # chunk: Add pending notes and open-patient actions
  @todo
  Scenario: Unsigned notes from previous days stay pending
    Given a note from a previous day is unsigned
    When the doctor opens Today
    Then it appears under pending notes until it is signed
  @todo
  Scenario: Open patient and start consultation
    Given a patient in today's schedule
    When the doctor clicks the patient
    Then Patient 360 opens for that encounter
    And "Start consultation" starts or opens the encounter

  # chunk: Add mobile Today with next patient hero
  @todo
  Scenario: Mobile keeps the consult button in thumb reach
    Given the doctor uses a phone
    When Today is shown
    Then the next patient hero and the consult button are within thumb reach
