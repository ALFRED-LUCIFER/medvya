Feature: Patient record and list chrome found only in the reviewers' reports: empty sections collapse, one-row filters, sideways scrolling inside tables
  The patient record is 3,241 to 5,129 px tall and the doctor scrolls past six Not recorded tiles to reach the medicines; its pinned filter takes 156 px of a phone.

  # chunk: Patient record and list chrome found only in the reviewers' reports: empty sections collapse, one-row filters, sideways scrolling inside tables
  @todo
  Scenario: Shorter record
    Given a patient with six empty sections at 1440 by 900
    When the record opens
    Then each empty section is one line with its add action and the medicines start within the first two viewports

  @todo
  Scenario: Allergy never collapses
    Given allergies not recorded
    When the record renders
    Then the allergy block is shown in full with its words

  @todo
  Scenario: Filter fits a phone
    Given 375 px
    When the record scrolls
    Then the pinned filter is at most 56 px tall

  @todo
  Scenario: Completed visit says so once
    Given a completed visit's panel
    When it opens
    Then one line says the visit is completed and no disabled button repeats a reason

  @todo
  Scenario: Dose is never hidden
    Given a prescription with three lines at 768 px
    When it renders
    Then dose, frequency and duration of every line are visible with no sideways scrolling

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs
    Then there is no violation
