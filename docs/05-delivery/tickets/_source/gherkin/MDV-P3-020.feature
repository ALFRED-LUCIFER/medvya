Feature: Dictation of note sections with the same review-and-approve rule
  As a doctor, I want to dictate note sections, so that dictated text is still reviewed before it becomes part of the record.

  # chunk: Add section dictation as AI draft
  @todo
  Scenario: Dictated text is a draft
    Given dictated text in a section
    When shown
    Then it appears as "AI draft · not reviewed" and must be accepted or edited
  @todo
  Scenario: Separate audio retention
    Given dictation audio
    When stored
    Then it follows the same retention policy and is never mixed with the consultation recording
  @todo
  Scenario: Desktop and mobile
    Given desktop and mobile web
    When dictating
    Then it works on both
