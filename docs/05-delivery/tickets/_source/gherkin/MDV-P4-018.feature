Feature: Overdue escalation: escalate to doctor, reassign, or close with reason (C-04)
  As a care coordinator, I want to escalate, reassign or close overdue tasks, so that none are forgotten.

  # chunk: Add overdue list and escalation
  @todo
  Scenario: Escalation within 1 minute
    Given an overdue task
    When it is escalated to the doctor
    Then an inbox item and notification reach the responsible clinician within 1 minute
  @todo
  Scenario: Sorting and filtering
    Given overdue tasks
    When the list is viewed
    Then it sorts by severity and days late
    And filters by owner
  @todo
  Scenario: Close with reason audited
    Given an overdue task
    When it is closed
    Then a reason is required and the closure is audited
