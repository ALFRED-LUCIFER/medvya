Feature: Clinician quality metrics: minor-edit rate, trust survey and note time vs P2 baseline
  As a founder, I want quality metrics for AI drafts, so that the exit gate rests on evidence.

  # chunk: Report minor-edit rate, trust survey and note time
  @todo
  Scenario: Minor-edit dashboard
    Given signed drafts
    When the dashboard is viewed
    Then it shows % signed with minor edits per clinician and overall (target 80 % or more)
  @todo
  Scenario: Trust survey
    Given opt-in clinicians
    When the survey closes
    Then all have completed it and results are summarised
  @todo
  Scenario: Note time vs P2 baseline
    Given time to signed note
    When compared with the P2 baseline
    Then the method is stated
