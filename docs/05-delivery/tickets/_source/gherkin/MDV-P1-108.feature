Feature: UX validation sessions with the pilot doctor, nurse, receptionist and owner: the 21 decisions, the brand list, the training date and the device check
  No clinician, nurse, receptionist or owner took part in the audit.

  # chunk: UX validation sessions with the pilot doctor, nurse, receptionist and owner: the 21 decisions, the brand list, the training date and the device check
  @todo
  Scenario: Rows 1 to 5 first
    Given the validation plan
    When the first safety tickets are ready to merge
    Then decisions 1 to 5 are recorded with role and date on the tickets they block

  @todo
  Scenario: All 21 decisions recorded
    Given the plan's table
    When this gate closes
    Then each of the 21 rows has an answer, a name by role and a date, on the blocked ticket and summarised here

  @todo
  Scenario: Identity question passes
    Given the doctor's session on the changed consultation
    When asked whose record is this and what is she allergic to without scrolling
    Then the answer is right three times out of three

  @todo
  Scenario: No silent allergy miss
    Given the doctor's session
    When the usual antibiotic is prescribed for the penicillin-allergic synthetic patient
    Then no medicine of that class is authorised without a stop the doctor noticed

  @todo
  Scenario: The desk recovers
    Given the receptionist's session
    When she presses Call in on the wrong patient
    Then she recovers without help

  @todo
  Scenario: People items delivered
    Given this gate
    When it closes
    Then the brand list is attached to the brand ticket, the training date is written on the navigation names ticket and the device check is written here

  @todo
  Scenario: Synthetic only
    Given every session
    When notes and screenshots are stored
    Then they hold synthetic patients only and no real person's health data
