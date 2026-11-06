Feature: Backups, point-in-time restore and a timed restore drill
  As a developer, I want backups and a proven restore, so that data can be recovered within a known time.

  # chunk: Define PITR, LTR and in-country backup redundancy in Bicep
  @todo
  Scenario: Backup settings are code
    Then PITR and LTR settings are defined in Bicep, not set by hand

  @todo
  Scenario: Backups stay in the country
    Then backup storage redundancy keeps data in-country
    And there is no geo-replication outside the country

  # chunk: Run and time a staging restore drill
  @todo
  Scenario: Restore drill is timed and recorded
    When a restore drill of staging completes
    Then the elapsed time is recorded in the runbook (P0 exit gate 2)

  @todo
  Scenario: Restored database passes a smoke test
    When the restored database is smoke-tested
    Then row counts per table match the point in time
