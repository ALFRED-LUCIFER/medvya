Feature: Navigation names in one batch: My day, Front desk, one Patient record with tabs, Register patient as a button
  Today is the doctor's day for doctors and the reception board for nurses.

  # chunk: Navigation names in one batch: My day, Front desk, one Patient record with tabs, Register patient as a button
  @todo
  Scenario: One name per screen
    Given every role
    When the menu and each page title are read
    Then the doctor's day is My day, the desk surface is Front desk, and no screen has two names

  @todo
  Scenario: No duplicate menu items
    Given a nurse with intake off
    When the menu renders
    Then no two items open the same screen

  @todo
  Scenario: One patient record
    Given a doctor and a receptionist opening the same patient
    When the record opens
    Then both see Patient record, the doctor with Summary and the receptionist with Details, and the receptionist's tabs hold no clinical data

  @todo
  Scenario: Register is a button
    Given Front desk and Patients
    When each renders
    Then Register patient is a button there and not a menu item, and the walk-in path is still at most 6 clicks new and 3 returning

  @todo
  Scenario: Old addresses work
    Given a bookmarked old address
    When it is opened
    Then the same screen opens

  @todo
  Scenario: Three languages, one batch
    Given en, hi and ar
    When the names are read
    Then all changed together, Arabic mirrors and axe finds no violation

  @todo
  Scenario: Lands at a training boundary
    Given the issue
    When it is merged
    Then it records the training date and that the merge is before training or after the first pilot weeks
