Feature: Disaster-recovery drill proving RPO ≤ 15 min and RTO ≤ 4 h
  As a developer, I want a DR drill, so that recovery targets are proven.

  # chunk: Run disaster-recovery drill
  @todo
  Scenario: RPO and RTO measured
    Given a simulated region or database loss
    When restored
    Then measured RPO ≤ 15 min and RTO ≤ 4 h with timestamps recorded
  @todo
  Scenario: Jobs resume without duplicates
    Given queued jobs
    When restored
    Then reminders, AI and integrations resume without duplicates
  @todo
  Scenario: Runbook updated
    Given the drill
    When complete
    Then the DR runbook is updated and reviewed
