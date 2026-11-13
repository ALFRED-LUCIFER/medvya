Feature: The nurse's chief complaint prefills the doctor's note, marked from nurse intake
  The nurse typed Fever and sore throat for three days and saved.

  # chunk: The nurse's chief complaint prefills the doctor's note, marked from nurse intake
  @todo
  Scenario: Prefilled and marked
    Given the nurse saved a complaint at intake
    When the doctor opens the consultation
    Then Chief complaint holds that text with the mark from nurse intake

  @todo
  Scenario: Never overwrites the doctor
    Given the doctor already typed a complaint
    When intake is saved later
    Then the doctor's text stays

  @todo
  Scenario: The doctor's edit is the doctor's
    Given the prefilled text
    When the doctor changes it
    Then the mark shows the doctor edited it and the signed note records both sources

  @todo
  Scenario: Blank stays blank
    Given no complaint at intake
    When the consultation opens
    Then the field is empty and nothing is guessed

  @todo
  Scenario: Announced and accessible
    Given a screen reader
    When the field is focused
    Then the source is read; axe finds no violation in English or Arabic
