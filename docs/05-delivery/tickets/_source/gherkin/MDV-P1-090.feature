Feature: Ending or signing with an unfinished prescription or order asks first; unfinished ones show on the doctor's day
  End consultation is one click with no dialog.

  # chunk: Ending or signing with an unfinished prescription or order asks first; unfinished ones show on the doctor's day
  @todo
  Scenario: End never completes silently
    Given a draft prescription
    When the doctor presses End consultation
    Then a dialog names the patient and the draft and nothing ends until a choice is made

  @todo
  Scenario: Sign asks too
    Given unplaced orders
    When the doctor presses Sign note
    Then the same dialog lists them before the sign dialog

  @todo
  Scenario: Unfinished work is listed
    Given End anyway was chosen
    When the doctor opens Today
    Then Prescriptions to authorise shows the patient and opens the consultation at the prescription

  @todo
  Scenario: A draft can be finished later
    Given an ended visit with a draft
    When the same doctor completes and authorises it
    Then it is authorised with its own audit record; another doctor without a care relationship is refused by the server

  @todo
  Scenario: Discard is recorded
    Given Discard the draft with a reason
    When the audit log is read
    Then it holds who, when and the reason, and the draft no longer appears on Today

  @todo
  Scenario: Server rules proven
    Given the integration tests for authorise-after-end
    When the same-doctor rule or the ended-visit rule is removed
    Then a test fails

  @todo
  Scenario: Four separate acts
    Given a visit taken to the end
    When the audit log is read
    Then sign, authorise, place orders and end are four separate events

  @todo
  Scenario: Keyboard, languages, axe
    Given the dialog
    When it opens
    Then focus is inside it, Escape cancels, and axe finds no violation in English or Arabic
