Feature: The doctor's follow-up reaches the front desk: Follow-up due on the Done card and the desk record, with Book
  The doctor's screen says The front desk will book it from Tuesday, 17 November 2026.

  # chunk: The doctor's follow-up reaches the front desk: Follow-up due on the Done card and the desk record, with Book
  @todo
  Scenario: The desk sees it
    Given the doctor set a follow-up of 2 weeks and ended the visit
    When the receptionist opens the board and the patient's desk record
    Then both show Follow-up due from the date with Book

  @todo
  Scenario: Book is prefilled
    Given the line
    When Book is pressed
    Then the booking panel opens on that date with the patient and doctor chosen

  @todo
  Scenario: It clears
    Given the follow-up is booked or dismissed with a reason
    When the board refreshes
    Then the line is gone and a dismissal is audited

  @todo
  Scenario: Date only
    Given a desk role
    When the follow-up is returned by the API
    Then it holds the date and doctor and no note text, and the test fails if clinical text is added

  @todo
  Scenario: MDV-P2-043 is true
    Given that ticket's line The follow-up choice appears on the front desk's list for that patient
    When the journey runs
    Then it passes on the real stack

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on the card and the record
    Then there is no violation
