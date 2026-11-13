Feature: Consultation workspace layout: section anchors, vitals in one row, prescription after assessment, diagnosis inline, chart panel beside the note
  One simple visit takes about 24 clicks, 9 typed fields and 5 route changes.

  # chunk: Anchors, vitals row and the note column
  @todo
  Scenario: First field near the top
    Given an open consultation with nurse vitals at 1440 by 900 and 1024 by 768
    When the page loads
    Then the first text box starts at or above 200 px

  @todo
  Scenario: Diagnosis without leaving
    Given the Assessment section
    When the doctor adds a coded diagnosis
    Then the route does not change and the typed note text is still on screen

  @todo
  Scenario: Chart reachable in one action on a tablet
    Given 768 by 1024
    When the doctor opens the chart from the strip
    Then the drawer shows allergies, medicines, problems and results in one action and closes back to the same scroll position

  # chunk: Prescription after assessment and diagnosis inline
  @todo
  Scenario: Shorter page
    Given an empty note at 1440 by 900
    When the page height is measured
    Then it is under two viewports

  @todo
  Scenario: Four separate acts
    Given a note with a draft prescription and an unplaced order
    When the doctor presses Review and sign
    Then only the note is signed, and authorising, placing orders and ending each still need their own action and write their own audit record

  @todo
  Scenario: Allergy result beside the line
    Given a medicine line for a patient with a recorded allergy
    When it is added through the Add medicine dialog
    Then the check result is shown in words beside that line

  # chunk: Chart panel beside the note and as a drawer; checks at three widths
  @todo
  Scenario: No unearned tick
    Given a newly opened consultation
    When the section anchors and checklist are read
    Then no section or item is marked complete

  @todo
  Scenario: Nothing under pinned chrome
    Given every control in the workspace
    When it is focused by keyboard
    Then it is fully visible, and pinned chrome takes at most 30 % of the height at 1024 by 768

  @todo
  Scenario: Guided steps keep every word
    Given text typed in the one-page view
    When the doctor switches to guided steps and back
    Then every word is there

  @todo
  Scenario: Languages and axe
    Given English and Arabic at 1440, 1024 and 768
    When axe runs
    Then there is no violation and the layout mirrors
