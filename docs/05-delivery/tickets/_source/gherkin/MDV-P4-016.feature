Feature: Care plans and tasks with owners, due dates, stage tracks and evidence to close (C-02)
  As a care coordinator, I want care plan tasks with owners and evidence to close, so that care loops are truly closed.

  # chunk: Add care task lifecycle with reasons
  @todo
  Scenario: Lifecycle reasons
    Given a task
    When it moves to BLOCKED, OVERDUE, CANCELLED or UNREACHABLE
    Then a reason and actor are required
  @todo
  Scenario: Reminders never complete
    Given a reminder sent or message delivered
    When the task is checked
    Then no stage is completed and the task is not closed

  # chunk: Add evidence and My tasks
  @todo
  Scenario: Evidence to close
    Given the final stage of a task
    When it is completed
    Then evidence (linked record or log entry) is required
  @todo
  Scenario: My tasks and audited reassignment
    Given tasks with owners
    When an owner opens My tasks
    Then they see their tasks
    And reassignment is audited
